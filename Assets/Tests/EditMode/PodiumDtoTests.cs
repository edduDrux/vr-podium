using NUnit.Framework;
using Podium.Api;
using UnityEngine;

namespace Podium.Tests
{
    /// <summary>
    /// Os DTOs são lidos pelo JsonUtility, que tem limitações conhecidas (sem nulos em
    /// tipos de valor, sem dicionários). Estes testes fixam o contrato com o JSON que a
    /// API realmente devolve — os corpos abaixo são cópias de respostas reais.
    /// </summary>
    public class PodiumDtoTests
    {
        private const string FeedbackReal =
            @"{""session_id"":""fee53f5f-beae-44d7-b18a-fbf076f30507"",""transcript"":"""",""questions"":[{""question"":""Qual é a fonte?"",""rationale"":null,""slide_origem"":2,""trecho_literal"":""Falar em público é a principal dificuldade""}],""content_analysis"":""x"",""metrics"":{""duration_seconds"":7.2,""speech_seconds"":5.97,""word_count"":0,""words_per_minute"":0.0,""pause_count"":1,""total_pause_seconds"":1.23,""longest_pause_seconds"":1.23},""perguntas_geradas"":6,""perguntas_aprovadas"":6,""slides_truncados"":false,""transcricao_truncada"":false,""slide_coverage"":{""slides"":[{""numero"":1,""classificacao"":""sem_termos"",""score"":null,""termos_totais"":0,""termos_presentes"":0,""termos_ausentes"":[]},{""numero"":2,""classificacao"":""nao_apresentado"",""score"":0.0,""termos_totais"":9,""termos_presentes"":0,""termos_ausentes"":[""publico"",""dificuldade""]}],""percentual_coberto"":0.0,""alerta_descolamento"":true},""taxa_aterramento"":1.0}";

        [Test]
        public void FeedbackRealEhLidoPorInteiro()
        {
            FeedbackResponse f = JsonUtility.FromJson<FeedbackResponse>(FeedbackReal);

            Assert.AreEqual("fee53f5f-beae-44d7-b18a-fbf076f30507", f.session_id);
            Assert.AreEqual(1, f.questions.Length);
            Assert.AreEqual(2, f.questions[0].slide_origem);
            Assert.AreEqual(1, f.metrics.pause_count);
            Assert.AreEqual(1.23f, f.metrics.longest_pause_seconds, 0.001f);
            Assert.AreEqual(6, f.perguntas_aprovadas);
            Assert.AreEqual(1.0f, f.taxa_aterramento, 0.001f);
            Assert.IsTrue(f.slide_coverage.alerta_descolamento);
            Assert.AreEqual(2, f.slide_coverage.slides.Length);
            Assert.AreEqual("sem_termos", f.slide_coverage.slides[0].classificacao);
            Assert.AreEqual(2, f.slide_coverage.slides[1].termos_ausentes.Length);
        }

        /// <summary>
        /// `"rationale": null` chega como string VAZIA, não como null.
        /// <para>
        /// O JsonUtility não representa null em string: ele materializa `string.Empty`.
        /// A consequência prática é a regra que a tela precisa seguir — testar um campo
        /// opcional com `!= null` o daria sempre por preenchido, e a interface mostraria
        /// "Por quê:" seguido de nada. Todo campo opcional de texto se testa com
        /// `string.IsNullOrEmpty`, e é isso que o painel faz.
        /// </para>
        /// </summary>
        [Test]
        public void TextoNuloChegaVazioENaoNulo()
        {
            FeedbackResponse f = JsonUtility.FromJson<FeedbackResponse>(FeedbackReal);

            Assert.IsEmpty(f.questions[0].rationale);
            Assert.IsTrue(string.IsNullOrEmpty(f.questions[0].rationale));
        }

        [Test]
        public void ScoreNuloViraZeroSemQuebrarALeitura()
        {
            // Perda conhecida e documentada no DTO: `classificacao` é quem diz "sem_termos".
            FeedbackResponse f = JsonUtility.FromJson<FeedbackResponse>(FeedbackReal);

            Assert.AreEqual(0f, f.slide_coverage.slides[0].score);
        }

        [Test]
        public void CoberturaNulaEhDetectadaNoCorpoBruto()
        {
            Assert.IsTrue(PodiumApiClient.CoberturaNaoMedida(@"{""slide_coverage"":null}"));
            Assert.IsTrue(PodiumApiClient.CoberturaNaoMedida(@"{ ""slide_coverage"": null }"));
            Assert.IsFalse(PodiumApiClient.CoberturaNaoMedida(FeedbackReal));
            Assert.IsFalse(PodiumApiClient.CoberturaNaoMedida(""));
            Assert.IsFalse(PodiumApiClient.CoberturaNaoMedida(null));
        }

        [Test]
        public void DuracaoNulaDoChunkViraZero()
        {
            // A API manda null para chunk; o DTO documenta que 0 significa "não informado".
            const string corpo =
                @"{""session_id"":""s"",""status"":""audio_received"",""received_bytes"":12345,""duration_seconds"":null,""message"":""ok""}";

            AudioUploadResponse r = JsonUtility.FromJson<AudioUploadResponse>(corpo);

            Assert.AreEqual(12345, r.received_bytes);
            Assert.AreEqual(0f, r.duration_seconds);
            Assert.AreEqual(SessionStatus.AudioReceived, r.status);
        }

        [Test]
        public void RespostaDoInitEhLida()
        {
            const string corpo =
                @"{""session_id"":""abc"",""status"":""created"",""file_type"":""pdf"",""slides_char_count"":1211}";

            PresentationInitResponse r = JsonUtility.FromJson<PresentationInitResponse>(corpo);

            Assert.AreEqual("abc", r.session_id);
            Assert.AreEqual(SessionStatus.Created, r.status);
            Assert.AreEqual(1211, r.slides_char_count);
        }

        [Test]
        public void ErroDoFastApiComDetailString()
        {
            ApiErrorBody e = JsonUtility.FromJson<ApiErrorBody>(@"{""detail"":""Sessão x não encontrada.""}");

            Assert.AreEqual("Sessão x não encontrada.", e.detail);
        }

        [Test]
        public void ApiRootIgnoraBarraFinal()
        {
            PodiumApiConfig config = ScriptableObject.CreateInstance<PodiumApiConfig>();
            config.baseUrl = "http://10.0.0.5:8000/";

            Assert.AreEqual("http://10.0.0.5:8000/api/v1", config.ApiRoot);

            Object.DestroyImmediate(config);
        }
    }
}
