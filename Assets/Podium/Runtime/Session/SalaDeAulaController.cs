using System.Collections;
using System.Globalization;
using Podium.Api;
using Podium.Audio;
using Podium.Scenarios;
using Podium.Slides;
using UnityEngine;

namespace Podium.Session
{
    /// <summary>
    /// O fluxo da Sala de Aula — escolher a banca, criar a sessão, apresentar, navegar o
    /// Feedback Duplo — e o painel IMGUI que o mostra no PC.
    /// <para>
    /// IMGUI, e não Canvas/TextMeshPro, porque não exige pacote, fonte nem prefab: a cena
    /// abre e funciona em qualquer máquina que abra o projeto. Dentro do headset ele não
    /// aparece (o IMGUI desenha no buffer da tela), e quem mostra a mesma sessão é o
    /// <c>PainelEmVR</c>.
    /// </para>
    /// <para>
    /// As operações são públicas por causa disso: duas telas, um fluxo só. Repetir aqui e
    /// lá as decisões de quando criar sessão ou trocar de pergunta seria garantir que uma
    /// das duas ficasse para trás.
    /// </para>
    /// </summary>
    public class SalaDeAulaController : MonoBehaviour
    {
        [Header("Dependências")]
        [SerializeField] private PodiumSession session;
        [SerializeField] private PodiumApiClient api;
        [SerializeField] private PresentationRecorder recorder;
        [SerializeField] private ApresentacaoDeSlides slides;

        [Header("Conteúdo")]
        [SerializeField] private ScenarioDefinition scenario;
        [SerializeField] private PersonaDefinition[] personas;

        [Tooltip("PDF ou PPTx enviado no /init. No Editor, caminho absoluto no PC.")]
        [SerializeField] private string presentationPath =
            "C:/Users/eduar/projetos/podium/storage/_fixtures/tcc_slides.pdf";

        private int personaSelecionada;
        private string[] nomesPersonas = new string[0];

        private string mensagemApi = "API ainda não verificada.";
        private bool apiOk;
        private bool verificandoApi;

        private string statusAnalise = "";
        private float decorridoAnalise;

        private FeedbackResponse feedback;
        private int perguntaSelecionada;
        private bool verTodasAsPerguntas;
        private string erro;
        private bool voltarAoLobby;
        private bool atalhosDosSlides = true;
        private Vector2 scroll;

        private GUIStyle titulo;
        private GUIStyle texto;
        private GUIStyle destaque;
        private bool estilosProntos;

        /// <summary>
        /// Desenhar o painel IMGUI na tela. O <c>PainelEmVR</c> desliga isto ao assumir:
        /// dentro do headset o IMGUI não aparece, e continuar desenhando só gastaria quadro.
        /// </summary>
        public bool DesenharNaTela { get; set; } = true;

        public ScenarioDefinition Cenario
        {
            get { return scenario; }
        }

        public string MensagemApi
        {
            get { return mensagemApi; }
        }

        public bool ApiOk
        {
            get { return apiOk && !verificandoApi && session != null; }
        }

        public string StatusDaAnalise
        {
            get { return statusAnalise; }
        }

        public float DecorridoDaAnalise
        {
            get { return decorridoAnalise; }
        }

        public string Erro
        {
            get { return erro; }
        }

        public FeedbackResponse Feedback
        {
            get { return feedback; }
        }

        public int PerguntaSelecionada
        {
            get { return perguntaSelecionada; }
        }

        /// <summary>A verificação da API está em andamento.</summary>
        public bool VerificandoApi
        {
            get { return verificandoApi; }
        }

        /// <summary>
        /// A tela que está no ar é o lobby: antes da sessão, depois de uma falha ou depois de
        /// "Nova sessão". Exposto porque, sem isto, o painel do headset olhava só o estado da
        /// sessão — que continua Completed depois de "Nova sessão" — e o botão não fazia nada.
        /// </summary>
        public bool NoLobby
        {
            get
            {
                if (session == null)
                {
                    return true;
                }

                PodiumSessionState estado = session.State;
                return estado == PodiumSessionState.Idle ||
                       estado == PodiumSessionState.Failed ||
                       (estado == PodiumSessionState.Completed && voltarAoLobby);
            }
        }

