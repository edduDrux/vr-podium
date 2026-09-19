using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Podium.Api;
using Podium.Audio;
using Podium.Scenarios;
using UnityEngine;

namespace Podium.Session
{
    public enum PodiumSessionState
    {
        Idle,
        Initializing,
        Ready,
        Recording,
        Uploading,
        Analyzing,
        Completed,
        Failed
    }

    /// <summary>
    /// Orquestra uma sessão inteira: ingestão do material, gravação em pedaços, análise e
    /// feedback. É o único lugar que decide QUANDO chamar a API; o
    /// <see cref="PodiumApiClient"/> só sabe COMO.
    /// <para>
    /// Os estados espelham o ciclo de vida do backend (PresentationStatus) porque a cena
    /// de espera precisa dizer a verdade sobre onde a sessão está — no VR, uma tela que
    /// não muda é lida como travamento.
    /// </para>
    /// </summary>
    public class PodiumSession : MonoBehaviour
    {
        [Header("Dependências")]
        [SerializeField] private PodiumApiClient api;
        [SerializeField] private PresentationRecorder recorder;

        [Tooltip("Opcional. Com ele, a fala é transcrita no próprio aparelho enquanto " +
                 "acontece e o /analyze pula o STT do servidor. Sem ele, o servidor " +
                 "transcreve como antes.")]
        [SerializeField] private TranscritorLocal transcritor;

        [Header("Sessão")]
        [SerializeField] private ScenarioDefinition scenario;
        [SerializeField] private PersonaDefinition persona;

        [Header("Robustez")]
        [Tooltip("Tentativas por chunk antes de desistir. Um chunk perdido não gera erro " +
                 "no backend: gera métrica de forma calculada sobre uma fala incompleta.")]
        [SerializeField] private int chunkUploadAttempts = 3;

        [SerializeField] private float chunkRetryDelaySeconds = 2f;

        /// <summary>
        /// Persona e cenário podem ser trocados pelo lobby antes de <see cref="BeginSession"/>.
        /// Trocar depois não tem efeito: o backend fixa os dois no /init.
        /// </summary>
        public PersonaDefinition Persona
        {
            get { return persona; }
            set { persona = value; }
        }

        public ScenarioDefinition Scenario
        {
            get { return scenario; }
            set { scenario = value; }
        }

        public event Action<PodiumSessionState> StateChanged;
        public event Action<string, float> AnalysisProgress;
        public event Action<FeedbackResponse> FeedbackReady;
        public event Action<PodiumErrorKind, string> SessionFailed;

        private readonly Queue<ChunkPendente> fila = new Queue<ChunkPendente>();
        private bool uploaderAtivo;
        private bool gravacaoEncerrada;
        private int chunksEnviados;

        public string SessionId { get; private set; }
        public PodiumSessionState State { get; private set; }

        /// <summary>
        /// A transcrição desta sessão foi feita aqui e aceita pela API — ou seja, o STT do
        /// servidor não vai rodar. A tela diz isso porque muda a expectativa de espera.
        /// </summary>
        public bool TranscricaoEnviada { get; private set; }

        /// <summary>Quem transcreve esta sessão, em uma frase, para o painel.</summary>
        public string DiagnosticoDaTranscricao
        {
            get
            {
                if (transcritor == null)
                {
                    return "Sem transcritor local; o servidor transcreve a fala.";
                }

                return transcritor.Diagnostico;
            }
        }

        private struct ChunkPendente
        {
            public byte[] Wav;
            public int Indice;
        }

        private void Awake()
        {
            State = PodiumSessionState.Idle;
        }

        private void OnEnable()
        {
            if (recorder != null)
            {
                recorder.ChunkReady += AoFecharChunk;
                recorder.BlocoCapturado += AoCapturarBloco;
                recorder.RecordingStopped += AoPararGravacao;
                recorder.LimitReached += AoAtingirLimite;
            }
        }

        private void OnDisable()
        {
            if (recorder != null)
            {
                recorder.ChunkReady -= AoFecharChunk;
                recorder.BlocoCapturado -= AoCapturarBloco;
                recorder.RecordingStopped -= AoPararGravacao;
                recorder.LimitReached -= AoAtingirLimite;
            }
        }

