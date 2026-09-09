using System;
using NUnit.Framework;
using Podium.Slides;

namespace Podium.Tests
{
    /// <summary>
    /// A ordem dos arquivos é a ordem da apresentação. Ordenado por texto puro, o slide 10
    /// entra logo depois do 1 — e o erro só aparece quando a sala já está montada e a fala
    /// começou.
    /// </summary>
    public class OrdemDosSlidesTests
    {
        [Test]
        public void NumeroMenorVemAntes()
        {
            Assert.Less(ApresentacaoDeSlides.CompararNomes("slide2.png", "slide10.png"), 0);
            Assert.Greater(ApresentacaoDeSlides.CompararNomes("slide10.png", "slide2.png"), 0);
        }

        [Test]
        public void ZeroAEsquerdaNaoMudaOValor()
        {
            Assert.AreEqual(0, ApresentacaoDeSlides.CompararNomes("slide02.png", "slide2.png"));
        }

        [Test]
        public void OrdenaAApresentacaoInteira()
        {
            string[] arquivos =
            {
                "C:/deck/slide10.png",
                "C:/deck/slide1.png",
                "C:/deck/Slide03.PNG",
                "C:/deck/slide2.png",
            };

            Array.Sort(arquivos, ApresentacaoDeSlides.CompararNomes);

            CollectionAssert.AreEqual(
                new[]
                {
                    "C:/deck/slide1.png",
                    "C:/deck/slide2.png",
                    "C:/deck/Slide03.PNG",
                    "C:/deck/slide10.png",
                },
                arquivos);
        }

        /// <summary>Só o nome do arquivo conta: a pasta é a mesma para todos os slides.</summary>
        [Test]
        public void CaminhoNaoInterfere()
        {
            Assert.Less(ApresentacaoDeSlides.CompararNomes("z/slide2.png", "a/slide10.png"), 0);
        }
    }
}