        /// <summary>
        /// O navegador de perguntas está no ar: é quando os comandos de avançar e voltar
        /// andam pelas perguntas, e não pelos slides.
        /// </summary>
        public bool NoNavegadorDePerguntas
        {
            get { return NavegandoPerguntas(); }
        }

        public string NomeDaPersona
        {
            get
            {
                PersonaDefinition p = PersonaAtual();
                return p != null ? p.displayName : "(sem persona)";
            }
        }

        public string DescricaoDaPersona
        {
            get
            {
                PersonaDefinition p = PersonaAtual();
                return p != null ? p.description : "Nenhuma persona atribuída no Inspector.";
            }
        }

        /// <summary>Quem transcreve esta sessão, em uma frase.</summary>
        public string DiagnosticoDaTranscricao
        {
            get { return session != null ? session.DiagnosticoDaTranscricao : string.Empty; }
        }

        private PersonaDefinition PersonaAtual()
        {
            if (personas == null || personas.Length == 0)
            {
                return null;
            }

            return personas[Mathf.Clamp(personaSelecionada, 0, personas.Length - 1)];
        }

        /// <summary>Anda pela lista de personas, dando a volta no fim.</summary>
        public void TrocarPersona(int passo)
        {
            if (personas == null || personas.Length == 0)
            {
                return;
            }

            personaSelecionada =
                ((personaSelecionada + passo) % personas.Length + personas.Length) % personas.Length;
            AplicarSelecao();
        }

        /// <summary>Procura as imagens dos slides do arquivo apontado, sem abrir sessão.</summary>
        public void ConferirSlides()
        {
            CarregarSlides();
        }

        /// <summary>Cria a sessão no backend com a persona e o arquivo escolhidos.</summary>
        public void CriarSessao()
        {
            if (session == null)
            {
                return;
            }

            erro = null;
            feedback = null;
            AplicarSelecao();
            CarregarSlides();
            session.BeginSession(presentationPath);
        }

        /// <summary>Volta ao lobby depois do feedback, para apresentar de novo.</summary>
        public void NovaSessao()
        {
            voltarAoLobby = true;
        }

        /// <summary>Abre uma pergunta pelo índice e leva a tela da sala ao slide dela.</summary>
        public void IrParaPergunta(int indice)
        {
            verTodasAsPerguntas = false;
            SelecionarPergunta(indice);
        }

        private void OnEnable()
        {
            if (session != null)
            {
                session.StateChanged += AoMudarEstado;
                session.AnalysisProgress += AoProgredir;
                session.FeedbackReady += AoReceberFeedback;
                session.SessionFailed += AoFalhar;
            }
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.StateChanged -= AoMudarEstado;
                session.AnalysisProgress -= AoProgredir;
                session.FeedbackReady -= AoReceberFeedback;
                session.SessionFailed -= AoFalhar;
            }
        }

        private void Start()
        {
            int n = personas == null ? 0 : personas.Length;
            nomesPersonas = new string[n];
            for (int i = 0; i < n; i++)
            {
                nomesPersonas[i] = personas[i] != null ? personas[i].displayName : "(vazio)";
            }

            AplicarSelecao();

            if (slides != null)
            {
                atalhosDosSlides = slides.AtalhosDeTeclado;
            }

            // Verificar já na abertura: o erro mais comum é o backend fora do ar, e ele
            // precisa aparecer antes de o usuário escolher persona e apertar "Criar".
            VerificarApi();
        }

        private void AplicarSelecao()
        {
            if (session == null)
            {
                return;
            }

            session.Scenario = scenario;

            if (personas != null && personas.Length > 0)
            {
                personaSelecionada = Mathf.Clamp(personaSelecionada, 0, personas.Length - 1);
                session.Persona = personas[personaSelecionada];
            }
        }

        /// <summary>
        /// A tela da sala usa a exportação em imagens do mesmo arquivo que sobe no /init:
        /// um caminho só, para que material analisado e material projetado não divirjam.
        /// </summary>
        private void CarregarSlides()
        {
            if (slides != null)
            {
                slides.Carregar(presentationPath);
            }
        }

