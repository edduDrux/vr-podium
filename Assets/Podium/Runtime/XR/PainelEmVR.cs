using System;
using Podium.Api;
using Podium.Audio;
using Podium.Session;
using Podium.Slides;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Podium.XR
{
    /// <summary>
    /// O painel da sessão dentro do headset: um quadro flutuante ao lado do púlpito, com
    /// botões que se aperta apontando o controle.
    /// <para>
    /// Existe porque o painel IMGUI da <see cref="SalaDeAulaController"/> não chega ao
    /// headset — o IMGUI desenha no buffer da tela, e dentro do VR isso não é lugar
    /// nenhum. O fluxo da sessão continua sendo do controlador; este componente só o
    /// mostra e o comanda, e é por isso que o controlador ganhou operações públicas em vez
    /// de este arquivo repetir as decisões dele.
    /// </para>
    /// <para>
    /// Interface em uGUI com a fonte embutida do Unity, e não TextMeshPro: a fonte legada
    /// vem com a engine e não exige importar nada. É o mesmo raciocínio que escolheu IMGUI
    /// na versão de PC — a cena precisa abrir e funcionar em qualquer máquina que clone o
    /// projeto.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(SalaDeAulaController))]
    public class PainelEmVR : MonoBehaviour
    {
        [Header("Dependências")]
        [SerializeField] private SalaDeAulaController controlador;
        [SerializeField] private PodiumSession session;
        [SerializeField] private PresentationRecorder recorder;
        [SerializeField] private ApresentacaoDeSlides slides;
        [SerializeField] private PodiumXRRig rig;

        [Header("Quadro")]
        [Tooltip("Posição do painel em relação a quem apresenta, em metros.")]
        [SerializeField] private Vector3 posicao = new Vector3(-0.85f, 1.35f, 0.75f);

        [SerializeField] private float larguraEmMetros = 1.1f;
        [SerializeField] private float alturaEmMetros = 0.78f;

        [Tooltip("Graus que o painel se vira para quem apresenta.")]
        [SerializeField] private float anguloDeVirada = 28f;

        // Pixels por metro. Alto o bastante para a letra não serrilhar a 80 cm dos olhos.
        private const float Densidade = 1200f;

        private static readonly Color CorDoFundo = new Color(0.08f, 0.09f, 0.11f, 0.94f);
        private static readonly Color CorDaBorda = new Color(0.35f, 0.42f, 0.52f, 1f);
        private static readonly Color CorDoTexto = new Color(0.90f, 0.92f, 0.95f, 1f);
        private static readonly Color CorDoDestaque = new Color(1f, 0.85f, 0.45f, 1f);
        private static readonly Color CorDoBotao = new Color(0.18f, 0.24f, 0.33f, 1f);

        private const int Botoes = 3;

        private Canvas canvas;
        private Text titulo;
        private Text corpo;
        private readonly Button[] botoes = new Button[Botoes];
        private readonly Text[] rotulos = new Text[Botoes];
        private readonly Action[] acoes = new Action[Botoes];

        private void Awake()
        {
            if (controlador == null)
            {
                controlador = GetComponent<SalaDeAulaController>();
            }
        }

        private void Start()
        {
            if (rig == null)
            {
                rig = FindAnyObjectByType<PodiumXRRig>();
            }

            // No PC o painel da tela já resolve, e um quadro flutuante no meio da sala só
            // atrapalharia a pré-visualização.
            if (rig == null || !rig.EmHeadset)
            {
                enabled = false;
                return;
            }

            if (controlador != null)
            {
                controlador.DesenharNaTela = false;
            }

            GarantirEventSystem();
            Construir();
            Redesenhar();
        }

        private void Update()
        {
            Redesenhar();
        }

        // ------------------------------------------------------------------ montagem

        /// <summary>
        /// O módulo de entrada do XR Interaction Toolkit é quem transforma o raio do
        /// controle em clique de botão. Criado aqui, e não deixado para a cena, porque uma
        /// cena sem ele mostra o painel e não responde a nada — falha muda.
        /// </summary>
        private static void GarantirEventSystem()
        {
            EventSystem existente = FindAnyObjectByType<EventSystem>();
            if (existente != null && existente.GetComponent<XRUIInputModule>() != null)
            {
                return;
            }

            GameObject go = existente != null ? existente.gameObject : new GameObject("EventSystem");
            if (existente == null)
            {
                go.AddComponent<EventSystem>();
            }

            BaseInputModule antigo = go.GetComponent<BaseInputModule>();
            if (antigo != null && !(antigo is XRUIInputModule))
            {
                Destroy(antigo);
            }

            go.AddComponent<XRUIInputModule>();
        }

        private void Construir()
        {
            Transform ancora = transform;
            Quaternion giro = Quaternion.Euler(0f, 180f + anguloDeVirada, 0f);

            GameObject go = new GameObject("Painel do Podium");
            go.transform.SetParent(ancora, false);
            go.transform.localPosition = posicao;
            go.transform.localRotation = giro;

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = rig != null ? rig.Olho : Camera.main;

            RectTransform quadro = canvas.GetComponent<RectTransform>();
            quadro.sizeDelta = new Vector2(larguraEmMetros * Densidade, alturaEmMetros * Densidade);
            quadro.localScale = Vector3.one / Densidade;

            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            go.AddComponent<GraphicRaycaster>();

            // É este componente que faz o raio do controle enxergar os botões.
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            Image fundo = go.AddComponent<Image>();
            fundo.color = CorDoFundo;

            Borda(quadro);

            float largura = quadro.sizeDelta.x;
            float altura = quadro.sizeDelta.y;
            float margem = 46f;
            float alturaDosBotoes = 150f;

            titulo = Texto(quadro, "Título", 58, FontStyle.Bold, CorDoDestaque,
                           new Vector2(margem, altura - margem - 74f),
                           new Vector2(largura - margem * 2f, 74f));
            titulo.alignment = TextAnchor.MiddleLeft;

            float alturaDoCorpo = altura - margem * 3f - 74f - alturaDosBotoes;
            corpo = Texto(quadro, "Corpo", 40, FontStyle.Normal, CorDoTexto,
                          new Vector2(margem, margem + alturaDosBotoes),
                          new Vector2(largura - margem * 2f, alturaDoCorpo));
            corpo.alignment = TextAnchor.UpperLeft;

            float larguraDoBotao = (largura - margem * 2f - 24f * (Botoes - 1)) / Botoes;
            for (int i = 0; i < Botoes; i++)
            {
                float x = margem + i * (larguraDoBotao + 24f);
                CriarBotao(quadro, i, new Vector2(x, margem),
                           new Vector2(larguraDoBotao, alturaDosBotoes - 20f));
            }
        }

        private static void Borda(RectTransform quadro)
        {
            GameObject go = new GameObject("Borda");
            go.transform.SetParent(quadro, false);

            RectTransform r = go.AddComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(-6f, -6f);
            r.offsetMax = new Vector2(6f, 6f);
            r.SetAsFirstSibling();

            Image imagem = go.AddComponent<Image>();
            imagem.color = CorDaBorda;
            imagem.raycastTarget = false;
        }

        private void CriarBotao(RectTransform pai, int indice, Vector2 posicao, Vector2 tamanho)
        {
            GameObject go = new GameObject("Botão " + (indice + 1));
            go.transform.SetParent(pai, false);

            RectTransform r = go.AddComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.zero;
            r.pivot = Vector2.zero;
            r.anchoredPosition = posicao;
            r.sizeDelta = tamanho;

            Image imagem = go.AddComponent<Image>();
            imagem.color = CorDoBotao;

            Button botao = go.AddComponent<Button>();
            botao.targetGraphic = imagem;

            // Realce forte de propósito: no headset o cursor é um raio fino, e sem a
            // mudança de cor não dá para saber o que está sob a mira antes de apertar.
            ColorBlock cores = botao.colors;
            cores.highlightedColor = new Color(0.32f, 0.46f, 0.62f, 1f);
            cores.pressedColor = new Color(0.55f, 0.70f, 0.85f, 1f);
            cores.selectedColor = cores.highlightedColor;
            cores.disabledColor = new Color(0.14f, 0.16f, 0.19f, 1f);
            botao.colors = cores;

            int capturado = indice;
            botao.onClick.AddListener(() =>
            {
                Action acao = acoes[capturado];
                if (acao != null)
                {
                    acao();
                }
            });

            Text rotulo = Texto(r, "Rótulo", 38, FontStyle.Bold, CorDoTexto,
                                Vector2.zero, tamanho);
            rotulo.alignment = TextAnchor.MiddleCenter;
            rotulo.raycastTarget = false;

            botoes[indice] = botao;
            rotulos[indice] = rotulo;
        }

        private static Text Texto(RectTransform pai, string nome, int tamanhoDaLetra,
                                  FontStyle estilo, Color cor, Vector2 posicao, Vector2 tamanho)
        {
            GameObject go = new GameObject(nome);
            go.transform.SetParent(pai, false);

            RectTransform r = go.AddComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.zero;
            r.pivot = Vector2.zero;
            r.anchoredPosition = posicao;
            r.sizeDelta = tamanho;

            Text texto = go.AddComponent<Text>();
            texto.font = FonteEmbutida();
            texto.fontSize = tamanhoDaLetra;
            texto.fontStyle = estilo;
            texto.color = cor;
            texto.horizontalOverflow = HorizontalWrapMode.Wrap;
            texto.verticalOverflow = VerticalWrapMode.Truncate;
            texto.supportRichText = false;

            return texto;
        }

        /// <summary>
        /// A fonte que vem dentro da engine. Sem ela seria preciso importar TextMeshPro
        /// Essentials ou carregar um .ttf — passo manual que quebraria a promessa de a cena
        /// abrir pronta em qualquer clone do projeto.
        /// </summary>
        public static Font FonteEmbutida()
        {
            Font fonte = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return fonte != null ? fonte : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        // -------------------------------------------------------------------- desenho

        private void Redesenhar()
        {
            if (canvas == null || controlador == null)
            {
                return;
            }

            for (int i = 0; i < Botoes; i++)
            {
                acoes[i] = null;
                botoes[i].gameObject.SetActive(false);
            }

            PodiumSessionState estado = session != null ? session.State : PodiumSessionState.Idle;

            switch (estado)
            {
                case PodiumSessionState.Idle:
                case PodiumSessionState.Failed:
                    DesenharLobby();
                    break;

                case PodiumSessionState.Initializing:
                    titulo.text = "Criando a sessão";
                    corpo.text = "Enviando a apresentação e extraindo o texto dos slides...";
                    break;

                case PodiumSessionState.Ready:
                    titulo.text = "A banca está sentada";
                    corpo.text = "Quando estiver pronto, comece a falar.\n\n" + LinhaDeSlides() +
                                 "\n" + controlador.DiagnosticoDaTranscricao;
                    Botao(0, "Começar", () => session.StartPresentation());
                    BotoesDeSlide(1);
                    break;

                case PodiumSessionState.Recording:
                    titulo.text = "Gravando  " + Tempo(recorder != null ? recorder.ElapsedSeconds : 0f);
                    corpo.text = "Fale olhando para a banca.\n\n" + LinhaDeSlides() +
                                 "\n" + controlador.DiagnosticoDaTranscricao;
                    Botao(0, "Encerrar", () => session.FinishPresentation());
                    BotoesDeSlide(1);
                    break;

                case PodiumSessionState.Uploading:
                    titulo.text = "Fechando a fala";
                    corpo.text = "Enviando o último trecho da apresentação...";
                    break;

                case PodiumSessionState.Analyzing:
                    titulo.text = "Analisando";
                    corpo.text = controlador.StatusDaAnalise + " · " +
                                 Mathf.RoundToInt(controlador.DecorridoDaAnalise) + " s\n\n" +
                                 (session.TranscricaoEnviada
                                     ? "A fala já foi transcrita aqui, então só falta a banca " +
                                       "formular as perguntas."
                                     : "A transcrição e as perguntas estão sendo geradas no " +
                                       "servidor. Não é travamento.");
                    break;

                case PodiumSessionState.Completed:
                    DesenharFeedback();
                    break;
            }
        }

        private void DesenharLobby()
        {
            titulo.text = controlador.Cenario != null ? controlador.Cenario.displayName : "Podium";

            string texto = "Banca: " + controlador.NomeDaPersona + "\n" +
                           controlador.DescricaoDaPersona + "\n\n" +
                           controlador.MensagemApi + "\n" + LinhaDeSlides();

            if (!string.IsNullOrEmpty(controlador.Erro))
            {
                texto = "Falha: " + controlador.Erro + "\n\n" + texto;
            }

            corpo.text = texto;

            Botao(0, "Trocar banca", () => controlador.TrocarPersona(1));
            Botao(1, "Conferir slides", controlador.ConferirSlides);
            Botao(2, "Criar sessão", controlador.CriarSessao, controlador.ApiOk);
        }

        private void DesenharFeedback()
        {
            FeedbackResponse f = controlador.Feedback;

            if (f == null || f.questions == null || f.questions.Length == 0)
            {
                titulo.text = "Feedback";
                corpo.text = f == null
                    ? "A sessão terminou, mas nenhum feedback chegou."
                    : "Nenhuma pergunta passou na verificação contra os slides.";
                Botao(0, "Nova sessão", controlador.NovaSessao);
                return;
            }

            int i = Mathf.Clamp(controlador.PerguntaSelecionada, 0, f.questions.Length - 1);
            GeneratedQuestion q = f.questions[i];

            titulo.text = "Pergunta " + (i + 1) + " de " + f.questions.Length;

            string texto = q.question + "\n\nSlide " + q.slide_origem + " — \"" +
                           q.trecho_literal + "\"";

            if (!string.IsNullOrEmpty(q.rationale))
            {
                texto += "\n\nPor quê: " + q.rationale;
            }

            corpo.text = texto;

            Botao(0, "Anterior", () => controlador.IrParaPergunta(i - 1), i > 0);
            Botao(1, "Próxima", () => controlador.IrParaPergunta(i + 1), i < f.questions.Length - 1);
            Botao(2, "Nova sessão", controlador.NovaSessao);
        }

        private void BotoesDeSlide(int primeiro)
        {
            if (slides == null || !slides.TemSlides)
            {
                return;
            }

            Botao(primeiro, "Slide <", slides.Anterior, slides.IndiceAtual > 0);
            Botao(primeiro + 1, "Slide >", slides.Proximo, slides.IndiceAtual < slides.Total - 1);
        }

        private string LinhaDeSlides()
        {
            if (slides == null)
            {
                return string.Empty;
            }

            if (!slides.TemSlides)
            {
                return "Slides: " + slides.Diagnostico;
            }

            return "Slide " + (slides.IndiceAtual + 1) + " de " + slides.Total + ".";
        }

        private void Botao(int indice, string rotulo, Action acao, bool ligado = true)
        {
            if (indice < 0 || indice >= Botoes)
            {
                return;
            }

            acoes[indice] = acao;
            rotulos[indice].text = rotulo;
            botoes[indice].interactable = ligado;
            botoes[indice].gameObject.SetActive(true);
        }

        private static string Tempo(float segundos)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(segundos));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
