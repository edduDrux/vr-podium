namespace Podium.Api
{
    /// <summary>
    /// Falhas que o Cliente VR precisa distinguir para dizer algo útil ao usuário.
    /// Cada valor corresponde a um caminho de erro que o backend realmente produz —
    /// não é uma lista genérica de HTTP.
    /// </summary>
    public enum PodiumErrorKind
    {
        None = 0,

        /// <summary>Não chegou na API: IP errado, firewall, Wi-Fi, ou cleartext HTTP bloqueado no Android.</summary>
        Network,

        /// <summary>A requisição estourou o tempo do cliente. Não significa que a API desistiu.</summary>
        Timeout,

        /// <summary>404 — sessão inexistente, ou feedback ainda não gerado.</summary>
        NotFound,

        /// <summary>415 — o arquivo não é PDF nem PPTx.</summary>
        UnsupportedMedia,

        /// <summary>413 — PDF acima de MAX_UPLOAD_SIZE_MB, ou áudio acima de 15 min.</summary>
        TooLarge,

        /// <summary>422 — arquivo ilegível, PDF só com imagens, ou áudio indecodificável.</summary>
        Unprocessable,

        /// <summary>409 — análise já em curso, chunks de formatos diferentes, ou sessão sem áudio.</summary>
        Conflict,

        /// <summary>5xx — a API respondeu, mas quebrou.</summary>
        Server,

        Unknown
    }

    /// <summary>
    /// Resultado de uma chamada. Existe para que o chamador nunca precise inspecionar
    /// UnityWebRequest: erro de rede e erro de negócio chegam pelo mesmo canal, e a
    /// corrotina não pode lançar exceção para quem a iniciou.
    /// </summary>
    public class PodiumResult<T>
    {
        public bool Ok;
        public T Value;
        public long HttpStatus;
        public PodiumErrorKind Kind = PodiumErrorKind.None;

        /// <summary>
        /// Mensagem pronta para a tela. Quando a API mandou `detail`, é o `detail` —
        /// as mensagens do backend são escritas em português e dizem o que fazer
        /// (o 422 do PDF sem texto, por exemplo, explica como reexportar o arquivo).
        /// </summary>
        public string Message;

        public string RawBody;

        public static PodiumResult<T> Success(T value, long status, string rawBody)
        {
            return new PodiumResult<T>
            {
                Ok = true, Value = value, HttpStatus = status, RawBody = rawBody
            };
        }

        public static PodiumResult<T> Failure(PodiumErrorKind kind, string message, long status = 0, string rawBody = null)
        {
            return new PodiumResult<T>
            {
                Ok = false, Kind = kind, Message = message, HttpStatus = status, RawBody = rawBody
            };
        }
    }
}
