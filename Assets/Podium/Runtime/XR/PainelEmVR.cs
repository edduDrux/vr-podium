using System;
using System.Globalization;
using Podium.Api;
using Podium.Audio;
using Podium.Scenarios;
using Podium.Session;
using Podium.Slides;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Podium.XR
{
    /// <summary>
    /// O painel da sessão dentro do headset: um quadro ao lado do púlpito, virado para quem
    /// apresenta, com botões que se aperta apontando o controle (gatilho) ou a mão (pinça).
    /// <para>
    /// Existe porque o painel IMGUI da <see cref="SalaDeAulaController"/> não chega ao
    /// headset — o IMGUI desenha no buffer da tela, e dentro do VR isso não é lugar
    /// nenhum. O fluxo da sessão continua sendo do controlador; este componente só o
    /// mostra e o comanda, e é por isso que o controlador ganhou operações públicas em vez
    /// de este arquivo repetir as decisões dele.
    /// </para>
    /// <para>
    /// Fica à DIREITA do púlpito, e não à esquerda: à esquerda está o monitor de retorno,
    /// mais perto dos olhos, e ele tapava a fileira de botões. Sai da frente da banca pelo
    /// mesmo motivo — é para ela que se apresenta. Se a pessoa se afastar do púlpito, o
    /// botão Menu do controle (ou a pinça segurada com a mão) traz o painel para perto.
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

        [Tooltip("Opcional. Com ele, a linha de ajuda diz se o headset está lendo os " +
                 "controles ou as mãos — e o que cada um faz.")]
        [SerializeField] private ComandosDoApresentador comandos;

        [Header("Quadro")]
        [Tooltip("Centro do painel, em metros, a partir do lugar de quem apresenta: x para a " +
                 "direita, y é a altura, z para a frente (para a banca).")]
        [SerializeField] private Vector3 lugarDoPainel = new Vector3(0.85f, 1.45f, 0.8f);

        [SerializeField] private float larguraEmMetros = 1.1f;
        [SerializeField] private float alturaEmMetros = 0.78f;

        [Tooltip("Altura dos olhos usada para virar o painel enquanto o headset ainda não " +
                 "informou onde está a cabeça.")]
        [SerializeField] private float alturaDosOlhos = 1.6f;

        [Tooltip("Graus de desvio entre o painel e a cabeça a partir dos quais ele se vira " +
                 "de novo para quem apresenta. Abaixo disso fica parado — um painel que " +
                 "acompanha cada movimento da cabeça é mais difícil de mirar.")]
        [SerializeField] private float toleranciaDeAngulo = 12f;

        // Pixels por metro. Alto o bastante para a letra não serrilhar a 1 m dos olhos.
        private const float Densidade = 1200f;

        // O texto muda no máximo a cada segundo (o cronômetro); redesenhar a 90 Hz só
        // produziria lixo de string para o coletor.
        private const float IntervaloDeDesenho = 0.1f;

        private const float VelocidadeDeGiro = 90f;
        private const int LimiteDaAnalise = 520;

        private static readonly Color CorDoFundo = new Color(0.08f, 0.09f, 0.11f, 0.96f);
        private static readonly Color CorDaBorda = new Color(0.35f, 0.42f, 0.52f, 1f);
        private static readonly Color CorDoTexto = new Color(0.90f, 0.92f, 0.95f, 1f);
        private static readonly Color CorDoTextoApagado = new Color(0.42f, 0.45f, 0.50f, 1f);
        private static readonly Color CorDaAjuda = new Color(0.62f, 0.68f, 0.76f, 1f);
        private static readonly Color CorDoDestaque = new Color(1f, 0.85f, 0.45f, 1f);
        private static readonly Color CorDoBotao = new Color(0.18f, 0.24f, 0.33f, 1f);

        private const int Botoes = 4;

        private Canvas canvas;
        private Transform quadro;
        private Text titulo;
        private Text corpo;
        private Text ajuda;
        private readonly Button[] botoes = new Button[Botoes];
        private readonly Text[] rotulos = new Text[Botoes];
        private readonly Action[] acoes = new Action[Botoes];
        private readonly bool[] visiveis = new bool[Botoes];

        private float proximoDesenho;
        private bool girando;

        private bool mostrandoResumo;
        private FeedbackResponse feedbackMostrado;
        private int perguntaMostrada = -1;

        // Delegados criados uma vez: um lambda por botão por quadro seria lixo de memória
        // constante, e o coletor de lixo é o que dá solavanco na imagem do headset.
        private Action acaoTrocarBanca;
        private Action acaoConferirSlides;
        private Action acaoVerificarApi;
        private Action acaoCriarSessao;
        private Action acaoComecar;
        private Action acaoEncerrar;
        private Action acaoSlideAnterior;
        private Action acaoProximoSlide;
        private Action acaoPerguntaAnterior;
        private Action acaoProximaPergunta;
        private Action acaoAlternarResumo;
        private Action acaoNovaSessao;

        /// <summary>O painel foi montado — ou seja, a sessão está sendo vista no headset.</summary>
        public bool Montado
        {
            get { return canvas != null; }
        }

        private void Awake()
        {
            if (controlador == null)
            {
                controlador = GetComponent<SalaDeAulaController>();
            }

            if (comandos == null)
            {
                comandos = GetComponent<ComandosDoApresentador>();
            }

            acaoTrocarBanca = TrocarBanca;
            acaoConferirSlides = ConferirSlides;
            acaoVerificarApi = VerificarApi;
            acaoCriarSessao = CriarSessao;
            acaoComecar = Comecar;
            acaoEncerrar = Encerrar;
            acaoSlideAnterior = SlideAnterior;
            acaoProximoSlide = ProximoSlide;
            acaoPerguntaAnterior = PerguntaAnterior;
            acaoProximaPergunta = ProximaPergunta;
            acaoAlternarResumo = AlternarResumo;
            acaoNovaSessao = NovaSessao;
        }

        private void Start()
        {
            if (rig == null)
            {
                rig = FindAnyObjectByType<PodiumXRRig>();
            }

            MontarSeHouverHeadset();
        }

        /// <summary>
        /// Enquanto não há painel, só pergunta ao rig se o headset chegou: ele pode ficar
        /// ativo alguns quadros depois do Play, e decidir de uma vez no <c>Start</c> deixava
        /// o headset sem painel nenhum. No PC a pergunta custa um bool por quadro.
        /// </summary>
        private void Update()
        {
            if (canvas == null)
            {
                MontarSeHouverHeadset();
                return;
            }

            if (Time.unscaledTime >= proximoDesenho)
            {
                proximoDesenho = Time.unscaledTime + IntervaloDeDesenho;
                Redesenhar();
            }
        }

        private void LateUpdate()
        {
            if (canvas != null)
            {
                Reorientar();
            }
        }

        private void MontarSeHouverHeadset()
        {
            // No PC o painel da tela já resolve, e um quadro flutuante no meio da sala só
            // atrapalharia a pré-visualização.
            if (canvas != null || rig == null || !rig.EmHeadset)
            {
                return;
            }

            if (controlador != null)
            {
                controlador.DesenharNaTela = false;
            }

            GarantirEventSystem();
            Construir();
            VoltarAoPulpito();
            Redesenhar();
        }

        // ---------------------------------------------------------------- posição

        /// <summary>Leva o painel ao lugar dele, ao lado do púlpito, virado para quem apresenta.</summary>
        public void VoltarAoPulpito()
        {
            if (quadro == null)
            {
                return;
            }

            Vector3 origem;
            Quaternion frente;
            Referencia(out origem, out frente);

            Vector3 lugar = origem + frente * lugarDoPainel;
            Vector3 olho = origem + Vector3.up * alturaDosOlhos;
            Posicionar(lugar, olho);
        }

        /// <summary>
        /// Traz o painel para junto de quem apresenta, na mesma posição relativa que ele tem
        /// no púlpito, contada a partir de onde a cabeça está e para onde ela está virada.
        /// De pé no púlpito olhando a banca, isso é o próprio lugar do painel.
        /// </summary>
        public void TrazerParaPerto()
        {
            Transform cabeca = Cabeca();
            if (quadro == null || cabeca == null || !CabecaRastreada(cabeca))
            {
                VoltarAoPulpito();
                return;
            }

            Vector3 olhar = cabeca.forward;
            olhar.y = 0f;
            if (olhar.sqrMagnitude < 0.0001f)
            {
                olhar = cabeca.up;
                olhar.y = 0f;
            }

            Quaternion giro = olhar.sqrMagnitude < 0.0001f
                ? Quaternion.identity
                : Quaternion.LookRotation(olhar.normalized, Vector3.up);

            Vector3 relativo = new Vector3(lugarDoPainel.x, lugarDoPainel.y - alturaDosOlhos,
                                           lugarDoPainel.z);
            Posicionar(cabeca.position + giro * relativo, cabeca.position);
        }

        /// <summary>
        /// Rotação que deixa a face do painel para quem está em <paramref name="olho"/>.
        /// <para>
        /// Um Canvas em world space é lido pelo lado de -Z: a frente dele (+Z) aponta para
        /// longe de quem olha, na direção do olhar — e o raio do XR Interaction Toolkit só o
        /// considera "de frente" quando a direção do raio e essa frente concordam. A versão
        /// anterior girava o painel 180° como se fosse o contrário, e ele ficava de costas e
        /// quase de perfil para quem apresentava: o texto espelhado, numa fatia estreita.
        /// </para>
        /// </summary>
        public static Quaternion VoltadoPara(Vector3 lugar, Vector3 olho)
        {
            Vector3 direcao = lugar - olho;
            if (direcao.sqrMagnitude < 0.000001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(direcao.normalized, Vector3.up);
        }

        private void Posicionar(Vector3 lugar, Vector3 olho)
        {
            quadro.SetPositionAndRotation(lugar, VoltadoPara(lugar, olho));
            girando = false;
        }

        /// <summary>
        /// O mesmo referencial da cenografia: o lugar e a direção em que o cenário põe quem
        /// apresenta. O painel acompanha a sala se o cenário mudar de lugar.
        /// </summary>
        private void Referencia(out Vector3 origem, out Quaternion frente)
        {
            ScenarioDefinition cenario = controlador != null ? controlador.Cenario : null;
            origem = cenario != null ? cenario.spawnPosition : Vector3.zero;
            frente = Quaternion.Euler(0f, cenario != null ? cenario.spawnYaw : 0f, 0f);
        }

        private Transform Cabeca()
        {
            Camera olho = rig != null ? rig.Olho : null;
            return olho != null ? olho.transform : null;
        }

        /// <summary>
        /// Antes de o headset informar a cabeça, ela está no chão da origem: virar o painel
        /// para lá o deitaria, e trazê-lo para perto o poria no piso.
        /// </summary>
        private bool CabecaRastreada(Transform cabeca)
        {
            Vector3 origem;
            Quaternion frente;
            Referencia(out origem, out frente);
            return cabeca.position.y - origem.y >= 0.5f;
        }

        /// <summary>
        /// Vira o painel de novo para a cabeça quando ela se afasta demais do ponto para o
        /// qual ele foi virado — a pessoa raramente está exatamente no ponto de nascimento.
        /// </summary>
        private void Reorientar()
        {
            Transform cabeca = Cabeca();
            if (cabeca == null || !CabecaRastreada(cabeca))
            {
                return;
            }

            Quaternion alvo = VoltadoPara(quadro.position, cabeca.position);

            if (!girando && Quaternion.Angle(quadro.rotation, alvo) > toleranciaDeAngulo)
            {
                girando = true;
            }

            if (!girando)
            {
                return;
            }

            quadro.rotation = Quaternion.RotateTowards(quadro.rotation, alvo,
                                                       VelocidadeDeGiro * Time.unscaledDeltaTime);

            if (Quaternion.Angle(quadro.rotation, alvo) < 0.5f)
            {
                girando = false;
            }
        }

        // ------------------------------------------------------------------ montagem

        /// <summary>
        /// O módulo de entrada do XR Interaction Toolkit é quem transforma o raio do
        /// controle — ou da mão — em clique de botão. Criado aqui, e não deixado para a cena,
        /// porque uma cena sem ele mostra o painel e não responde a nada: falha muda.
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
            GameObject go = new GameObject("Painel do Podium");
            go.transform.SetParent(transform, false);

            canvas = go.AddComponent<Canvas>();

            // Só depois do Canvas: ele troca o Transform do objeto por um RectTransform, e
            // uma referência guardada antes apontaria para o componente destruído — o
            // painel ficaria na origem, no chão, sem que nada acusasse erro.
            quadro = canvas.transform;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = rig != null ? rig.Olho : Camera.main;

            RectTransform area = canvas.GetComponent<RectTransform>();
            area.sizeDelta = new Vector2(larguraEmMetros * Densidade, alturaEmMetros * Densidade);
            area.localScale = Vector3.one / Densidade;

            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            go.AddComponent<GraphicRaycaster>();

            // É este componente que faz o raio do controle e o da mão enxergarem os botões.
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            // A borda antes do fundo: filho desenha por cima do pai, e uma borda desenhada
            // depois cobria o painel inteiro com a cor dela.
            Retangulo(area, "Borda", CorDaBorda, 6f, false);

            // O fundo recebe o raio: apontar para o painel, mesmo fora de um botão, conta
            // como apontar para a interface — é assim que a pinça longa sabe se afastar.
            Retangulo(area, "Fundo", CorDoFundo, 0f, true);

            float largura = area.sizeDelta.x;
            float altura = area.sizeDelta.y;
            const float margem = 44f;
            const float alturaDosBotoes = 128f;
            const float alturaDaAjuda = 50f;
            const float alturaDoTitulo = 72f;

            titulo = Texto(area, "Título", 54, FontStyle.Bold, CorDoDestaque,
                           new Vector2(margem, altura - margem - alturaDoTitulo),
                           new Vector2(largura - margem * 2f, alturaDoTitulo));
            titulo.alignment = TextAnchor.MiddleLeft;

            float baseDoCorpo = margem + alturaDosBotoes + alturaDaAjuda + 12f;
            float alturaDoCorpo = altura - margem - alturaDoTitulo - 16f - baseDoCorpo;
            corpo = Texto(area, "Corpo", 40, FontStyle.Normal, CorDoTexto,
                          new Vector2(margem, baseDoCorpo),
                          new Vector2(largura - margem * 2f, alturaDoCorpo));
            corpo.alignment = TextAnchor.UpperLeft;
            AjustarAoEspaco(corpo, 24, 40);

            ajuda = Texto(area, "Ajuda", 30, FontStyle.Italic, CorDaAjuda,
                          new Vector2(margem, margem + alturaDosBotoes),
                          new Vector2(largura - margem * 2f, alturaDaAjuda));
            ajuda.alignment = TextAnchor.MiddleLeft;
            AjustarAoEspaco(ajuda, 22, 30);

            const float vao = 20f;
            float larguraDoBotao = (largura - margem * 2f - vao * (Botoes - 1)) / Botoes;
            for (int i = 0; i < Botoes; i++)
            {
                float x = margem + i * (larguraDoBotao + vao);
                CriarBotao(area, i, new Vector2(x, margem),
                           new Vector2(larguraDoBotao, alturaDosBotoes - 16f));
            }
        }

        private static void Retangulo(RectTransform pai, string nome, Color cor, float sobra,
                                      bool recebeRaio)
        {
            GameObject go = new GameObject(nome);
            go.transform.SetParent(pai, false);

            RectTransform r = go.AddComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(-sobra, -sobra);
            r.offsetMax = new Vector2(sobra, sobra);

            Image imagem = go.AddComponent<Image>();
            imagem.color = cor;
            imagem.raycastTarget = recebeRaio;
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

                    // Mostra já o efeito do clique, sem esperar o próximo ciclo de desenho.
                    proximoDesenho = 0f;
                }
            });

            Text rotulo = Texto(r, "Rótulo", 36, FontStyle.Bold, CorDoTexto,
                                new Vector2(8f, 0f), new Vector2(tamanho.x - 16f, tamanho.y));
            rotulo.alignment = TextAnchor.MiddleCenter;
            rotulo.raycastTarget = false;
            AjustarAoEspaco(rotulo, 22, 36);

            botoes[indice] = botao;
            rotulos[indice] = rotulo;
            go.SetActive(false);
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
            texto.raycastTarget = false;

            return texto;
        }

        /// <summary>
        /// Encolhe a letra quando o texto não cabe, em vez de cortar o fim. Uma pergunta
        /// longa com âncora e porquê passava da área do corpo e perdia justamente o porquê.
        /// </summary>
        private static void AjustarAoEspaco(Text texto, int menor, int maior)
        {
            texto.resizeTextForBestFit = true;
            texto.resizeTextMinSize = menor;
            texto.resizeTextMaxSize = maior;
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

        /// <summary>
        /// Decide o que cada botão mostra e só toca no que mudou. A versão anterior desligava
        /// e religava todos os botões a cada quadro, e desligar um botão apaga o estado de
        /// mira dele: o realce nunca aparecia sob o raio, e parecia que o painel não
        /// reconhecia o controle.
        /// </summary>
        private void Redesenhar()
        {
            if (canvas == null || controlador == null)
            {
                return;
            }

            for (int i = 0; i < Botoes; i++)
            {
                acoes[i] = null;
                visiveis[i] = false;
            }

            PodiumSessionState estado = session != null ? session.State : PodiumSessionState.Idle;

            if (controlador.NoLobby)
            {
                DesenharLobby();
            }
            else
            {
                switch (estado)
                {
                    case PodiumSessionState.Initializing:
                        titulo.text = "Criando a sessão";
                        corpo.text = "Enviando a apresentação e extraindo o texto dos slides...";
                        break;

                    case PodiumSessionState.Ready:
                        titulo.text = "A banca está sentada";
                        corpo.text = "Quando estiver pronto, aperte Começar e fale para a banca.\n\n" +
                                     LinhaDeSlides() + "\n" + controlador.DiagnosticoDaTranscricao;
                        Botao(0, "Começar", acaoComecar);
                        BotoesDeSlide(1);
                        break;

                    case PodiumSessionState.Recording:
                        titulo.text = "Gravando  " + Tempo(recorder != null ? recorder.ElapsedSeconds : 0f) +
                                      (recorder != null ? " / " + Tempo(recorder.MaxSeconds) : string.Empty);
                        corpo.text = "Fale olhando para a banca.\n\n" + LinhaDeSlides() +
                                     "\n" + controlador.DiagnosticoDaTranscricao;
                        Botao(0, "Encerrar", acaoEncerrar);
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
                                     (session != null && session.TranscricaoEnviada
                                         ? "A fala já foi transcrita aqui, então só falta a banca " +
                                           "formular as perguntas."
                                         : "A transcrição e as perguntas estão sendo geradas no " +
                                           "servidor. Uma fala de 10 min leva cerca de 3 min. " +
                                           "Não é travamento.");
                        break;

                    case PodiumSessionState.Completed:
                        DesenharFeedback();
                        break;
                }
            }

            for (int i = 0; i < Botoes; i++)
            {
                if (botoes[i].gameObject.activeSelf != visiveis[i])
                {
                    botoes[i].gameObject.SetActive(visiveis[i]);
                }
            }

            ajuda.text = LinhaDeAjuda();
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

            Botao(0, "Trocar banca", acaoTrocarBanca);
            Botao(1, "Conferir slides", acaoConferirSlides);

            // Sem isto, uma API que estava fora do ar na abertura deixava "Criar sessão"
            // apagado para sempre dentro do headset — o único jeito de tentar de novo era o
            // painel do PC, que no headset não aparece.
            Botao(2, controlador.VerificandoApi ? "Verificando..." : "Verificar API",
                  acaoVerificarApi, !controlador.VerificandoApi);
            Botao(3, "Criar sessão", acaoCriarSessao, controlador.ApiOk);
        }

        private void DesenharFeedback()
        {
            FeedbackResponse f = controlador.Feedback;

            // Feedback novo abre sempre na pergunta, e trocar de pergunta — pelo botão ou
            // pelo A/B do controle — sai do resumo: ninguém troca de pergunta para não vê-la.
            if (f != feedbackMostrado || controlador.PerguntaSelecionada != perguntaMostrada)
            {
                mostrandoResumo = false;
                feedbackMostrado = f;
                perguntaMostrada = controlador.PerguntaSelecionada;
            }

            if (f == null)
            {
                titulo.text = "Feedback";
                corpo.text = "A sessão terminou, mas nenhum feedback chegou.";
                Botao(3, "Nova sessão", acaoNovaSessao);
                return;
            }

            bool semPerguntas = f.questions == null || f.questions.Length == 0;

            if (mostrandoResumo || semPerguntas)
            {
                titulo.text = "Resumo da sessão";
                corpo.text = (semPerguntas
                                 ? "Nenhuma pergunta passou na verificação contra os slides.\n\n"
                                 : string.Empty) +
                             TextoDoResumo(f);

                if (!semPerguntas)
                {
                    Botao(2, "Perguntas", acaoAlternarResumo);
                }

                Botao(3, "Nova sessão", acaoNovaSessao);
                return;
            }

            int i = Mathf.Clamp(controlador.PerguntaSelecionada, 0, f.questions.Length - 1);
            GeneratedQuestion q = f.questions[i];

            titulo.text = "Pergunta " + (i + 1) + " de " + f.questions.Length;

            // A âncora fica junto da pergunta, nunca escondida: é o que distingue banca
            // ancorada no material de alucinação.
            string texto = q.question + "\n\nSlide " + q.slide_origem + " — \"" +
                           q.trecho_literal + "\"";

            if (!string.IsNullOrEmpty(q.rationale))
            {
                texto += "\n\nPor quê: " + q.rationale;
            }

            corpo.text = texto;

            Botao(0, "Anterior", acaoPerguntaAnterior, i > 0);
            Botao(1, "Próxima", acaoProximaPergunta, i < f.questions.Length - 1);
            Botao(2, "Resumo", acaoAlternarResumo);
            Botao(3, "Nova sessão", acaoNovaSessao);
        }

        /// <summary>
        /// O que o painel do PC mostra depois das perguntas — forma, cobertura e quem
        /// transcreveu —, em texto corrido. No headset não havia como ver nada disso.
        /// </summary>
        public static string TextoDoResumo(FeedbackResponse f)
        {
            if (f == null)
            {
                return string.Empty;
            }

            string texto = f.perguntas_aprovadas + " de " + f.perguntas_geradas +
                           " perguntas aprovadas (aterramento " + Pct(f.taxa_aterramento) + ").";

            if (f.metrics != null)
            {
                texto += "\nForma: " +
                         f.metrics.words_per_minute.ToString("0", CultureInfo.InvariantCulture) +
                         " palavras/min · " + f.metrics.pause_count + " pausas · maior pausa " +
                         f.metrics.longest_pause_seconds.ToString("0.0", CultureInfo.InvariantCulture) +
                         " s · fala de " + Tempo(f.metrics.duration_seconds) + ".";
            }

            if (f.coverageNaoMedida)
            {
                texto += "\nCobertura dos slides: não medida nesta sessão.";
            }
            else if (f.slide_coverage != null)
            {
                texto += "\nCobertura dos slides: " + Pct(f.slide_coverage.percentual_coberto) +
                         " do material apresentado" +
                         (f.slide_coverage.alerta_descolamento
                             ? " — ALERTA: a fala se descolou dos slides."
                             : ".");
            }

            if (!string.IsNullOrEmpty(f.transcript_source))
            {
                texto += "\nTranscrição por " + f.transcript_source + ".";
            }

            if (f.slides_truncados || f.transcricao_truncada)
            {
                texto += "\nAviso: contexto truncado na análise.";
            }

            if (!string.IsNullOrEmpty(f.content_analysis))
            {
                string analise = f.content_analysis.Trim();
                if (analise.Length > LimiteDaAnalise)
                {
                    analise = analise.Substring(0, LimiteDaAnalise).TrimEnd() +
                              "… (íntegra no painel do PC)";
                }

                texto += "\n\n" + analise;
            }

            return texto;
        }

        private void BotoesDeSlide(int primeiro)
        {
            if (slides == null || !slides.TemSlides)
            {
                return;
            }

            Botao(primeiro, "Slide anterior", acaoSlideAnterior, slides.IndiceAtual > 0);
            Botao(primeiro + 1, "Próximo slide", acaoProximoSlide,
                  slides.IndiceAtual < slides.Total - 1);
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

        /// <summary>
        /// Diz com o que o headset está lendo a pessoa e o que cada gesto faz. Quando nada é
        /// reconhecido, diz isso — antes, controle sem perfil de interação era um raio que
        /// simplesmente não aparecia, sem pista nenhuma do porquê.
        /// </summary>
        private string LinhaDeAjuda()
        {
            ModoDeEntrada modo = comandos != null ? comandos.Modo : ModoDeEntrada.Controles;

            // Curtas de propósito: a linha encolhe a letra para caber, e frase comprida vira
            // texto miúdo a um metro dos olhos.
            switch (modo)
            {
                case ModoDeEntrada.Controles:
                    return "Controle: gatilho aperta · A/X avança · B/Y volta · Menu traz o painel";

                case ModoDeEntrada.Maos:
                    return "Mãos: pinça aperta · pinça segurada fora do painel traz o painel";

                default:
                    return "Nada reconhecido: pegue um controle ou mostre as mãos";
            }
        }

        private void Botao(int indice, string rotulo, Action acao, bool ligado = true)
        {
            if (indice < 0 || indice >= Botoes)
            {
                return;
            }

            acoes[indice] = acao;
            visiveis[indice] = true;
            rotulos[indice].text = rotulo;

            // O fundo do botão apagado quase some no painel escuro; é o rótulo esmaecido que
            // diz "ainda não" — sem ele, "Criar sessão" apagado parecia um botão ativo preto.
            rotulos[indice].color = ligado ? CorDoTexto : CorDoTextoApagado;
            botoes[indice].interactable = ligado;
        }

        // ------------------------------------------------------------------- ações

        private void TrocarBanca()
        {
            controlador.TrocarPersona(1);
        }

        private void ConferirSlides()
        {
            controlador.ConferirSlides();
        }

        private void VerificarApi()
        {
            controlador.VerificarApi();
        }

        private void CriarSessao()
        {
            controlador.CriarSessao();
        }

        private void Comecar()
        {
            if (session != null)
            {
                session.StartPresentation();
            }
        }

        private void Encerrar()
        {
            if (session != null)
            {
                session.FinishPresentation();
            }
        }

        private void SlideAnterior()
        {
            if (slides != null)
            {
                slides.Anterior();
            }
        }

        private void ProximoSlide()
        {
            if (slides != null)
            {
                slides.Proximo();
            }
        }

        private void PerguntaAnterior()
        {
            controlador.IrParaPergunta(controlador.PerguntaSelecionada - 1);
        }

        private void ProximaPergunta()
        {
            controlador.IrParaPergunta(controlador.PerguntaSelecionada + 1);
        }

        private void AlternarResumo()
        {
            mostrandoResumo = !mostrandoResumo;
        }

        private void NovaSessao()
        {
            mostrandoResumo = false;
            controlador.NovaSessao();
        }

        // --------------------------------------------------------------- formatação

        private static string Tempo(float segundos)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(segundos));
            return (total / 60) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Pct(float fracao)
        {
            return (fracao * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
