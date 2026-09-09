using System.Globalization;
using Podium.Api;
using Podium.Audio;
using Podium.Scenarios;
using Podium.Slides;
using UnityEngine;

namespace Podium.Session
{
    /// <summary>
    /// Lobby e painel de estado da Sala de Aula, em IMGUI.
    /// <para>
    /// IMGUI, e não Canvas/TextMeshPro, porque não exige pacote, fonte nem prefab: a cena
    /// abre e funciona em qualquer máquina que abra o projeto. Não é a interface final do
    /// VR — é a que permite ver a sessão inteira acontecer (escolha da persona, gravação,
    /// análise, Feedback Duplo) enquanto a interação XR ainda não existe.
    /// </para>
    /// </summary>
    public class SalaDeAulaController : MonoBehaviour
    {
        [Header("Dependências")]
        [SerializeField] private PodiumSession session;
        [SerializeField] private PodiumApiClient api;
        [SerializeField] private PresentationRecorder recorder;
        [SerializeField] private ApresentacaoDeSlides slides;

        [Header("Conteúdo")]
        [SerializeField] private ScenarioDefinition scenario;
        [SerializeField] private PersonaDefinition[] personas;

        [Tooltip("PDF ou PPTx enviado no /init. No Editor, caminho absoluto no PC.")]
        [SerializeField] private string presentationPath =
            "C:/Users/eduar/projetos/podium/storage/_fixtures/tcc_slides.pdf";

        private int personaSelecionada;
        private string[] nomesPersonas = new string[0];

        private string mensagemApi = "API ainda não verificada.";
        private bool apiOk;
        private bool verificandoApi;

        private string statusAnalise = "";
        private float decorridoAnalise;

        private FeedbackResponse feedback;
        private string erro;
        private bool voltarAoLobby;
        private Vector2 scroll;

        private GUIStyle titulo;
        private GUIStyle texto;
        private GUIStyle destaque;
        private bool estilosProntos;

        private void OnEnable()
        {
            if (session != null)
            {
                session.StateChanged += AoMudarEstado;
                session.AnalysisProgress += AoProgredir;
                session.FeedbackReady += AoReceberFeedback;
                session.SessionFailed += AoFalhar;
            }
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.StateChanged -= AoMudarEstado;
                session.AnalysisProgress -= AoProgredir;
                session.FeedbackReady -= AoReceberFeedback;
                session.SessionFailed -= AoFalhar;
            }
        }

        private void Start()
        {
            int n = personas == null ? 0 : personas.Length;
            nomesPersonas = new string[n];
            for (int i = 0; i < n; i++)
            {
                nomesPersonas[i] = personas[i] != null ? personas[i].displayName : "(vazio)";
            }

            AplicarSelecao();

            // Verificar já na abertura: o erro mais comum é o backend fora do ar, e ele
            // precisa aparecer antes de o usuário escolher persona e apertar "Criar".
            VerificarApi();
        }

        private void AplicarSelecao()
        {
            if (session == null)
            {
                return;
            }

            session.Scenario = scenario;

            if (personas != null && personas.Length > 0)
            {
                personaSelecionada = Mathf.Clamp(personaSelecionada, 0, personas.Length - 1);
                session.Persona = personas[personaSelecionada];
            }
        }

        /// <summary>
        /// A tela da sala usa a exportação em imagens do mesmo arquivo que sobe no /init:
        /// um caminho só, para que material analisado e material projetado não divirjam.
        /// </summary>
        private void CarregarSlides()
        {
            if (slides != null)
            {
                slides.Carregar(presentationPath);
            }
        }

        private void VerificarApi()
        {
            if (api == null || verificandoApi)
            {
                return;
            }

            verificandoApi = true;
            apiOk = false;
            mensagemApi = "Verificando " +
                          (api.Config != null ? api.Config.ApiRoot : "(config vazio)") + "...";

            StartCoroutine(api.CheckHealth(r =>
            {
                verificandoApi = false;
                apiOk = r.Ok;
                mensagemApi = r.Ok
                    ? "API no ar (banco: " + r.Value.database + ")."
                    : "API fora do ar: " + r.Message +
                      "\nNo PC: abra o Docker Desktop e rode 'docker compose up -d' na pasta podium.";
            }));
        }

        private void AoMudarEstado(PodiumSessionState novo)
        {
            if (novo != PodiumSessionState.Completed)
            {
                voltarAoLobby = false;
            }

            if (novo == PodiumSessionState.Analyzing)
            {
                statusAnalise = SessionStatus.Processing;
                decorridoAnalise = 0f;
            }
        }

        private void AoProgredir(string status, float decorrido)
        {
            statusAnalise = status;
            decorridoAnalise = decorrido;
        }

        private void AoReceberFeedback(FeedbackResponse f)
        {
            feedback = f;
            scroll = Vector2.zero;
        }

