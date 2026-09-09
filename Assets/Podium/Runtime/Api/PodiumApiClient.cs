using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Podium.Api
{
    /// <summary>
    /// Único ponto do projeto que conhece as rotas do backend do Podium.
    /// Tudo é corrotina, e não async/await: o ciclo de vida da corrotina morre junto com
    /// o GameObject, o que evita callback chegando numa cena que já foi descarregada.
    /// </summary>
    public class PodiumApiClient : MonoBehaviour
    {
        [SerializeField] private PodiumApiConfig config;

        public PodiumApiConfig Config
        {
            get { return config; }
            set { config = value; }
        }

        private const string PdfContentType = "application/pdf";
        private const string PptxContentType =
            "application/vnd.openxmlformats-officedocument.presentationml.presentation";


        /// <summary>GET /health — checagem de conectividade antes de abrir sessão.</summary>
        public IEnumerator CheckHealth(Action<PodiumResult<HealthResponse>> onDone)
        {
            UnityWebRequest req = UnityWebRequest.Get(config.ApiRoot + "/health");
            req.timeout = config.requestTimeout;
            yield return Send(req, onDone);
        }

        /// <summary>
        /// POST /presentations/init — cria a sessão e ingere o material.
        /// O nome do arquivo importa: o backend identifica o formato pela extensão e só
        /// usa o content-type como reforço.
        /// </summary>
        public IEnumerator InitSession(
            string presentationFilePath,
            string persona,
            string scenario,
            Action<PodiumResult<PresentationInitResponse>> onDone,
            string userId = null)
        {
            if (!File.Exists(presentationFilePath))
            {
                onDone(PodiumResult<PresentationInitResponse>.Failure(
                    PodiumErrorKind.Unprocessable,
                    "Arquivo de apresentação não encontrado: " + presentationFilePath));
                yield break;
            }

            byte[] bytes = File.ReadAllBytes(presentationFilePath);
            string fileName = Path.GetFileName(presentationFilePath);
            string contentType = Path.GetExtension(presentationFilePath).ToLowerInvariant() == ".pptx"
                ? PptxContentType
                : PdfContentType;

            List<IMultipartFormSection> form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", bytes, fileName, contentType),
                new MultipartFormDataSection("persona", persona),
                new MultipartFormDataSection("scenario", scenario)
            };

            if (!string.IsNullOrEmpty(userId))
            {
                form.Add(new MultipartFormDataSection("user_id", userId));
            }

            UnityWebRequest req = UnityWebRequest.Post(config.ApiRoot + "/presentations/init", form);
            req.timeout = config.uploadTimeout;
            yield return Send(req, onDone);
        }

        /// <summary>
        /// POST /presentations/{id}/audio.
        /// <para>
        /// ATENÇÃO: com <paramref name="isChunk"/> = true, envie um pedaço por vez e
        /// espere a resposta. O índice do próximo chunk é derivado da CONTAGEM de
        /// arquivos em disco (storage_service.next_chunk_path), então dois uploads
        /// simultâneos calculam o mesmo índice e um sobrescreve o outro — sem erro
        /// nenhum, com trecho de fala faltando na análise. A correção está na Fase 1.1
        /// do PRD do backend; até lá, serializar é responsabilidade do cliente.
        /// </para>
        /// Todos os chunks da sessão precisam ter a mesma extensão, ou a API responde 409.
        /// </summary>
        public IEnumerator UploadAudio(
            string sessionId,
            byte[] audioBytes,
            string fileName,
            bool isChunk,
            Action<PodiumResult<AudioUploadResponse>> onDone)
        {
            List<IMultipartFormSection> form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", audioBytes, fileName, "audio/wav"),
                new MultipartFormDataSection("is_chunk", isChunk ? "true" : "false")
            };

            UnityWebRequest req = UnityWebRequest.Post(
                config.ApiRoot + "/presentations/" + sessionId + "/audio", form);
            req.timeout = config.uploadTimeout;
            yield return Send(req, onDone);
        }

        /// <summary>Mesma rota, lendo do disco — usado no teste de fumaça, sem microfone.</summary>
        public IEnumerator UploadAudioFile(
            string sessionId,
            string audioFilePath,
            bool isChunk,
            Action<PodiumResult<AudioUploadResponse>> onDone)
        {
            if (!File.Exists(audioFilePath))
            {
                onDone(PodiumResult<AudioUploadResponse>.Failure(
                    PodiumErrorKind.Unprocessable,
                    "Arquivo de áudio não encontrado: " + audioFilePath));
                yield break;
            }

            byte[] bytes = File.ReadAllBytes(audioFilePath);
            string fileName = Path.GetFileName(audioFilePath);

            List<IMultipartFormSection> form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", bytes, fileName, MimeForAudio(fileName)),
                new MultipartFormDataSection("is_chunk", isChunk ? "true" : "false")
            };

            UnityWebRequest req = UnityWebRequest.Post(
                config.ApiRoot + "/presentations/" + sessionId + "/audio", form);
            req.timeout = config.uploadTimeout;
            yield return Send(req, onDone);
        }

        /// <summary>
        /// POST /presentations/{id}/analyze — responde 202 e processa em segundo plano.
        /// Um 409 aqui quase sempre significa "esta sessão já está sendo analisada": não
        /// é falha, é motivo para começar a consultar o status.
        /// </summary>
        public IEnumerator StartAnalysis(
            string sessionId,
            Action<PodiumResult<AnalysisAcceptedResponse>> onDone)
        {
            UnityWebRequest req = UnityWebRequest.Post(
                config.ApiRoot + "/presentations/" + sessionId + "/analyze",
                new List<IMultipartFormSection>());
            req.timeout = config.requestTimeout;
            yield return Send(req, onDone);
        }

        /// <summary>GET /presentations/{id} — estado da sessão.</summary>
        public IEnumerator GetPresentation(
            string sessionId,
            Action<PodiumResult<PresentationResponse>> onDone)
        {
            UnityWebRequest req = UnityWebRequest.Get(config.ApiRoot + "/presentations/" + sessionId);
            req.timeout = config.requestTimeout;
            yield return Send(req, onDone);
        }

        /// <summary>GET /presentations/{id}/feedback — o Feedback Duplo.</summary>
        public IEnumerator GetFeedback(
            string sessionId,
            Action<PodiumResult<FeedbackResponse>> onDone)
        {
            UnityWebRequest req = UnityWebRequest.Get(
                config.ApiRoot + "/presentations/" + sessionId + "/feedback");
            req.timeout = config.requestTimeout;

            yield return Send<FeedbackResponse>(req, resultado =>
            {
                if (resultado.Ok && resultado.Value != null)
                {
                    resultado.Value.coverageNaoMedida =
                        CoberturaNaoMedida(resultado.RawBody);
                }
                onDone(resultado);
            });
        }

        /// <summary>
        /// Consulta o status até `completed` ou `failed`.
        /// <paramref name="onProgress"/> recebe (status, segundos decorridos) a cada
        /// consulta — a cena de espera precisa disso: o pipeline leva minutos, e uma tela
        /// parada no VR é indistinguível de um travamento.
        /// </summary>
        public IEnumerator WaitForCompletion(
            string sessionId,
            Action<string, float> onProgress,
            Action<PodiumResult<PresentationResponse>> onDone)
        {
            float inicio = Time.realtimeSinceStartup;

            while (true)
            {
                PodiumResult<PresentationResponse> atual = null;
                yield return GetPresentation(sessionId, r => atual = r);

                float decorrido = Time.realtimeSinceStartup - inicio;

                if (!atual.Ok)
                {
                    // Falha de rede isolada não encerra a espera: o Wi-Fi do headset
                    // oscila e a análise segue rodando no servidor. Só erro definitivo
                    // (sessão inexistente) interrompe na hora.
                    if (atual.Kind == PodiumErrorKind.NotFound || decorrido >= config.pollTimeoutSeconds)
                    {
                        onDone(atual);
                        yield break;
                    }

                    yield return new WaitForSecondsRealtime(config.pollIntervalSeconds);
                    continue;
                }

                string status = atual.Value.status;
                if (onProgress != null)
                {
                    onProgress(status, decorrido);
                }

                if (status == SessionStatus.Completed || status == SessionStatus.Failed)
                {
                    onDone(atual);
                    yield break;
                }

                if (decorrido >= config.pollTimeoutSeconds)
                {
                    onDone(PodiumResult<PresentationResponse>.Failure(
                        PodiumErrorKind.Timeout,
                        string.Format(
                            "A análise passou de {0:0} s sem concluir (último status: {1}). " +
                            "A sessão pode continuar processando no servidor.",
                            config.pollTimeoutSeconds, status)));
                    yield break;
                }

                yield return new WaitForSecondsRealtime(config.pollIntervalSeconds);
            }
        }

        private IEnumerator Send<T>(UnityWebRequest req, Action<PodiumResult<T>> onDone)
        {
            using (req)
            {
                yield return req.SendWebRequest();

                string corpo = req.downloadHandler != null ? req.downloadHandler.text : null;
                long codigo = req.responseCode;

                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    onDone(PodiumResult<T>.Failure(
                        KindForConnectionError(req.error),
                        "Não foi possível falar com a API (" + req.error + "). Confira o " +
                        "endereço no PodiumApiConfig, se o container está no ar e se o " +
                        "headset está na mesma rede do PC."));
                    yield break;
                }

                if (req.result == UnityWebRequest.Result.DataProcessingError)
                {
                    onDone(PodiumResult<T>.Failure(
                        PodiumErrorKind.Unknown, "Resposta ilegível da API: " + req.error, codigo, corpo));
                    yield break;
                }

                if (codigo >= 200 && codigo < 300)
                {
                    T valor;
                    try
                    {
                        valor = JsonUtility.FromJson<T>(corpo);
                    }
                    catch (Exception e)
                    {
                        onDone(PodiumResult<T>.Failure(
                            PodiumErrorKind.Unknown,
                            "A API respondeu " + codigo + ", mas o corpo não pôde ser lido: " + e.Message,
                            codigo, corpo));
                        yield break;
                    }

                    onDone(PodiumResult<T>.Success(valor, codigo, corpo));
                    yield break;
                }

                onDone(PodiumResult<T>.Failure(KindFor(codigo), DetailOrDefault(corpo, codigo), codigo, corpo));
            }
        }

        /// <summary>
        /// Detecta `slide_coverage: null` no corpo bruto — feedback anterior à cobertura
        /// de slides. A checagem é feita aqui, no texto, porque o JsonUtility apaga a
        /// distinção: ele materializa um objeto vazio no lugar do null, e a tela passaria
        /// a mostrar 0% de cobertura para uma sessão que nunca foi medida. Ausência de
        /// medição não é medição zerada.
        /// Os espaços são removidos antes da busca porque o serializador do backend gera
        /// JSON compacto, mas isso é detalhe dele, não contrato.
        /// </summary>
        public static bool CoberturaNaoMedida(string corpo)
        {
            if (string.IsNullOrEmpty(corpo))
            {
                return false;
            }

            return corpo.Replace(" ", string.Empty).Contains("\"slide_coverage\":null");
        }

        /// <summary>
        /// O Unity reporta timeout do cliente como ConnectionError; a diferença só existe
        /// no texto do erro. Separar importa porque timeout não significa que a API
        /// desistiu — o /analyze, por exemplo, continua rodando no servidor.
        /// </summary>
        private static PodiumErrorKind KindForConnectionError(string erro)
        {
            if (!string.IsNullOrEmpty(erro) &&
                erro.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return PodiumErrorKind.Timeout;
            }

            return PodiumErrorKind.Network;
        }

        private static PodiumErrorKind KindFor(long codigo)
        {
            switch (codigo)
            {
                case 404: return PodiumErrorKind.NotFound;
                case 409: return PodiumErrorKind.Conflict;
                case 413: return PodiumErrorKind.TooLarge;
                case 415: return PodiumErrorKind.UnsupportedMedia;
                case 422: return PodiumErrorKind.Unprocessable;
                default:
                    if (codigo >= 500)
                    {
                        return PodiumErrorKind.Server;
                    }
                    return PodiumErrorKind.Unknown;
            }
        }

        /// <summary>
        /// Extrai o `detail` do corpo de erro. Tolera o caso em que o FastAPI devolve
        /// `detail` como LISTA (erro de validação do Pydantic — persona inválida, por
        /// exemplo): ali o JsonUtility não encontra a string, e cair no corpo bruto é
        /// melhor do que engolir o motivo da recusa.
        /// </summary>
        private static string DetailOrDefault(string corpo, long codigo)
        {
            if (string.IsNullOrEmpty(corpo))
            {
                return "A API respondeu " + codigo + " sem detalhe.";
            }

            try
            {
                ApiErrorBody erro = JsonUtility.FromJson<ApiErrorBody>(corpo);
                if (erro != null && !string.IsNullOrEmpty(erro.detail))
                {
                    return erro.detail;
                }
            }
            catch (Exception)
            {
                // Formato inesperado: seguir para o corpo bruto.
            }

            return "HTTP " + codigo + ": " + corpo;
        }

        private static string MimeForAudio(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            switch (ext)
            {
                case ".wav": return "audio/wav";
                case ".mp3": return "audio/mpeg";
                case ".ogg": return "audio/ogg";
                case ".m4a": return "audio/mp4";
                case ".aac": return "audio/aac";
                case ".flac": return "audio/flac";
                case ".webm": return "audio/webm";
                default: return "application/octet-stream";
            }
        }
    }
}
