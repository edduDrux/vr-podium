using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Podium.Audio
{
    /// <summary>
    /// Grava o microfone do headset e entrega a fala por dois caminhos ao mesmo tempo:
    /// pedaços de duração fixa prontos para upload e blocos curtos para quem transcreve
    /// enquanto a pessoa fala.
    /// <para>
    /// Grava em chunks em vez de um arquivo único por três motivos: 15 minutos de PCM
    /// vivem na memória do Quest o tempo todo; o upload inteiro só começaria depois da
    /// fala, somando minutos de espera a um pipeline que já leva minutos; e uma queda de
    /// rede no fim perderia a apresentação toda. A API aceita chunks nativamente
    /// (is_chunk=true) e os concatena no /analyze.
    /// </para>
    /// <para>
    /// O microfone é drenado a cada quadro, e não lido só no fim do pedaço. É o que
    /// permite um único leitor servir dois consumidores: o <see cref="ChunkReady"/> junta
    /// os blocos até fechar um minuto de WAV, e o <see cref="BlocoCapturado"/> entrega o
    /// mesmo áudio em tempo real para a transcrição local. Dois <c>Microphone.Start</c> no
    /// mesmo dispositivo não coexistem — o segundo reinicia o primeiro.
    /// </para>
    /// </summary>
    public class PresentationRecorder : MonoBehaviour
    {
        [Header("Captura")]
        [Tooltip("16 kHz mono é exatamente o que o backend usa no STT, e também o que o " +
                 "Whisper espera. Gravar acima disso só aumenta o upload.")]
        [SerializeField] private int sampleRate = 16000;

        [Tooltip("Duração de cada pedaço enviado. 60 s equilibra memória e número de requisições.")]
        [SerializeField] private int chunkSeconds = 60;

        [Tooltip("Teto do MVP (MAX_AUDIO_DURATION_MINUTES no backend). Acima disso o " +
                 "/analyze responde 413 e a sessão inteira se perde — melhor parar antes.")]
        [SerializeField] private int maxMinutes = 15;

        // O anel do microfone só precisa aguentar o intervalo entre duas drenagens. Dez
        // segundos é folga de sobra para um quadro, e sobrevive a uma travada de
        // carregamento de slide sem perder fala.
        private const int SegundosDeBuffer = 10;

        /// <summary>Disparado a cada pedaço com áudio: (wav, índice).</summary>
        public event Action<byte[], int> ChunkReady;

        /// <summary>
        /// Disparado a cada drenagem do microfone: (amostras intercaladas, canais,
        /// frequência). É o áudio cru, do jeito que saiu do dispositivo — quem transcreve
        /// reamostra se precisar.
        /// </summary>
        public event Action<float[], int, int> BlocoCapturado;

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
        private float sessionStartedAt;
        private int ultimaPosicao;
        private int chunkIndex;
        private bool recording;

        // Amostras intercaladas ainda não fechadas em um pedaço.
        private readonly List<float> acumulado = new List<float>();

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

        /// <summary>Frequência realmente usada — o dispositivo pode não aceitar a pedida.</summary>
        public int SampleRate
        {
            get { return clip != null ? clip.frequency : sampleRate; }
        }

        /// <summary>
        /// Pede a permissão de microfone assim que a cena abre, e não na hora de gravar.
        /// <para>
        /// No Android o microfone é permissão perigosa: sem ela concedida,
        /// <c>Microphone.devices</c> vem vazio e a única pista é "nenhum microfone
        /// disponível" — que parece defeito de hardware. Pedindo no início, a caixa do
        /// sistema aparece enquanto a pessoa ainda está no lobby, e não no instante em que
        /// ela começou a falar.
        /// </para>
        /// </summary>
        private void Start()
        {
            PedirPermissao();
        }

        private static void PedirPermissao()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
            }
#endif
        }

        /// <summary>A permissão já foi concedida (ou a plataforma não pede nenhuma).</summary>
        public static bool TemPermissao
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            get { return Permission.HasUserAuthorizedPermission(Permission.Microphone); }
#else
            get { return true; }