        private void AoFalhar(PodiumErrorKind kind, string mensagem)
        {
            erro = mensagem;
        }

        private void OnGUI()
        {
            PrepararEstilos();

            float largura = Mathf.Min(640f, Screen.width - 40f);
            GUILayout.BeginArea(new Rect(20f, 20f, largura, Screen.height - 40f), GUI.skin.box);

            GUILayout.Label(scenario != null ? scenario.displayName : "Podium", titulo);
            if (scenario != null)
            {
                GUILayout.Label(scenario.description, texto);
            }
            GUILayout.Space(8f);

            PodiumSessionState estado = session != null ? session.State : PodiumSessionState.Idle;

            switch (estado)
            {
                case PodiumSessionState.Idle:
                case PodiumSessionState.Failed:
                    DesenharLobby(estado);
                    break;

                case PodiumSessionState.Initializing:
                    GUILayout.Label("Criando a sessão e extraindo o texto dos slides...", texto);
                    break;

                case PodiumSessionState.Ready:
                    GUILayout.Label("Sessão criada. A banca está sentada; quando estiver pronto, " +
                                    "comece a falar.", texto);
                    if (GUILayout.Button("Começar apresentação", GUILayout.Height(40f)))
                    {
                        session.StartPresentation();
                    }
                    DesenharSlides();
                    break;

                case PodiumSessionState.Recording:
                    DesenharGravacao();
                    break;

                case PodiumSessionState.Uploading:
                    GUILayout.Label("Enviando o último trecho da fala...", texto);
                    break;

                case PodiumSessionState.Analyzing:
                    GUILayout.Label("Analisando: " + statusAnalise + " · " +
                                    Segundos(decorridoAnalise) + " s", destaque);
                    GUILayout.Label("Uma fala de 10 min leva cerca de 3 min (transcrição + geração " +
                                    "das perguntas). Não é travamento.", texto);
                    break;

                case PodiumSessionState.Completed:
                    if (voltarAoLobby)
                    {
                        DesenharLobby(estado);
                    }
                    else
                    {
                        DesenharFeedback();
                    }
                    break;
            }

            GUILayout.EndArea();
        }

