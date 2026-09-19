using NUnit.Framework;
using Podium.Audio;

namespace Podium.Tests
{
    /// <summary>
    /// A aritmética do anel do microfone.
    /// <para>
    /// Vale um teste próprio porque é a única conta do gravador que erra em silêncio:
    /// quando a posição dá a volta e o cálculo não acompanha, nada estoura — o WAV sai com
    /// a fala fora de ordem, as métricas de forma são calculadas sobre esse áudio e a banca
    /// pergunta sobre uma apresentação que ninguém fez.
    /// </para>
    /// </summary>
    public class CapturaDoMicrofoneTests
    {
        [Test]
        public void AvancoSimplesEhADiferenca()
        {
            Assert.AreEqual(300, PresentationRecorder.AmostrasNovas(1000, 1300, 16000));
        }

        [Test]
        public void SemAvancoNaoHaAmostras()
        {
            Assert.AreEqual(0, PresentationRecorder.AmostrasNovas(1000, 1000, 16000));
        }

        [Test]
        public void AVoltaDoAnelNaoPerdeOTrechoFinal()
        {
            // O microfone estava em 15 900 de 16 000 e passou para 100: entraram as 100
            // amostras até o fim do anel mais as 100 do começo.
            Assert.AreEqual(200, PresentationRecorder.AmostrasNovas(15900, 100, 16000));
        }

        [Test]
        public void AUltimaAmostraAntesDaVoltaEhContada()
        {
            Assert.AreEqual(1, PresentationRecorder.AmostrasNovas(15999, 0, 16000));
        }

        [Test]
        public void AnelInvalidoNaoProduzLeituraNegativa()
        {
            Assert.AreEqual(0, PresentationRecorder.AmostrasNovas(10, 5, 0));
        }
    }
}
