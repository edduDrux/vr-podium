using System;
using UnityEngine;

namespace Podium.Audio
{
    /// <summary>
    /// Grava o microfone do headset em pedaços de duração fixa e entrega cada pedaço
    /// pronto para upload.
    /// <para>
    /// Grava em chunks em vez de um arquivo único por três motivos: 15 minutos de PCM
    /// vivem na memória do Quest o tempo todo; o upload inteiro só começaria depois da
    /// fala, somando minutos de espera a um pipeline que já leva minutos; e uma queda de
    /// rede no fim perderia a apresentação toda. A API aceita chunks nativamente
    /// (is_chunk=true) e os concatena no /analyze.
    /// </para>
    /// </summary>
    public class PresentationRecorder : MonoBehaviour
    {
        [Header("Captura")]
        [Tooltip("16 kHz mono é exatamente o que o backend usa no STT. Gravar acima disso " +
                 "só aumenta o upload — a conversão descartaria a diferença.")]
        [SerializeField] private int sampleRate = 16000;

        [Tooltip("Duração de cada pedaço enviado. 60 s equilibra memória e número de requisições.")]
        [SerializeField] private int chunkSeconds = 60;

        [Tooltip("Teto do MVP (MAX_AUDIO_DURATION_MINUTES no backend). Acima disso o " +
                 "/analyze responde 413 e a sessão inteira se perde — melhor parar antes.")]
        [SerializeField] private int maxMinutes = 15;

        // O buffer do microfone é alocado MAIOR do que o pedaço. Com loop=false o Unity
        // para a captura sozinho quando o buffer enche, e o que GetPosition devolve
        // nesse estado não é garantido. Fechando o pedaço aos `chunkSeconds` num buffer
        // de `chunkSeconds + margem`, o microfone ainda está gravando quando a posição é
        // lida — mesmo que o Update atrase alguns segundos numa travada.
        private const int BufferMarginSeconds = 5;

        /// <summary>Disparado a cada pedaço com áudio: (wav, índice).</summary>
        public event Action<byte[], int> ChunkReady;

        /// <summary>
        /// Disparado quando a gravação encerra, DEPOIS do último ChunkReady. É este sinal,
        /// e não um chunk marcado como "último", que autoriza o /analyze: assim um pedaço
        /// final sem amostras não precisa virar um WAV vazio só para carregar o aviso.
        /// </summary>
        public event Action RecordingStopped;

        /// <summary>Disparado quando a gravação parou sozinha por atingir o teto de duração.</summary>
        public event Action LimitReached;

        private string device;
        private AudioClip clip;
        private float chunkStartedAt;
        private float sessionStartedAt;
        private int chunkIndex;
        private bool recording;

        public bool IsRecording
        {
            get { return recording; }
        }

        /// <summary>Segundos gravados desde o início da sessão.</summary>
        public float ElapsedSeconds
        {
            get { return recording ? Time.realtimeSinceStartup - sessionStartedAt : 0f; }
        }

        public float MaxSeconds
        {
            get { return maxMinutes * 60f; }
        }

        public int ChunksEmitted
        {
            get { return chunkIndex; }
        }

        public bool StartRecording()
        {
            if (recording)
            {
                return true;
            }

            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[Podium] Nenhum microfone disponível. No Quest, o app " +
                               "precisa da permissão de microfone concedida.");
                return false;
            }

            device = Microphone.devices[0];
            sampleRate = ClampToDeviceCaps(device, sampleRate);

            sessionStartedAt = Time.realtimeSinceStartup;
            chunkIndex = 0;
            recording = true;

            return StartChunk();
        }

        /// <summary>Fecha o pedaço corrente, encerra e avisa por RecordingStopped.</summary>
        public void StopRecording()
        {
            if (!recording)
            {
                return;
            }

            recording = false;
            FecharChunk();

            if (RecordingStopped != null)
            {
                RecordingStopped();
            }
        }

        private void Update()
        {
            if (!recording)
            {
                return;
            }

            if (ElapsedSeconds >= MaxSeconds)
            {
                StopRecording();
                if (LimitReached != null)
                {
                    LimitReached();
                }
                return;
            }

            if (Time.realtimeSinceStartup - chunkStartedAt >= chunkSeconds)
            {
                FecharChunk();
                StartChunk();
            }
        }

        private bool StartChunk()
        {
            clip = Microphone.Start(device, false, chunkSeconds + BufferMarginSeconds, sampleRate);
            if (clip == null)
            {
                Debug.LogError("[Podium] Microphone.Start falhou para o dispositivo " + device);
                recording = false;
                return false;
            }

            chunkStartedAt = Time.realtimeSinceStartup;
            return true;
        }

        private void FecharChunk()
        {
            if (clip == null)
            {
                return;
            }

            int posicao;
            if (Microphone.IsRecording(device))
            {
                // Lida ANTES do End: depois dele, GetPosition devolve 0.
                posicao = Microphone.GetPosition(device);
            }
            else
            {
                // O buffer encheu antes de o Update chegar aqui — só acontece com uma
                // travada maior do que a margem. Nesse estado a posição não é confiável;
                // o buffer inteiro é o único valor seguro.
                posicao = clip.samples;
                Debug.LogWarning("[Podium] Buffer do microfone encheu antes do fechamento do " +
                                 "pedaço " + chunkIndex + "; usando o buffer inteiro.");
            }

            Microphone.End(device);

            if (posicao > 0)
            {
                float[] amostras = new float[posicao * clip.channels];
                clip.GetData(amostras, 0);

                byte[] wav = WavEncoder.FromSamples(amostras, clip.channels, clip.frequency);
                if (ChunkReady != null)
                {
                    ChunkReady(wav, chunkIndex);
                }
                chunkIndex++;
            }

            Destroy(clip);
            clip = null;
        }

        private void OnDisable()
        {
            if (recording)
            {
                StopRecording();
            }
        }

        private static int ClampToDeviceCaps(string device, int desejado)
        {
            int min;
            int max;
            Microphone.GetDeviceCaps(device, out min, out max);

            // (0, 0) significa "qualquer frequência serve".
            if (min == 0 && max == 0)
            {
                return desejado;
            }

            return Mathf.Clamp(desejado, min, max);
        }
    }
}
