using System;

namespace Podium.Api
{
    // Os campos usam snake_case, e não a convenção do C#, porque o JsonUtility casa nome
    // de campo com chave do JSON literalmente. Renomear para PascalCase exigiria um
    // serializador externo (Newtonsoft) só para adoçar a leitura — dependência a mais
    // num projeto de um desenvolvedor só. O contrato da API é a fonte da verdade; o
    // cliente se dobra a ele.

    /// <summary>GET /api/v1/health</summary>
    [Serializable]
    public class HealthResponse
    {
        public string status;
        public string environment;
        public string database;
    }

    /// <summary>POST /api/v1/presentations/init (201)</summary>
    [Serializable]
    public class PresentationInitResponse
    {
        public string session_id;
        public string status;
        public string file_type;

        /// <summary>Zero aqui significa slide sem texto — a API já recusa esse caso com 422.</summary>
        public int slides_char_count;
    }

    /// <summary>POST /api/v1/presentations/{id}/audio</summary>
    [Serializable]
    public class AudioUploadResponse
    {
        public string session_id;
        public string status;
        public long received_bytes;

        /// <summary>
        /// A API devolve null para chunk (um pedaço isolado não tem duração de sessão).
        /// O JsonUtility não representa null em float, então aqui isso vira 0 — leia
        /// como "não informado", nunca como "áudio de zero segundo".
        /// </summary>
        public float duration_seconds;

        public string message;
    }

    /// <summary>POST /api/v1/presentations/{id}/analyze (202)</summary>
    [Serializable]
    public class AnalysisAcceptedResponse
    {
        public string session_id;
        public string status;

        /// <summary>Caminho relativo (ex.: /api/v1/presentations/&lt;id&gt;), não URL absoluta.</summary>
        public string poll_url;

        public string message;
    }

    /// <summary>GET /api/v1/presentations/{id}</summary>
    [Serializable]
    public class PresentationResponse
    {
        public string id;
        public string scenario;
        public string persona;
        public string status;
        public string file_filename;
        public string file_type;
        public string created_at;

        /// <summary>Preenchido apenas quando status == failed.</summary>
        public string error_message;
    }

    /// <summary>
    /// Pergunta da banca. `slide_origem` e `trecho_literal` são a âncora de evidência do
    /// sistema — a interface deve mostrá-los, porque é o que distingue uma pergunta
    /// ancorada no material de uma alucinação.
    /// </summary>
    [Serializable]
    public class GeneratedQuestion
    {
        public string question;
        public string rationale;
        public int slide_origem;
        public string trecho_literal;
    }

    /// <summary>Avaliação de FORMA — derivada do sinal de áudio, independente do STT.</summary>
    [Serializable]
    public class SpeechMetrics
    {
        public float duration_seconds;

        /// <summary>Duração menos as pausas. É esta, e não a duração, que embasa o ritmo.</summary>
        public float speech_seconds;

        public int word_count;
        public float words_per_minute;
        public int pause_count;
        public float total_pause_seconds;
        public float longest_pause_seconds;
    }

    /// <summary>Veredicto de cobertura de um slide.</summary>
    [Serializable]
    public class SlideCoverage
    {
        public int numero;

        /// <summary>apresentado | parcial | nao_apresentado | sem_termos</summary>
        public string classificacao;

        /// <summary>A API manda null quando classificacao == "sem_termos"; aqui chega 0.</summary>
        public float score;

        public int termos_totais;
        public int termos_presentes;

        /// <summary>A resposta auditável a "o que você preparou mas não apresentou?".</summary>
        public string[] termos_ausentes;
    }

    [Serializable]
    public class SlideCoverageReport
    {
        public SlideCoverage[] slides;
        public float percentual_coberto;

        /// <summary>Material e fala descolados — o sinal mais forte que a tela deve dar.</summary>
        public bool alerta_descolamento;
    }

    /// <summary>GET /api/v1/presentations/{id}/feedback — o Feedback Duplo.</summary>
    [Serializable]
    public class FeedbackResponse
    {
        public string session_id;
        public string transcript;
        public GeneratedQuestion[] questions;
        public string content_analysis;
        public SpeechMetrics metrics;

        public int perguntas_geradas;
        public int perguntas_aprovadas;
        public float taxa_aterramento;

        public bool slides_truncados;
        public bool transcricao_truncada;

        public SlideCoverageReport slide_coverage;

        /// <summary>
        /// True quando a API mandou `slide_coverage: null` — feedback anterior à cobertura
        /// de slides. Existe porque o JsonUtility materializa um objeto vazio no lugar do
        /// null, e a tela não pode mostrar 0% de cobertura para uma sessão que nunca foi
        /// medida: ausência de medição não é medição zerada.
        /// Preenchido por <see cref="PodiumApiClient"/>, não vem do JSON.
        /// </summary>
        [NonSerialized] public bool coverageNaoMedida;
    }

    /// <summary>Corpo de erro do FastAPI quando `detail` é uma string.</summary>
    [Serializable]
    public class ApiErrorBody
    {
        public string detail;
    }
}
