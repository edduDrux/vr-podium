using System;
using System.Threading.Tasks;
using UnityEngine;
using Whisper;
using Whisper.Utils;

namespace Podium.Audio
{
    /// <summary>
    /// Transcreve a fala no próprio aparelho, enquanto ela acontece, com o whisper.cpp.
    /// <para>
    /// Existe para tirar o STT do caminho crítico. No servidor a transcrição é a etapa
    /// mais cara do pipeline (tokens proporcionais à duração do áudio) e a mais lenta
    /// (0,28× tempo real medido, ou seja ~3 min para uma fala de 10 min); feita aqui, ela
    /// acontece DURANTE a apresentação e já chega pronta ao <c>/analyze</c>, que a
    /// reaproveita e pula o STT. O que sobra de espera é só a geração das perguntas.
    /// </para>
    /// <para>
    /// O áudio continua subindo: as métricas de forma (ritmo, pausas) derivam do sinal, não
    /// do texto, e essa independência é um invariante do projeto. Se o modelo não carregar,
    /// nada quebra — a sessão segue sem transcrição local e o servidor transcreve como
    /// antes. Degradar sem inventar vale para o cliente também.
    /// </para>
    /// </summary>
    public class TranscritorLocal : MonoBehaviour
    {
        [Tooltip("O WhisperManager da cena. Sem ele, a transcrição local fica desligada e " +
                 "quem transcreve é o servidor.")]
        [SerializeField] private WhisperManager whisper;

        [Tooltip("Desligue para forçar o STT do servidor — é assim que se compara as duas " +
                 "transcrições na validação do TCC.")]
        [SerializeField] private bool ligado = true;

        [Tooltip("Nome do motor registrado na API, para a auditoria distinguir as fontes.")]
        [SerializeField] private string motor = "whisper.cpp";

        /// <summary>Frequência que o whisper.cpp exige. Qualquer outra é reamostrada.</summary>
        private const int FrequenciaDoWhisper = 16000;

        private WhisperStream stream;
        private string textoParcial = string.Empty;
        private string textoFinal;
        private bool transmitindo;
        private bool encerrando;
        private string falha;

        /// <summary>Texto acumulado até agora. Cresce enquanto a pessoa fala.</summary>
        public string TextoParcial
        {
            get { return textoParcial; }
        }

        /// <summary>
        /// Texto fechado, disponível só depois de <see cref="Encerrar"/> terminar. Null
        /// enquanto a transcrição não fechou, ou quando ela não rodou.
        /// </summary>
        public string TextoFinal
        {
            get { return textoFinal; }
        }

        /// <summary>Quem transcreveu, no formato que a API registra.</summary>
        public string Motor
        {
            get
            {
                string modelo = whisper != null ? NomeDoModelo(whisper.ModelPath) : null;
                return string.IsNullOrEmpty(modelo) ? motor : motor + " " + modelo;
            }
        }

        /// <summary>Há um WhisperManager ligado e o usuário não desligou a transcrição local.</summary>
        public bool Habilitado
        {
            get { return ligado && whisper != null; }
        }

        /// <summary>O modelo está carregado e pronto para receber áudio.</summary>
        public bool Pronto
        {
            get { return Habilitado && whisper.IsLoaded; }
        }

        public bool Encerrando
        {
            get { return encerrando; }
        }

        /// <summary>Frase curta para o painel dizer em que pé está a transcrição.</summary>
        public string Diagnostico
        {
            get
            {
                if (!ligado)
                {
                    return "Transcrição local desligada; quem transcreve é o servidor.";
                }

                if (whisper == null)
                {
                    return "Sem WhisperManager na cena; quem transcreve é o servidor.";
                }

                if (falha != null)
                {
                    return "Transcrição local falhou (" + falha + "); o servidor assume.";
                }

                if (whisper.IsLoading)
                {
                    return "Carregando o modelo de transcrição...";
                }

                if (!whisper.IsLoaded)
                {
                    return "Modelo de transcrição não carregado; o servidor assume.";
                }

                if (encerrando)
                {
                    return "Fechando a transcrição da fala...";
                }

                if (transmitindo)
                {
                    int palavras = ContarPalavras(textoParcial);
                    return "Transcrevendo aqui mesmo: " + palavras + " palavras.";
                }

                return "Transcrição local pronta (" + Motor + ").";
            }
        }