        /// <summary>
        /// O mesmo áudio que vira WAV alimenta a transcrição local. Um leitor de microfone
        /// só, dois consumidores: dois <c>Microphone.Start</c> no mesmo dispositivo não
        /// coexistem.
        /// </summary>
        private void AoCapturarBloco(float[] amostras, int canais, int frequencia)
        {
            if (transcritor != null)
            {
                transcritor.Alimentar(amostras, canais, frequencia);
            }
        }

        /// <summary>Cria a sessão no backend e ingere o PDF/PPTx. Chame antes de gravar.</summary>
        public void BeginSession(string presentationFilePath)
        {
            // Completed também libera: depois do feedback, uma nova sessão é o caminho
            // natural (trocar a persona e apresentar de novo), não uma exceção.
            if (State != PodiumSessionState.Idle &&
                State != PodiumSessionState.Failed &&
                State != PodiumSessionState.Completed)
            {
                Debug.LogWarning("[Podium] BeginSession ignorado: sessão já em andamento.");
                return;
            }

            // Falhar aqui, com nome, e não com NullReference três chamadas adiante: campo
            // esquecido no Inspector é o erro mais comum de montagem de cena.
            string faltando = DependenciaFaltando();
            if (faltando != null)
            {
                Falhar(PodiumErrorKind.Unknown,
                       "PodiumSession sem '" + faltando + "' atribuído no Inspector.");
                return;
            }

            fila.Clear();
            chunksEnviados = 0;
            gravacaoEncerrada = false;
            SessionId = null;

            StartCoroutine(RotinaInit(presentationFilePath));
        }

        /// <summary>Começa a gravar. Só faz sentido depois de <see cref="BeginSession"/>.</summary>
        public void StartPresentation()
        {
            if (State != PodiumSessionState.Ready)
            {
                Debug.LogWarning("[Podium] StartPresentation ignorado: estado " + State);
                return;
            }

            StartCoroutine(RotinaComecar());
        }

        /// <summary>
        /// Abre a transcrição local ANTES do microfone. Na outra ordem, os primeiros
        /// segundos de fala seriam gravados no WAV mas não chegariam ao transcritor — e a
        /// frase de abertura é justamente onde mora o tema da apresentação.
        /// </summary>
        private IEnumerator RotinaComecar()
        {
            if (transcritor != null && transcritor.Habilitado)
            {
                Task preparo = transcritor.Iniciar();
                while (!preparo.IsCompleted)
                {
                    yield return null;
                }

                if (preparo.IsFaulted)
                {
                    // Transcrever aqui é otimização, não requisito: o servidor continua
                    // sabendo transcrever, então a falha vira aviso e a fala começa.
                    Debug.LogWarning("[Podium] A transcrição local não abriu: " +
                                     preparo.Exception.GetBaseException().Message);
                }
            }

            if (recorder.StartRecording())
            {
                MudarEstado(PodiumSessionState.Recording);
            }
            else
            {
                Falhar(PodiumErrorKind.Unknown, "Não foi possível iniciar o microfone.");
            }
        }

        /// <summary>
        /// Encerra a fala. A análise NÃO dispara aqui: dispara quando o último chunk
        /// termina de subir — pedir /analyze com pedaço ainda na fila analisaria uma
        /// apresentação truncada. O gravador avisa por RecordingStopped, e é esse aviso
        /// que muda o estado e arma a análise.
        /// </summary>
        public void FinishPresentation()
        {
            if (State != PodiumSessionState.Recording)
            {
                return;
            }

            recorder.StopRecording();
        }

        private string DependenciaFaltando()
        {
            if (api == null) return "api";
            if (recorder == null) return "recorder";
            if (scenario == null) return "scenario";
            if (persona == null) return "persona";
            return null;
        }

