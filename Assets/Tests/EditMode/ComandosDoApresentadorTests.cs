using NUnit.Framework;
using Podium.XR;

namespace Podium.Tests
{
    /// <summary>
    /// As regras dos comandos que não pedem mira: para onde vão o avançar e o voltar, e
    /// como se distingue a mão do controle.
    /// <para>
    /// Errar a primeira faz o A do controle passar slide enquanto a pessoa lê as perguntas
    /// da banca. Errar a segunda esconde o modelo do controle de quem está segurando um —
    /// ou deixa um controle flutuando na ponta dos dedos de quem usa as mãos.
    /// </para>
    /// </summary>
    public class ComandosDoApresentadorTests
    {
        [Test]
        public void NoFeedbackOsComandosAndamNasPerguntas()
        {
            Assert.AreEqual(AlvoDaNavegacao.Perguntas, ComandosDoApresentador.AlvoPara(true, true));
            Assert.AreEqual(AlvoDaNavegacao.Perguntas, ComandosDoApresentador.AlvoPara(true, false));
        }

        [Test]
        public void ForaDoFeedbackOsComandosAndamNosSlides()
        {
            Assert.AreEqual(AlvoDaNavegacao.Slides, ComandosDoApresentador.AlvoPara(false, true));
        }

        [Test]
        public void SemSlidesNemPerguntasOBotaoNaoFazNada()
        {
            Assert.AreEqual(AlvoDaNavegacao.Nenhum, ComandosDoApresentador.AlvoPara(false, false));
        }

        /// <summary>Os layouts que o OpenXR e o XR Hands registram para as mãos.</summary>
        [Test]
        public void MaosSaoReconhecidasComoMaos()
        {
            Assert.IsTrue(ComandosDoApresentador.EhMao("HandInteraction"));
            Assert.IsTrue(ComandosDoApresentador.EhMao("HandInteractionPoses"));
            Assert.IsTrue(ComandosDoApresentador.EhMao("MetaAimHand"));
            Assert.IsTrue(ComandosDoApresentador.EhMao("XRHandDevice"));
        }

        /// <summary>Touch (Quest 2), Touch Plus (Quest 3/3S) e Touch Pro, pelos nomes do OpenXR.</summary>
        [Test]
        public void ControlesDaMetaSaoControles()
        {
            Assert.IsFalse(ComandosDoApresentador.EhMao("OculusTouchController"));
            Assert.IsFalse(ComandosDoApresentador.EhMao("QuestTouchPlusController"));
            Assert.IsFalse(ComandosDoApresentador.EhMao("QuestProTouchController"));
            Assert.IsFalse(ComandosDoApresentador.EhMao("KHRSimpleController"));
        }

        [Test]
        public void SemLayoutNaoHaMao()
        {
            Assert.IsFalse(ComandosDoApresentador.EhMao(null));
            Assert.IsFalse(ComandosDoApresentador.EhMao(string.Empty));
        }

        /// <summary>Quem segura um controle numa mão está usando os botões dele.</summary>
        [Test]
        public void ControleEmUmLadoBastaParaAAjudaFalarDeBotoes()
        {
            Assert.AreEqual(ModoDeEntrada.Controles,
                            ComandosDoApresentador.Combinar(ModoDeEntrada.Controles, ModoDeEntrada.Maos));
            Assert.AreEqual(ModoDeEntrada.Controles,
                            ComandosDoApresentador.Combinar(ModoDeEntrada.Nenhum, ModoDeEntrada.Controles));
        }

        [Test]
        public void SoMaosViramModoDeMaos()
        {
            Assert.AreEqual(ModoDeEntrada.Maos,
                            ComandosDoApresentador.Combinar(ModoDeEntrada.Maos, ModoDeEntrada.Nenhum));
            Assert.AreEqual(ModoDeEntrada.Nenhum,
                            ComandosDoApresentador.Combinar(ModoDeEntrada.Nenhum, ModoDeEntrada.Nenhum));
        }
    }
}