#endif
        }

        public bool StartRecording()
        {
            if (recording)
            {
                return true;
            }

            if (!TemPermissao)
            {
                // Pedir de novo: quem negou por engano ganha uma segunda caixa em vez de
                // um app que nunca mais grava.
                PedirPermissao();
                Debug.LogError("[Podium] Sem permissão de microfone. Conceda na caixa do " +
                               "sistema e comece a apresentação de novo.");
                return false;
            }

            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[Podium] Nenhum microfone disponível. No Quest, confira a " +
                               "permissão de microfone nas configurações do app.");
                return false;
            }

            device = Microphone.devices[0];
            sampleRate = ClampToDeviceCaps(device, sampleRate);

            // loop=true: o anel nunca "enche e para", então a posição lida é sempre
            // confiável e a drenagem por quadro não depende de o Update chegar a tempo.
            clip = Microphone.Start(device, true, SegundosDeBuffer, sampleRate);
            if (clip == null)
            {
                Debug.LogError("[Podium] Microphone.Start falhou para o dispositivo " + device);
                return false;
            }

            acumulado.Clear();
            ultimaPosicao = 0;
            chunkIndex = 0;
            sessionStartedAt = Time.realtimeSinceStartup;
            recording = true;

            return true;
        }

        /// <summary>Fecha o pedaço corrente, encerra e avisa por RecordingStopped.</summary>
        public void StopRecording()
        {
            if (!recording)
            {
                return;
            }

            recording = false;

            Drenar();
            Microphone.End(device);
            FecharChunk();

            Destroy(clip);
            clip = null;

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

            Drenar();

            if (acumulado.Count >= AmostrasPorChunk())
            {
                FecharChunk();
            }
        }

        private int AmostrasPorChunk()
        {
            return chunkSeconds * clip.frequency * clip.channels;
        }

        /// <summary>
        /// Copia do anel do microfone tudo que entrou desde a última leitura, guarda para
        /// o pedaço em formação e entrega a quem transcreve.
        /// </summary>
        private void Drenar()
        {
            if (clip == null)
            {
                return;
            }

            int posicao = Microphone.GetPosition(device);
            int quadrosNovos = AmostrasNovas(ultimaPosicao, posicao, clip.samples);

            if (quadrosNovos <= 0)
            {
                return;
            }

            if (quadrosNovos > clip.samples / 2)
            {
                // Metade do anel numa drenagem só significa que o quadro demorou segundos.
                // A fala anterior a isto pode ter sido sobrescrita pelo próprio microfone.
                Debug.LogWarning("[Podium] O microfone avançou " + quadrosNovos +
                                 " amostras entre duas leituras; parte da fala pode ter " +
                                 "sido perdida numa travada.");
            }

            float[] bloco = new float[quadrosNovos * clip.channels];

            // GetData dá a volta sozinho quando a leitura passa do fim do anel.
            clip.GetData(bloco, ultimaPosicao);
            ultimaPosicao = posicao;

            acumulado.AddRange(bloco);

            if (BlocoCapturado != null)
            {
                BlocoCapturado(bloco, clip.channels, clip.frequency);
            }
        }

        /// <summary>
        /// Quantos quadros entraram no anel entre duas posições. Separado e público porque
        /// a volta do anel (a posição nova menor que a anterior) é a única aritmética aqui
        /// que erra em silêncio: errada, ela não estoura — só entrega fala embaralhada.
        /// </summary>
        public static int AmostrasNovas(int anterior, int atual, int tamanhoDoAnel)
        {
            if (tamanhoDoAnel <= 0)
            {
                return 0;
            }

            int avanco = atual - anterior;
            return avanco >= 0 ? avanco : avanco + tamanhoDoAnel;
        }

        private void FecharChunk()
        {
            if (acumulado.Count == 0 || clip == null)
            {
                return;
            }

            byte[] wav = WavEncoder.FromSamples(acumulado.ToArray(), clip.channels, clip.frequency);
            acumulado.Clear();

            if (ChunkReady != null)
            {
                ChunkReady(wav, chunkIndex);
            }

            chunkIndex++;
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
