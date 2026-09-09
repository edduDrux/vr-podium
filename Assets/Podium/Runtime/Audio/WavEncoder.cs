using System;
using System.IO;
using UnityEngine;

namespace Podium.Audio
{
    /// <summary>
    /// AudioClip para WAV PCM 16 bits.
    /// <para>
    /// WAV, e não um formato comprimido: o Unity não traz codificador de MP3/Opus, e o
    /// backend já normaliza tudo para MP3 mono 16 kHz antes do STT
    /// (audio_service.normalize_for_stt). Comprimir aqui seria reimplementar, no
    /// headset, trabalho que o servidor já faz melhor — e o upload de áudio da API não
    /// tem teto de tamanho (save_upload sem max_bytes). O que economiza banda é gravar
    /// mono a 16 kHz, que é exatamente onde o áudio vai parar de qualquer forma.
    /// </para>
    /// </summary>
    public static class WavEncoder
    {
        private const int BitsPerSample = 16;
        private const int HeaderBytes = 44;

        public static byte[] FromSamples(float[] samples, int channels, int sampleRate)
        {
            if (samples == null)
            {
                throw new ArgumentNullException("samples");
            }

            int dataBytes = samples.Length * sizeof(short);

            using (MemoryStream stream = new MemoryStream(HeaderBytes + dataBytes))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int byteRate = sampleRate * channels * (BitsPerSample / 8);
                short blockAlign = (short)(channels * (BitsPerSample / 8));

                writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new char[] { 'W', 'A', 'V', 'E' });
                writer.Write(new char[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write((short)BitsPerSample);
                writer.Write(new char[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);

                for (int i = 0; i < samples.Length; i++)
                {
                    // Clamp antes da conversão: o microfone pode entregar valor fora de
                    // [-1, 1] e o estouro do short viraria estalo — que o detector de
                    // pausas do backend leria como fala.
                    float amostra = Mathf.Clamp(samples[i], -1f, 1f);
                    writer.Write((short)(amostra * short.MaxValue));
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        public static byte[] FromClip(AudioClip clip)
        {
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            return FromSamples(samples, clip.channels, clip.frequency);
        }
    }
}