        private IEnumerator RotinaInit(string presentationFilePath)
        {
            MudarEstado(PodiumSessionState.Initializing);

            PodiumResult<PresentationInitResponse> r = null;
            yield return api.InitSession(
                presentationFilePath, persona.apiValue, scenario.apiValue, x => r = x);

            if (!r.Ok)
            {
                Falhar(r.Kind, r.Message);
                yield break;
            }

            SessionId = r.Value.session_id;
            Debug.Log("[Podium] Sessão " + SessionId + " criada. " +
                      r.Value.slides_char_count + " caracteres extraídos dos slides.");

            MudarEstado(PodiumSessionState.Ready);
        }

        private void AoFecharChunk(byte[] wav, int indice)
        {
            if (State == PodiumSessionState.Failed)
            {
                return;
            }

            fila.Enqueue(new ChunkPendente { Wav = wav, Indice = indice });

            if (!uploaderAtivo)
            {
                StartCoroutine(RotinaUpload());
            }
        }

        /// <summary>
        /// O gravador avisa DEPOIS do último ChunkReady. Mudar o estado aqui, e não em
        /// FinishPresentation, cobre também a parada automática pelo teto de duração.
        /// </summary>
        private void AoPararGravacao()
        {
            gravacaoEncerrada = true;

            if (State == PodiumSessionState.Recording)
            {
                MudarEstado(PodiumSessionState.Uploading);
            }

            if (!uploaderAtivo)
            {
                StartCoroutine(RotinaUpload());
            }
        }

        private void AoAtingirLimite()
        {
            Debug.LogWarning("[Podium] Teto de duração atingido; gravação encerrada " +
                             "automaticamente para não estourar o limite do /analyze.");
        }

        /// <summary>
        /// Drena a fila de chunks UM POR VEZ.
        /// A serialização não é preferência de estilo: o backend deriva o índice do
        /// próximo chunk da contagem de arquivos já gravados, então dois uploads em voo
        /// ao mesmo tempo recebem o mesmo índice e um sobrescreve o outro em silêncio.
        /// </summary>
        private IEnumerator RotinaUpload()
        {
            uploaderAtivo = true;

            while (fila.Count > 0)
            {
                ChunkPendente pendente = fila.Dequeue();
                string nome = string.Format("chunk_{0:000}.wav", pendente.Indice);

                bool enviado = false;
                PodiumResult<AudioUploadResponse> ultimo = null;

                for (int tentativa = 1; tentativa <= chunkUploadAttempts && !enviado; tentativa++)
                {
                    PodiumResult<AudioUploadResponse> r = null;
                    yield return api.UploadAudio(SessionId, pendente.Wav, nome, true, x => r = x);
                    ultimo = r;

                    if (r.Ok)
                    {
                        enviado = true;
                        chunksEnviados++;
                        break;
                    }

                    // Só faz sentido insistir em falha de transporte. Um 409 (formatos
                    // misturados) ou 413 não melhora com repetição.
                    if (r.Kind != PodiumErrorKind.Network && r.Kind != PodiumErrorKind.Timeout)
                    {
                        break;
                    }

                    Debug.LogWarning("[Podium] Chunk " + pendente.Indice + " falhou (tentativa " +
                                     tentativa + "): " + r.Message);
                    yield return new WaitForSecondsRealtime(chunkRetryDelaySeconds);
                }

                if (!enviado)
                {
                    uploaderAtivo = false;
                    Falhar(ultimo.Kind, "Falha ao enviar o trecho " + pendente.Indice +
                                        " da fala: " + ultimo.Message);
                    yield break;
                }
            }

            uploaderAtivo = false;

            if (gravacaoEncerrada && State == PodiumSessionState.Uploading)
            {
                yield return RotinaAnalise();
            }
        }

