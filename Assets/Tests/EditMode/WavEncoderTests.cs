using System;
using System.Text;
using NUnit.Framework;
using Podium.Audio;

namespace Podium.Tests
{
    /// <summary>
    /// O WAV é o que o backend decodifica com FFmpeg. Um cabeçalho errado não dá erro
    /// aqui — dá 422 no /analyze, longe da causa.
    /// </summary>
    public class WavEncoderTests
    {
        [Test]
        public void CabecalhoDescreveOConteudo()
        {
            byte[] wav = WavEncoder.FromSamples(new float[] { 0f, 0.5f, -0.5f, 1f }, 1, 16000);

            Assert.AreEqual(44 + 4 * 2, wav.Length);
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.AreEqual("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.AreEqual("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
            Assert.AreEqual("data", Encoding.ASCII.GetString(wav, 36, 4));

            Assert.AreEqual(1, BitConverter.ToInt16(wav, 20), "formato PCM");
            Assert.AreEqual(1, BitConverter.ToInt16(wav, 22), "canais");
            Assert.AreEqual(16000, BitConverter.ToInt32(wav, 24), "taxa");
            Assert.AreEqual(16000 * 2, BitConverter.ToInt32(wav, 28), "byte rate");
            Assert.AreEqual(16, BitConverter.ToInt16(wav, 34), "bits por amostra");
            Assert.AreEqual(8, BitConverter.ToInt32(wav, 40), "tamanho dos dados");
            Assert.AreEqual(36 + 8, BitConverter.ToInt32(wav, 4), "tamanho RIFF");
        }

        [Test]
        public void AmostrasViramPcm16ComEscalaCorreta()
        {
            byte[] wav = WavEncoder.FromSamples(new float[] { 0f, 1f, -1f, 0.5f }, 1, 16000);

            Assert.AreEqual(0, BitConverter.ToInt16(wav, 44));
            Assert.AreEqual(short.MaxValue, BitConverter.ToInt16(wav, 46));
            Assert.AreEqual(-short.MaxValue, BitConverter.ToInt16(wav, 48));
            Assert.AreEqual((short)(0.5f * short.MaxValue), BitConverter.ToInt16(wav, 50));
        }

        [Test]
        public void ValoresForaDaFaixaSaoLimitadosEmVezDeEstourar()
        {
            // Estouro de short vira estalo, e estalo vira "fala" para o detector de pausas.
            byte[] wav = WavEncoder.FromSamples(new float[] { 2f, -3f }, 1, 16000);

            Assert.AreEqual(short.MaxValue, BitConverter.ToInt16(wav, 44));
            Assert.AreEqual(-short.MaxValue, BitConverter.ToInt16(wav, 46));
        }

        [Test]
        public void SemAmostrasProduzSoOCabecalho()
        {
            byte[] wav = WavEncoder.FromSamples(new float[0], 1, 16000);

            Assert.AreEqual(44, wav.Length);
            Assert.AreEqual(0, BitConverter.ToInt32(wav, 40));
        }

        [Test]
        public void EstereoDobraBlockAlignEByteRate()
        {
            byte[] wav = WavEncoder.FromSamples(new float[] { 0f, 0f }, 2, 48000);

            Assert.AreEqual(2, BitConverter.ToInt16(wav, 22));
            Assert.AreEqual(48000 * 2 * 2, BitConverter.ToInt32(wav, 28));
            Assert.AreEqual(4, BitConverter.ToInt16(wav, 32), "block align");
        }
    }
}
