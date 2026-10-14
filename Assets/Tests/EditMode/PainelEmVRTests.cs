using NUnit.Framework;
using Podium.Api;
using Podium.XR;
using UnityEngine;

namespace Podium.Tests
{
    /// <summary>
    /// O painel do headset precisa estar de frente para quem apresenta.
    /// <para>
    /// Um Canvas em world space é lido pelo lado de -Z: a frente dele aponta para longe de
    /// quem olha. A versão anterior girava o painel 180° como se fosse o contrário, e ele
    /// ficava de costas e quase de perfil — o "menu que não aparece" no Quest. O raio do XR
    /// Interaction Toolkit usa a mesma regra: um gráfico está de frente quando a direção do
    /// raio e a frente dele concordam.
    /// </para>
    /// </summary>
    public class PainelEmVRTests
    {
        private static readonly Vector3 Olho = new Vector3(0f, 1.6f, 0f);

        [Test]
        public void FrenteDoPainelApontaNaDirecaoDoOlhar()
        {
            Vector3 lugar = new Vector3(0.85f, 1.45f, 0.8f);
            Quaternion giro = PainelEmVR.VoltadoPara(lugar, Olho);

            Vector3 frente = giro * Vector3.forward;
            Vector3 olhar = (lugar - Olho).normalized;

            Assert.Greater(Vector3.Dot(frente, olhar), 0.999f);
        }

        [Test]
        public void PainelDeQualquerLadoFicaDeFrente()
        {
            Vector3[] lugares =
            {
                new Vector3(-0.85f, 1.35f, 0.75f),
                new Vector3(0f, 1.6f, 1.2f),
                new Vector3(2f, 1.2f, -1f),
            };

            foreach (Vector3 lugar in lugares)
            {
                Vector3 frente = PainelEmVR.VoltadoPara(lugar, Olho) * Vector3.forward;
                Assert.Greater(Vector3.Dot(frente, (lugar - Olho).normalized), 0.999f,
                               "painel em " + lugar);
            }
        }

        /// <summary>Virar para o olho pode inclinar o painel para trás, nunca de lado.</summary>
        [Test]
        public void PainelNaoTombaDeLado()
        {
            Quaternion giro = PainelEmVR.VoltadoPara(new Vector3(-1f, 1.3f, 1f), Olho);

            Assert.Greater((giro * Vector3.up).y, 0.9f);
            Assert.AreEqual(0f, (giro * Vector3.right).y, 0.0001f);
        }

        [Test]
        public void OlhoNoProprioPainelNaoGeraRotacaoInvalida()
        {
            Assert.AreEqual(Quaternion.identity, PainelEmVR.VoltadoPara(Olho, Olho));
        }

        /// <summary>O resumo leva ao headset o que antes só o painel do PC mostrava.</summary>
        [Test]
        public void ResumoTrazPerguntasFormaCoberturaEFonte()
        {
            FeedbackResponse f = new FeedbackResponse
            {
                perguntas_geradas = 6,
                perguntas_aprovadas = 5,
                taxa_aterramento = 0.83f,
                metrics = new SpeechMetrics
                {
                    words_per_minute = 131.4f,
                    pause_count = 12,
                    longest_pause_seconds = 2.4f,
                    duration_seconds = 437f,
                },
                slide_coverage = new SlideCoverageReport
                {
                    percentual_coberto = 0.72f,
                    alerta_descolamento = true,
                },
                transcript_source = "cliente_vr (whisper.cpp ggml-base-q5_1)",
            };

            string texto = PainelEmVR.TextoDoResumo(f);

            StringAssert.Contains("5 de 6 perguntas aprovadas (aterramento 83%)", texto);
            StringAssert.Contains("131 palavras/min · 12 pausas · maior pausa 2.4 s · fala de 7:17",
                                  texto);
            StringAssert.Contains("72% do material apresentado — ALERTA", texto);
            StringAssert.Contains("Transcrição por cliente_vr (whisper.cpp ggml-base-q5_1)", texto);
        }

        /// <summary>Ausência de medição não é cobertura zero — a mesma regra do painel do PC.</summary>
        [Test]
        public void CoberturaNaoMedidaNaoViraZero()
        {
            FeedbackResponse f = new FeedbackResponse
            {
                coverageNaoMedida = true,
                slide_coverage = new SlideCoverageReport(),
            };

            string texto = PainelEmVR.TextoDoResumo(f);

            StringAssert.Contains("não medida", texto);
            StringAssert.DoesNotContain("0% do material", texto);
        }

        /// <summary>Texto longo demais para o quadro é cortado com aviso, não em silêncio.</summary>
        [Test]
        public void AnaliseLongaECortadaComAviso()
        {
            FeedbackResponse f = new FeedbackResponse { content_analysis = new string('a', 3000) };

            string texto = PainelEmVR.TextoDoResumo(f);

            StringAssert.Contains("íntegra no painel do PC", texto);
            Assert.Less(texto.Length, 800);
        }

        [Test]
        public void SemFeedbackOResumoFicaVazio()
        {
            Assert.AreEqual(string.Empty, PainelEmVR.TextoDoResumo(null));
        }
    }
}