        public void VerificarApi()
        {
            if (api == null || verificandoApi)
            {
                return;
            }

            verificandoApi = true;
            apiOk = false;
            mensagemApi = "Verificando " +
                          (api.Config != null ? api.Config.ApiRoot : "(config vazio)") + "...";

            StartCoroutine(api.CheckHealth(r =>
            {
                verificandoApi = false;
                apiOk = r.Ok;
                mensagemApi = r.Ok
                    ? "API no ar (banco: " + r.Value.database + ")."
                    : "API fora do ar: " + r.Message +
                      "\nNo PC: abra o Docker Desktop e rode 'docker compose up -d' na pasta podium.";
            }));
        }

        private void AoMudarEstado(PodiumSessionState novo)
        {
            if (novo != PodiumSessionState.Completed)
            {
                voltarAoLobby = false;
            }

            if (novo == PodiumSessionState.Analyzing)
            {
                statusAnalise = SessionStatus.Processing;
                decorridoAnalise = 0f;
            }

            // A sessão existe a partir daqui, e com ela a chance de pedir as páginas ao
            // backend — o único caminho que funciona no headset, onde não há PowerPoint
            // nem Docker para converter o arquivo em imagens.
            if (novo == PodiumSessionState.Ready)
            {
                StartCoroutine(GarantirSlidesDaSessao());
            }
        }

        /// <summary>
        /// Se a conversão local não produziu nada, projeta as páginas que o backend desenha.
        /// A ordem importa: a conversão local é preferida por não depender da rede a cada
        /// troca de slide, e só quando ela não existe é que a parede vem pelo Wi-Fi.
        /// </summary>
        private IEnumerator GarantirSlidesDaSessao()
        {
            if (slides == null || api == null || session == null || slides.TemSlides)
            {
                yield break;
            }

            yield return slides.CarregarDoServidor(api, session.SessionId);
        }

        private void AoProgredir(string status, float decorrido)
        {
            statusAnalise = status;
            decorridoAnalise = decorrido;
        }

        private void AoReceberFeedback(FeedbackResponse f)
        {
            feedback = f;
            scroll = Vector2.zero;
            verTodasAsPerguntas = false;
            SelecionarPergunta(0);
        }