        private void DesenharLobby(PodiumSessionState estado)
        {
            if (estado == PodiumSessionState.Failed && !string.IsNullOrEmpty(erro))
            {
                GUILayout.Label("Falha: " + erro, destaque);
                GUILayout.Space(8f);
            }

            GUILayout.Label("Banca", titulo);
            if (nomesPersonas.Length > 0)
            {
                int novo = GUILayout.SelectionGrid(personaSelecionada, nomesPersonas,
                                                   nomesPersonas.Length, GUILayout.Height(32f));
                if (novo != personaSelecionada)
                {
                    personaSelecionada = novo;
                    AplicarSelecao();
                }

                PersonaDefinition p = personas[personaSelecionada];
                if (p != null)
                {
                    GUILayout.Label(p.description + "\n(" + p.apiValue + ")", texto);
                }
            }
            else
            {
                GUILayout.Label("Nenhuma persona atribuída no Inspector.", destaque);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Apresentação (PDF ou PPTx)", titulo);
            presentationPath = GUILayout.TextField(presentationPath);

            if (slides != null)
            {
                // Conferir antes de criar a sessão: descobrir que a pasta de imagens não
                // existe já com a banca montada custa uma sessão inteira.
                if (GUILayout.Button("Conferir slides", GUILayout.Height(26f)))
                {
                    CarregarSlides();
                }

                GUILayout.Label(slides.Diagnostico, slides.TemSlides ? texto : destaque);
            }

            GUILayout.Space(8f);
            GUILayout.Label(mensagemApi, apiOk ? texto : destaque);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Verificar API", GUILayout.Height(32f)))
            {
                VerificarApi();
            }

            GUI.enabled = apiOk && !verificandoApi && session != null;
            if (GUILayout.Button("Criar sessão", GUILayout.Height(32f)))
            {
                erro = null;
                feedback = null;
                AplicarSelecao();
                CarregarSlides();
                session.BeginSession(presentationPath);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DesenharGravacao()
        {
            float t = recorder != null ? recorder.ElapsedSeconds : 0f;
            float max = recorder != null ? recorder.MaxSeconds : 0f;
            int chunks = recorder != null ? recorder.ChunksEmitted : 0;

            GUILayout.Label("Gravando: " + Tempo(t) + " / " + Tempo(max) +
                            " · trechos enviados: " + chunks, destaque);
            GUILayout.Label("Fale olhando para a banca. Ao encerrar, o último trecho sobe e a " +
                            "análise começa sozinha.", texto);

            if (GUILayout.Button("Encerrar apresentação", GUILayout.Height(40f)))
            {
                session.FinishPresentation();
            }

            DesenharSlides();
        }

        /// <summary>
        /// Comandos de slide. Ficam no painel além do teclado porque no headset não há
        /// teclado, e este é o lugar onde os botões da mão virão morar.
        /// </summary>
        private void DesenharSlides()
        {
            if (slides == null)
            {
                return;
            }

            GUILayout.Space(8f);

            if (!slides.TemSlides)
            {
                GUILayout.Label("Slides: " + slides.Diagnostico, texto);
                return;
            }

            GUILayout.BeginHorizontal();

            GUI.enabled = slides.IndiceAtual > 0;
            if (GUILayout.Button("Slide anterior", GUILayout.Height(32f)))
            {
                slides.Anterior();
            }

            GUI.enabled = slides.IndiceAtual < slides.Total - 1;
            if (GUILayout.Button("Próximo slide", GUILayout.Height(32f)))
            {
                slides.Proximo();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label("Slide " + (slides.IndiceAtual + 1) + " de " + slides.Total +
                            " · teclado: setas, PageUp/PageDown ou espaço.", texto);
        }

        private void DesenharFeedback()
        {
            FeedbackResponse f = feedback;
            if (f == null)
            {
                GUILayout.Label("Sessão concluída, mas nenhum feedback foi recebido.", destaque);
            }
            else
            {
                scroll = GUILayout.BeginScrollView(scroll);

                GUILayout.Label("Perguntas da banca — " + f.perguntas_aprovadas + " aprovadas de " +
                                f.perguntas_geradas + " geradas (aterramento " +
                                Pct(f.taxa_aterramento) + ")", titulo);

                if (f.questions == null || f.questions.Length == 0)
                {
                    GUILayout.Label("Nenhuma pergunta passou na verificação contra os slides.", texto);
                }
                else
                {
                    for (int i = 0; i < f.questions.Length; i++)
                    {
                        GeneratedQuestion q = f.questions[i];
                        GUILayout.Label((i + 1) + ". " + q.question, destaque);
                        // A âncora é exibida junto da pergunta, não escondida: é o que
                        // distingue banca ancorada de alucinação.
                        GUILayout.Label("Slide " + q.slide_origem + " — \"" + q.trecho_literal + "\"", texto);
                        if (!string.IsNullOrEmpty(q.rationale))
                        {
                            GUILayout.Label("Por quê: " + q.rationale, texto);
                        }
                        GUILayout.Space(6f);
                    }
                }

                if (!string.IsNullOrEmpty(f.content_analysis))
                {
                    GUILayout.Label("Análise de conteúdo", titulo);
                    GUILayout.Label(f.content_analysis, texto);
                }

                if (f.metrics != null)
                {
                    GUILayout.Label("Forma", titulo);
                    GUILayout.Label(Segundos(f.metrics.words_per_minute) + " palavras/min · " +
                                    f.metrics.pause_count + " pausas · maior pausa " +
                                    Decimal1(f.metrics.longest_pause_seconds) + " s · fala de " +
                                    Tempo(f.metrics.duration_seconds), texto);
                }

                GUILayout.Label("Cobertura dos slides", titulo);
                if (f.coverageNaoMedida)
                {
                    GUILayout.Label("Não medida nesta sessão.", texto);
                }
                else if (f.slide_coverage != null)
                {
                    GUILayout.Label(Pct(f.slide_coverage.percentual_coberto) + " do material foi " +
                                    "apresentado" +
                                    (f.slide_coverage.alerta_descolamento
                                        ? " — ALERTA: a fala se descolou dos slides."
                                        : "."),
                                    f.slide_coverage.alerta_descolamento ? destaque : texto);
                }

                if (f.slides_truncados || f.transcricao_truncada)
                {
                    GUILayout.Label("Aviso: contexto truncado (slides: " + f.slides_truncados +
                                    ", transcrição: " + f.transcricao_truncada + ").", destaque);
                }

                GUILayout.EndScrollView();
            }

            if (GUILayout.Button("Nova sessão", GUILayout.Height(36f)))
            {
                voltarAoLobby = true;
            }
        }

        private void PrepararEstilos()
        {
            if (estilosProntos)
            {
                return;
            }

            titulo = new GUIStyle(GUI.skin.label);
            titulo.fontSize = 18;
            titulo.fontStyle = FontStyle.Bold;
            titulo.wordWrap = true;

            texto = new GUIStyle(GUI.skin.label);
            texto.fontSize = 14;
            texto.wordWrap = true;

            destaque = new GUIStyle(texto);
            destaque.fontStyle = FontStyle.Bold;

            GUI.skin.button.fontSize = 14;
            GUI.skin.textField.fontSize = 14;

            estilosProntos = true;
        }

        private static string Tempo(float segundos)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(segundos));
            return (total / 60) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Segundos(float valor)
        {
            return valor.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Decimal1(float valor)
        {
            return valor.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Pct(float fracao)
        {
            return (fracao * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