        /// <summary>
        /// Prepara o fluxo para uma nova apresentação. Chamado antes da primeira amostra;
        /// se o modelo ainda estiver carregando, a espera acontece aqui e não no meio da
        /// fala.
        /// </summary>
        public async Task Iniciar()
        {
            textoParcial = string.Empty;
            textoFinal = null;
            falha = null;

            if (!Habilitado)
            {
                return;
            }

            try
            {
                if (!whisper.IsLoaded)
                {
                    await whisper.InitModel();
                }

                if (!whisper.IsLoaded)
                {
                    falha = "o modelo não carregou";
                    return;
                }

                // Mono: o gravador pede mono e o Unity só entrega um canal de microfone.
                stream = await whisper.CreateStream(FrequenciaDoWhisper, 1);
                if (stream == null)
                {
                    falha = "não foi possível abrir o fluxo";
                    return;
                }

                stream.OnResultUpdated += AoAtualizar;
                stream.OnStreamFinished += AoTerminar;
                stream.StartStream();
                transmitindo = true;
            }
            catch (Exception e)
            {
                falha = e.Message;
                Debug.LogWarning("[Podium] Transcrição local indisponível: " + e.Message +
                                 " — o servidor vai transcrever.");
            }
        }

        /// <summary>
        /// Entrega mais um pedaço do microfone. Ligue ao
        /// <see cref="PresentationRecorder.BlocoCapturado"/>.
        /// </summary>
        public void Alimentar(float[] amostras, int canais, int frequencia)
        {
            if (!transmitindo || stream == null || amostras == null || amostras.Length == 0)
            {
                return;
            }

            float[] mono = AudioUtils.Preprocess(amostras, frequencia, canais, FrequenciaDoWhisper);
            if (mono == null || mono.Length == 0)
            {
                return;
            }

            // A detecção de voz é a mesma do whisper.cpp: sem ela, cada silêncio vira uma
            // inferência que só produz alucinação curta ("obrigado", "legendas por...").
            bool voz = AudioUtils.SimpleVad(mono, FrequenciaDoWhisper, 1.25f, 1.0f, 100.0f);

            stream.AddToStream(new AudioChunk
            {
                Data = mono,
                Frequency = FrequenciaDoWhisper,
                Channels = 1,
                Length = mono.Length / (float)FrequenciaDoWhisper,
                IsVoiceDetected = voz,
            });
        }

        /// <summary>
        /// Fecha a transcrição e espera o último trecho terminar. Devolve o texto, ou null
        /// se a transcrição local não rodou — nesse caso o servidor transcreve.
        /// </summary>
        public async Task<string> Encerrar()
        {
            if (!transmitindo || stream == null)
            {
                return null;
            }

            transmitindo = false;
            encerrando = true;
            stream.StopStream();

            // O StopStream é `async void`: a única forma de saber que acabou é o evento.
            // O teto existe para que uma inferência travada não segure a análise inteira.
            float limite = Time.realtimeSinceStartup + 120f;
            while (textoFinal == null && Time.realtimeSinceStartup < limite)
            {
                await Task.Yield();
            }

            encerrando = false;

            if (textoFinal == null)
            {
                falha = "o fechamento passou de 2 min";
                Debug.LogWarning("[Podium] A transcrição local não fechou a tempo; " +
                                 "o servidor vai transcrever.");
                // O parcial não é devolvido: metade da fala descrita como se fosse a fala
                // inteira contaminaria a cobertura de slides e as perguntas da banca.
                return null;
            }

            return textoFinal;
        }

        private void AoAtualizar(string texto)
        {
            textoParcial = texto ?? string.Empty;
        }

        private void AoTerminar(string texto)
        {
            textoFinal = texto ?? string.Empty;
            textoParcial = textoFinal;
        }

        private void OnDisable()
        {
            if (stream != null)
            {
                stream.OnResultUpdated -= AoAtualizar;
                stream.OnStreamFinished -= AoTerminar;
            }

            transmitindo = false;
        }

        /// <summary>
        /// `Models/ggml-base-q5_1.bin` vira `ggml-base-q5_1`: é isso que identifica a
        /// transcrição no relatório, não o caminho da máquina de quem rodou.
        /// </summary>
        public static string NomeDoModelo(string caminho)
        {
            if (string.IsNullOrEmpty(caminho))
            {
                return null;
            }

            string nome = caminho.Replace('\\', '/');
            int barra = nome.LastIndexOf('/');
            if (barra >= 0)
            {
                nome = nome.Substring(barra + 1);
            }

            int ponto = nome.LastIndexOf('.');
            return ponto > 0 ? nome.Substring(0, ponto) : nome;
        }

        public static int ContarPalavras(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return 0;
            }

            return texto.Split(new[] { ' ', '\n', '\r', '\t' },
                               StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