        private void Update()
        {
            bool navegando = NavegandoPerguntas();

            // As setas servem aos slides durante a fala e às perguntas depois dela; quem
            // manda é a tela que está no ar. Se o teclado já estava desligado no Inspector,
            // continua desligado.
            if (slides != null)
            {
                slides.AtalhosDeTeclado = atalhosDosSlides && !navegando;
            }

            if (!navegando)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.PageDown))
            {
                SelecionarPergunta(perguntaSelecionada + 1);
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.PageUp))
            {
                SelecionarPergunta(perguntaSelecionada - 1);
            }
        }

        private void AoFalhar(PodiumErrorKind kind, string mensagem)
        {
            erro = mensagem;
        }

        private void OnGUI()
        {
            if (!DesenharNaTela)
            {
                return;
            }

            PrepararEstilos();

            float largura = Mathf.Min(640f, Screen.width - 40f);
            GUILayout.BeginArea(new Rect(20f, 20f, largura, Screen.height - 40f), GUI.skin.box);

            GUILayout.Label(scenario != null ? scenario.displayName : "Podium", titulo);
            if (scenario != null)
            {
                GUILayout.Label(scenario.description, texto);
            }
            GUILayout.Space(8f);

            PodiumSessionState estado = session != null ? session.State : PodiumSessionState.Idle;

            switch (estado)
            {
                case PodiumSessionState.Idle:
                case PodiumSessionState.Failed:
                    DesenharLobby(estado);
                    break;

                case PodiumSessionState.Initializing:
                    GUILayout.Label("Criando a sessão e extraindo o texto dos slides...", texto);
                    break;

                case PodiumSessionState.Ready:
                    GUILayout.Label("Sessão criada. A banca está sentada; quando estiver pronto, " +
                                    "comece a falar.", texto);
                    if (GUILayout.Button("Começar apresentação", GUILayout.Height(40f)))
                    {
                        session.StartPresentation();
                    }
                    DesenharSlides();
                    break;

                case PodiumSessionState.Recording:
                    DesenharGravacao();
                    break;

                case PodiumSessionState.Uploading:
                    GUILayout.Label("Enviando o último trecho da fala...", texto);
                    break;

                case PodiumSessionState.Analyzing:
                    GUILayout.Label("Analisando: " + statusAnalise + " · " +
                                    Segundos(decorridoAnalise) + " s", destaque);
                    GUILayout.Label("Uma fala de 10 min leva cerca de 3 min (transcrição + geração " +
                                    "das perguntas). Não é travamento.", texto);
                    break;

                case PodiumSessionState.Completed:
                    if (voltarAoLobby)
                    {
                        DesenharLobby(estado);
                    }
                    else
                    {
                        DesenharFeedback();
                    }
                    break;
            }

            GUILayout.EndArea();
        }

        private void DesenharLobby(PodiumSessionState estado)
        {
            if (estado == PodiumSessionState.Failed && !string.IsNullOrEmpty(erro))
            {
                GUILayout.Label("Falha: " + erro, destaque);
                GUILayout.Space(8f);
            }

            GUILayout.Label("Banca", titulo);
            if (nomesPersonas.Length > 0)
            {
                int novo = GUILayout.SelectionGrid(personaSelecionada, nomesPersonas,
                                                   nomesPersonas.Length, GUILayout.Height(32f));
                if (novo != personaSelecionada)
                {
                    personaSelecionada = novo;
                    AplicarSelecao();
                }

                PersonaDefinition p = personas[personaSelecionada];
                if (p != null)
                {
                    GUILayout.Label(p.description + "\n(" + p.apiValue + ")", texto);
                }
            }
            else
            {
                GUILayout.Label("Nenhuma persona atribuída no Inspector.", destaque);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Apresentação (PDF ou PPTx)", titulo);
            presentationPath = GUILayout.TextField(presentationPath);

            if (slides != null)
            {
                // Conferir antes de criar a sessão: descobrir que a pasta de imagens não
                // existe já com a banca montada custa uma sessão inteira.
                if (GUILayout.Button("Conferir slides", GUILayout.Height(26f)))
                {
                    CarregarSlides();
                }

                GUILayout.Label(slides.Diagnostico, slides.TemSlides ? texto : destaque);
            }

            GUILayout.Space(8f);
            GUILayout.Label(mensagemApi, apiOk ? texto : destaque);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Verificar API", GUILayout.Height(32f)))
            {
                VerificarApi();
            }

            GUI.enabled = ApiOk;
            if (GUILayout.Button("Criar sessão", GUILayout.Height(32f)))
            {
                CriarSessao();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DesenharGravacao()
        {
            float t = recorder != null ? recorder.ElapsedSeconds : 0f;
            float max = recorder != null ? recorder.MaxSeconds : 0f;
            int chunks = recorder != null ? recorder.ChunksEmitted : 0;

            GUILayout.Label("Gravando: " + Tempo(t) + " / " + Tempo(max) +
                            " · trechos enviados: " + chunks, destaque);
            GUILayout.Label("Fale olhando para a banca. Ao encerrar, o último trecho sobe e a " +
                            "análise começa sozinha.", texto);

            if (GUILayout.Button("Encerrar apresentação", GUILayout.Height(40f)))
            {
                session.FinishPresentation();
            }

            DesenharSlides();
        }

        /// <summary>
        /// Comandos de slide por clique, além do teclado. No headset este painel não aparece:
        /// quem faz esse papel é o <c>PainelEmVR</c>, e os botões do controle e a pinça da
        /// mão (ver <c>ComandosDoApresentador</c>).
        /// </summary>
        private void DesenharSlides()
        {
            if (slides == null)
            {
                return;
            }

            GUILayout.Space(8f);

            if (!slides.TemSlides)
            {
                GUILayout.Label("Slides: " + slides.Diagnostico, texto);
                return;
            }

            GUILayout.BeginHorizontal();

            GUI.enabled = slides.IndiceAtual > 0;
            if (GUILayout.Button("Slide anterior", GUILayout.Height(32f)))
            {
                slides.Anterior();
            }

            GUI.enabled = slides.IndiceAtual < slides.Total - 1;
            if (GUILayout.Button("Próximo slide", GUILayout.Height(32f)))
            {
                slides.Proximo();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label("Slide " + (slides.IndiceAtual + 1) + " de " + slides.Total +
                            " · teclado: setas, PageUp/PageDown ou espaço.", texto);
        }

        private void DesenharFeedback()
        {
            FeedbackResponse f = feedback;
            if (f == null)
            {
                GUILayout.Label("Sessão concluída, mas nenhum feedback foi recebido.", destaque);
            }
            else
            {
                scroll = GUILayout.BeginScrollView(scroll);

                GUILayout.Label("Perguntas da banca — " + f.perguntas_aprovadas + " aprovadas de " +
                                f.perguntas_geradas + " geradas (aterramento " +
                                Pct(f.taxa_aterramento) + ")", titulo);

                if (f.questions == null || f.questions.Length == 0)
                {
                    GUILayout.Label("Nenhuma pergunta passou na verificação contra os slides.", texto);
                }
                else if (verTodasAsPerguntas)
                {
                    DesenharListaDePerguntas(f);
                }
                else
                {
                    DesenharNavegadorDePerguntas(f);
                }

                if (!string.IsNullOrEmpty(f.content_analysis))
                {
                    GUILayout.Label("Análise de conteúdo", titulo);
                    GUILayout.Label(f.content_analysis, texto);
                }

                if (f.metrics != null)
                {
                    GUILayout.Label("Forma", titulo);
                    GUILayout.Label(Segundos(f.metrics.words_per_minute) + " palavras/min · " +
                                    f.metrics.pause_count + " pausas · maior pausa " +
                                    Decimal1(f.metrics.longest_pause_seconds) + " s · fala de " +
                                    Tempo(f.metrics.duration_seconds), texto);
                }

                GUILayout.Label("Cobertura dos slides", titulo);
                if (f.coverageNaoMedida)
                {
                    GUILayout.Label("Não medida nesta sessão.", texto);
                }
                else if (f.slide_coverage != null)
                {
                    GUILayout.Label(Pct(f.slide_coverage.percentual_coberto) + " do material foi " +
                                    "apresentado" +
                                    (f.slide_coverage.alerta_descolamento
                                        ? " — ALERTA: a fala se descolou dos slides."
                                        : "."),
                                    f.slide_coverage.alerta_descolamento ? destaque : texto);
                }

                if (!string.IsNullOrEmpty(f.transcript_source))
                {
                    // Quem transcreveu muda a acurácia esperada do texto, e com ela a
                    // leitura das perguntas: o relatório compara as duas fontes.
                    GUILayout.Label("Transcrição por " + f.transcript_source, texto);
                }

                if (f.slides_truncados || f.transcricao_truncada)
                {
                    GUILayout.Label("Aviso: contexto truncado (slides: " + f.slides_truncados +
                                    ", transcrição: " + f.transcricao_truncada + ").", destaque);
                }

                GUILayout.EndScrollView();
            }

            if (GUILayout.Button("Nova sessão", GUILayout.Height(36f)))
            {
                voltarAoLobby = true;
            }
        }

        /// <summary>
        /// Uma pergunta por vez, com a âncora embaixo e a tela da sala no slide de origem.
        /// Ler as perguntas em bloco é o modo de conferir; ensaiar a defesa é responder uma,
        /// olhando para o slide que a gerou, e só então ver a seguinte.
        /// </summary>
        private void DesenharNavegadorDePerguntas(FeedbackResponse f)
        {
            perguntaSelecionada = Mathf.Clamp(perguntaSelecionada, 0, f.questions.Length - 1);
            GeneratedQuestion q = f.questions[perguntaSelecionada];

            GUILayout.BeginHorizontal();

            GUI.enabled = perguntaSelecionada > 0;
            if (GUILayout.Button("Pergunta anterior", GUILayout.Height(32f)))
            {
                SelecionarPergunta(perguntaSelecionada - 1);
            }

            GUI.enabled = perguntaSelecionada < f.questions.Length - 1;
            if (GUILayout.Button("Próxima pergunta", GUILayout.Height(32f)))
            {
                SelecionarPergunta(perguntaSelecionada + 1);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label("Pergunta " + (perguntaSelecionada + 1) + " de " + f.questions.Length +
                            " · teclado: setas ou PageUp/PageDown.", texto);
            GUILayout.Space(4f);

            GUILayout.Label(q.question, destaque);

            // A âncora fica junto da pergunta, nunca escondida: é o que distingue banca
            // ancorada no material de alucinação.
            GUILayout.Label("Slide " + q.slide_origem + " — \"" + q.trecho_literal + "\"", texto);

            if (!string.IsNullOrEmpty(q.rationale))
            {
                GUILayout.Label("Por quê: " + q.rationale, texto);
            }

            if (slides != null && slides.TemSlides)
            {
                GUILayout.Label("A tela da sala está no slide " + (slides.IndiceAtual + 1) + ".", texto);
            }

            GUILayout.Space(4f);
            if (GUILayout.Button("Ver todas as perguntas", GUILayout.Height(28f)))
            {
                verTodasAsPerguntas = true;
            }
        }

        private void DesenharListaDePerguntas(FeedbackResponse f)
        {
            for (int i = 0; i < f.questions.Length; i++)
            {
                GeneratedQuestion q = f.questions[i];
                GUILayout.Label((i + 1) + ". " + q.question, destaque);
                GUILayout.Label("Slide " + q.slide_origem + " — \"" + q.trecho_literal + "\"", texto);
                if (!string.IsNullOrEmpty(q.rationale))
                {
                    GUILayout.Label("Por quê: " + q.rationale, texto);
                }

                if (GUILayout.Button("Abrir esta pergunta", GUILayout.Height(24f)))
                {
                    IrParaPergunta(i);
                }

                GUILayout.Space(6f);
            }
        }

        private void SelecionarPergunta(int indice)
        {
            if (feedback == null || feedback.questions == null || feedback.questions.Length == 0)
            {
                return;
            }

            perguntaSelecionada = Mathf.Clamp(indice, 0, feedback.questions.Length - 1);
            LevarTelaAoSlideDaPergunta();
        }

        /// <summary>
        /// O slide_origem é a numeração do arquivo que a banca leu, e as imagens da parede
        /// são a exportação desse mesmo arquivo: o número 7 do backend é o sétimo PNG. Se a
        /// pasta de imagens for de outro material, este pulo mostra isso na hora.
        /// </summary>
        private void LevarTelaAoSlideDaPergunta()
        {
            if (slides == null || !slides.TemSlides || feedback == null || feedback.questions == null)
            {
                return;
            }

            if (perguntaSelecionada < 0 || perguntaSelecionada >= feedback.questions.Length)
            {
                return;
            }

            int numero = feedback.questions[perguntaSelecionada].slide_origem;
            if (numero >= 1)
            {
                slides.Ir(numero - 1);
            }
        }

        /// <summary>Verdadeiro enquanto o painel está com o navegador de perguntas no ar.</summary>
        private bool NavegandoPerguntas()
        {
            return !voltarAoLobby &&
                   !verTodasAsPerguntas &&
                   session != null &&
                   session.State == PodiumSessionState.Completed &&
                   feedback != null &&
                   feedback.questions != null &&
                   feedback.questions.Length > 0;
        }

        private void PrepararEstilos()
        {
            if (estilosProntos)
            {
                return;
            }

            titulo = new GUIStyle(GUI.skin.label);
            titulo.fontSize = 18;
            titulo.fontStyle = FontStyle.Bold;
            titulo.wordWrap = true;

            texto = new GUIStyle(GUI.skin.label);
            texto.fontSize = 14;
            texto.wordWrap = true;

            destaque = new GUIStyle(texto);
            destaque.fontStyle = FontStyle.Bold;

            GUI.skin.button.fontSize = 14;
            GUI.skin.textField.fontSize = 14;

            estilosProntos = true;
        }

        private static string Tempo(float segundos)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(segundos));
            return (total / 60) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Segundos(float valor)
        {
            return valor.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Decimal1(float valor)
        {
            return valor.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string Pct(float fracao)
        {
            return (fracao * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
