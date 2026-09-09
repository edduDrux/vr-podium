using System.Collections;
using System.Globalization;
using Podium.Api;
using UnityEngine;

namespace Podium.Samples
{
    /// <summary>
    /// Percorre o fluxo inteiro da API a partir de arquivos em disco: init, áudio,
    /// analyze, polling e feedback.
    /// <para>
    /// Existe para tirar o hardware do caminho crítico. Se este teste fecha no Editor,
    /// contra o `docker compose` local, então a integração está de pé e o que falta é
    /// conteúdo — cena, avatares, interação. Depurar rede com o headset na cabeça é a
    /// pior forma de descobrir que o IP estava errado.
    /// </para>
    /// Uso: crie um GameObject vazio, adicione este componente, aponte o
    /// PodiumApiConfig e os dois caminhos de arquivo, e dê Play.
    /// </summary>
    public class PodiumSmokeTest : MonoBehaviour
    {
        [Header("Configuração")]
        [SerializeField] private PodiumApiConfig config;

        [Header("Entradas (caminhos absolutos no PC)")]
        [SerializeField] private string presentationPath =
            @"C:/Users/eduar/projetos/podium/storage/_fixtures/tcc_slides.pdf";
        [SerializeField] private string audioPath =
            @"C:/Users/eduar/projetos/podium/storage/_fixtures/fala.wav";

        [Header("Parâmetros da sessão")]
        [SerializeField] private string persona = PersonaValues.ProfessorRigoroso;
        [SerializeField] private string scenario = ScenarioValues.SalaDeAula;

        [SerializeField] private bool runOnStart = true;

        [Tooltip("Desligado, o teste para depois do upload: valida init e áudio sem gastar cota de IA. O /analyze é a única etapa que custa (STT + geração).")]
        [SerializeField] private bool runAnalysis = true;

        private PodiumApiClient api;

        /// <summary>
        /// Permite ligar o config por código — é assim que a ferramenta de Editor monta a
        /// cena pronta, e é o que um teste automatizado usaria.
        /// </summary>
        public PodiumApiConfig Config
        {
            get { return config; }
            set { config = value; }
        }

        private void Start()
        {
            if (runOnStart)
            {
                Run();
            }
        }

        [ContextMenu("Rodar teste de fumaça")]
        public void Run()
        {
            api = gameObject.GetComponent<PodiumApiClient>();
            if (api == null)
            {
                api = gameObject.AddComponent<PodiumApiClient>();
            }

            api.Config = config;
            StartCoroutine(Rotina());
        }

        private IEnumerator Rotina()
        {
            Debug.Log("[Fumaça] Alvo: " + config.ApiRoot);

            PodiumResult<HealthResponse> saude = null;
            yield return api.CheckHealth(r => saude = r);
            if (!saude.Ok)
            {
                Debug.LogError("[Fumaça] /health falhou: " + saude.Message);
                yield break;
            }
            Debug.Log("[Fumaça] health: api=" + saude.Value.status + " banco=" + saude.Value.database);

            PodiumResult<PresentationInitResponse> init = null;
            yield return api.InitSession(presentationPath, persona, scenario, r => init = r);
            if (!init.Ok)
            {
                Debug.LogError("[Fumaça] /init falhou (" + init.Kind + "): " + init.Message);
                yield break;
            }

            string sessionId = init.Value.session_id;
            Debug.Log("[Fumaça] sessão " + sessionId + " · " + init.Value.file_type +
                      " · " + init.Value.slides_char_count + " chars de slides");

            PodiumResult<AudioUploadResponse> audio = null;
            yield return api.UploadAudioFile(sessionId, audioPath, false, r => audio = r);
            if (!audio.Ok)
            {
                Debug.LogError("[Fumaça] /audio falhou (" + audio.Kind + "): " + audio.Message);
                yield break;
            }
            Debug.Log("[Fumaça] áudio recebido: " + audio.Value.received_bytes + " bytes · " +
                      audio.Value.duration_seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");

            if (!runAnalysis)
            {
                Debug.Log("[Fumaça] Parando antes do /analyze (runAnalysis desligado). " +
                          "Init e upload validados — nenhuma cota de IA consumida.");
                yield break;
            }

            PodiumResult<AnalysisAcceptedResponse> aceite = null;
            yield return api.StartAnalysis(sessionId, r => aceite = r);
            if (!aceite.Ok && aceite.Kind != PodiumErrorKind.Conflict)
            {
                Debug.LogError("[Fumaça] /analyze falhou (" + aceite.Kind + "): " + aceite.Message);
                yield break;
            }
            Debug.Log("[Fumaça] análise aceita; consultando status...");

            PodiumResult<PresentationResponse> final = null;
            yield return api.WaitForCompletion(
                sessionId,
                (status, decorrido) => Debug.Log("[Fumaça] " + status + " · " +
                                                 decorrido.ToString("0", CultureInfo.InvariantCulture) + " s"),
                r => final = r);

            if (!final.Ok)
            {
                Debug.LogError("[Fumaça] polling falhou (" + final.Kind + "): " + final.Message);
                yield break;
            }

            if (final.Value.status == SessionStatus.Failed)
            {
                Debug.LogError("[Fumaça] sessão falhou no servidor: " + final.Value.error_message);
                yield break;
            }

            PodiumResult<FeedbackResponse> feedback = null;
            yield return api.GetFeedback(sessionId, r => feedback = r);
            if (!feedback.Ok)
            {
                Debug.LogError("[Fumaça] /feedback falhou (" + feedback.Kind + "): " + feedback.Message);
                yield break;
            }

            Relatar(feedback.Value);
        }

        private static void Relatar(FeedbackResponse f)
        {
            Debug.Log("[Fumaça] ---- FEEDBACK DUPLO ----");
            Debug.Log("[Fumaça] aterramento: " + f.perguntas_aprovadas + "/" + f.perguntas_geradas +
                      " (taxa " + f.taxa_aterramento.ToString("0.00", CultureInfo.InvariantCulture) + ")");

            if (f.questions != null)
            {
                for (int i = 0; i < f.questions.Length; i++)
                {
                    GeneratedQuestion q = f.questions[i];
                    Debug.Log("[Fumaça] P" + (i + 1) + " [slide " + q.slide_origem + "] " + q.question);
                    Debug.Log("[Fumaça]     âncora: \"" + q.trecho_literal + "\"");
                }
            }

            if (f.metrics != null)
            {
                Debug.Log("[Fumaça] forma: " + f.metrics.words_per_minute.ToString("0", CultureInfo.InvariantCulture) + " ppm · " +
                          f.metrics.pause_count + " pausas · maior pausa " +
                          f.metrics.longest_pause_seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            }

            if (f.coverageNaoMedida)
            {
                Debug.Log("[Fumaça] cobertura: não medida nesta sessão (feedback anterior à Fase 3).");
            }
            else if (f.slide_coverage != null)
            {
                Debug.Log("[Fumaça] cobertura: " +
                          (f.slide_coverage.percentual_coberto * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%" +
                          (f.slide_coverage.alerta_descolamento ? "  ALERTA DE DESCOLAMENTO" : ""));
            }

            if (f.slides_truncados || f.transcricao_truncada)
            {
                Debug.LogWarning("[Fumaça] contexto truncado — slides:" + f.slides_truncados +
                                 " transcrição:" + f.transcricao_truncada);
            }
        }
    }
}
