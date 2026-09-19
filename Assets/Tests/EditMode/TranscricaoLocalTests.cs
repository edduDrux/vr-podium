using NUnit.Framework;
using Podium.Audio;

namespace Podium.Tests
{
    /// <summary>
    /// O que a transcrição local manda para a auditoria da API.
    /// <para>
    /// O `engine` que sobe no PUT vira o `transcript_source` do feedback, e é por ele que o
    /// relatório distingue a acurácia do whisper do headset da do STT do servidor. Um
    /// caminho de máquina no lugar do nome do modelo não identificaria nada — e ainda
    /// vazaria o nome de usuário de quem rodou.
    /// </para>
    /// </summary>
    public class TranscricaoLocalTests
    {
        [Test]
        public void NomeDoModeloSaiSemPastaESemExtensao()
        {
            Assert.AreEqual("ggml-base-q5_1",
                            TranscritorLocal.NomeDoModelo("Models/ggml-base-q5_1.bin"));
        }

        [Test]
        public void CaminhoDoWindowsTambemEhReduzido()
        {
            Assert.AreEqual("ggml-tiny",
                            TranscritorLocal.NomeDoModelo(@"C:\Users\alguem\ggml-tiny.bin"));
        }

        [Test]
        public void NomeSemExtensaoPassaInteiro()
        {
            Assert.AreEqual("ggml-small", TranscritorLocal.NomeDoModelo("ggml-small"));
        }

        [Test]
        public void SemCaminhoNaoHaNome()
        {
            Assert.IsNull(TranscritorLocal.NomeDoModelo(null));
            Assert.IsNull(TranscritorLocal.NomeDoModelo(""));
        }

        [Test]
        public void ContagemDePalavrasIgnoraEspacoEQuebraDeLinha()
        {
            Assert.AreEqual(0, TranscritorLocal.ContarPalavras(null));
            Assert.AreEqual(0, TranscritorLocal.ContarPalavras("   \n  "));
            Assert.AreEqual(4, TranscritorLocal.ContarPalavras("  bom dia\n a  todos "));
        }
    }
}