        private IEnumerator RotinaAnalise()
        {
            if (chunksEnviados == 0)
            {
                Falhar(PodiumErrorKind.Unprocessable,
                       "Nenhum trecho de áudio foi capturado; não há o que analisar.");
                yield break;
            }

            MudarEstado(PodiumSessionState.Analyzing);

            yield return RotinaTranscricao();

            PodiumResult<AnalysisAcceptedResponse> aceite = null;
            yield return api.StartAnalysis(SessionId, x => aceite = x);

            if (!aceite.Ok)
            {
                if (aceite.Kind != PodiumErrorKind.Conflict)
                {
                    Falhar(aceite.Kind, aceite.Message);
                    yield break;
                }

                // 409 tem três causas no backend: análise já em curso, sessão sem áudio e
                // chunks de formatos misturados. Só a primeira autoriza seguir para o
                // polling; o status da sessão desambigua sem depender do texto do erro.
                PodiumResult<PresentationResponse> estado = null;
                yield return api.GetPresentation(SessionId, x => estado = x);

                if (!estado.Ok || estado.Value.status != SessionStatus.Processing)
                {
                    Falhar(PodiumErrorKind.Conflict, aceite.Message);
                    yield break;
                }
            }

            PodiumResult<PresentationResponse> final = null;
            yield return api.WaitForCompletion(
                SessionId,
                (status, decorrido) =>
                {
                    if (AnalysisProgress != null)
                    {
                        AnalysisProgress(status, decorrido);
                    }
                },
                x => final = x);

            if (!final.Ok)
            {
                Falhar(final.Kind, final.Message);
                yield break;
            }

            if (final.Value.status == SessionStatus.Failed)
            {
                Falhar(PodiumErrorKind.Server,
                       final.Value.error_message ?? "A análise falhou no servidor.");
                yield break;
            }

            PodiumResult<FeedbackResponse> feedback = null;
            yield return api.GetFeedback(SessionId, x => feedback = x);

            if (!feedback.Ok)
            {
                Falhar(feedback.Kind, feedback.Message);
                yield break;
            }

            MudarEstado(PodiumSessionState.Completed);

            if (FeedbackReady != null)
            {
                FeedbackReady(feedback.Value);
            }
        }

        /// <summary>
        /// Fecha a transcrição local e a entrega ao backend, para que o /analyze pule o STT.
        /// <para>
        /// Roda depois do último chunk e antes do /analyze, nesta ordem por obrigação do
        /// contrato: o /audio apaga a transcrição da sessão ao receber áudio novo, e o
        /// /analyze só pula o STT se encontrar o texto já gravado.
        /// </para>
        /// <para>
        /// Nada aqui pode derrubar a sessão. Transcrição é atalho de custo e de tempo; se
        /// ela não fechar, ou se o PUT falhar, o servidor transcreve como sempre fez e a
        /// apresentação segue — só mais devagar.
        /// </para>
        /// </summary>
        private IEnumerator RotinaTranscricao()
        {
            if (transcritor == null || !transcritor.Habilitado)
            {
                yield break;
            }

            if (AnalysisProgress != null)
            {
                AnalysisProgress(SessionStatus.Processing, 0f);
            }

            Task<string> fechamento = transcritor.Encerrar();
            while (!fechamento.IsCompleted)
            {
                yield return null;
            }

            if (fechamento.IsFaulted)
            {
                Debug.LogWarning("[Podium] A transcrição local falhou ao fechar: " +
                                 fechamento.Exception.GetBaseException().Message);
                yield break;
            }

            string texto = fechamento.Result;
            if (string.IsNullOrEmpty(texto))
            {
                yield break;
            }

            PodiumResult<TranscriptAcceptedResponse> r = null;
            yield return api.SubmitTranscript(SessionId, texto, transcritor.Motor, x => r = x);

            if (r.Ok)
            {
                TranscricaoEnviada = true;
                Debug.Log("[Podium] Transcrição local enviada: " + r.Value.word_count +
                          " palavras. O servidor não vai chamar o STT.");
            }
            else
            {
                Debug.LogWarning("[Podium] A API recusou a transcrição local (" + r.Message +
                                 "); o servidor vai transcrever.");
            }
        }

        private void MudarEstado(PodiumSessionState novo)
        {
            State = novo;
            if (StateChanged != null)
            {
                StateChanged(novo);
            }
        }

        private void Falhar(PodiumErrorKind kind, string mensagem)
        {
            Debug.LogError("[Podium] " + kind + ": " + mensagem);
            MudarEstado(PodiumSessionState.Failed);

            if (SessionFailed != null)
            {
                SessionFailed(kind, mensagem);
            }
        }
    }
}
