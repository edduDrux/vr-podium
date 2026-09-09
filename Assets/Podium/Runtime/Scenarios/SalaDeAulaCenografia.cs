using System.Collections.Generic;
using Podium.Slides;
using UnityEngine;

namespace Podium.Scenarios
{
    /// <summary>
    /// Monta a sala com primitivos em tempo de execução: piso, paredes, teto, quadro,
    /// púlpito, mesa da banca, um lugar por persona (no assento que a
    /// <see cref="PersonaDefinition.seatIndex"/> indica) e a plateia de mesas e cadeiras
    /// atrás da banca.
    /// <para>
    /// Primitivos criados por código, e não salvos na cena, para que o material seja
    /// sempre o padrão do pipeline ativo (URP): uma cena com material referenciado à mão
    /// fica rosa quando o pipeline muda. Quando existirem os avatares e a sala modelada,
    /// este script sai; a regra dos assentos fica.
    /// </para>
    /// </summary>
    public class SalaDeAulaCenografia : MonoBehaviour
    {
        [SerializeField] private ScenarioDefinition scenario;
        [SerializeField] private PersonaDefinition[] personas;

        [Header("Projeção")]
        [Tooltip("Quem carrega as imagens dos slides. Sem isto, a tela fica apagada.")]
        [SerializeField] private ApresentacaoDeSlides slides;

        [SerializeField] private float larguraDaTela = 3.2f;
        [SerializeField] private float alturaDaTela = 1.8f;

        [Tooltip("Altura do centro da tela na parede da frente.")]
        [SerializeField] private float alturaDaTelaNaParede = 1.9f;

        [Tooltip("Monitor de retorno no púlpito: o mesmo slide, virado para quem apresenta.")]
        [SerializeField] private bool comMonitorDeRetorno = true;

        [Header("Banca")]
        [Tooltip("Distância, em metros, do apresentador até a mesa da banca.")]
        [SerializeField] private float distanciaBanca = 4f;

        [SerializeField] private float espacamentoAssentos = 1.2f;

        [Header("Plateia")]
        [SerializeField] private int colunasDeMesas = 4;
        [SerializeField] private int fileirasDeMesas = 4;
        [SerializeField] private float espacamentoColunas = 1.7f;
        [SerializeField] private float espacamentoFileiras = 1.5f;

        [Tooltip("Distância da mesa da banca até a primeira fileira de mesas.")]
        [SerializeField] private float recuoPrimeiraFileira = 2.2f;

        [Tooltip("Folga extra entre as metades esquerda e direita da plateia.")]
        [SerializeField] private float larguraCorredor = 0.9f;

        [Header("Sala")]
        [SerializeField] private float alturaTeto = 3.2f;

        [Tooltip("Com teto a sala fecha; sem teto a luz direcional da cena entra direto.")]
        [SerializeField] private bool comTeto = true;

        [Tooltip("Altura da câmera no Editor. No headset, quem manda é o tracking.")]
        [SerializeField] private float alturaOlhos = 1.6f;

        private static readonly Color[] CoresPersonas =
        {
            new Color(0.55f, 0.20f, 0.20f),
            new Color(0.20f, 0.45f, 0.30f),
            new Color(0.20f, 0.30f, 0.55f),
        };

        private static readonly Color CorPiso = new Color(0.58f, 0.54f, 0.49f);
        private static readonly Color CorParede = new Color(0.85f, 0.84f, 0.79f);
        private static readonly Color CorTeto = new Color(0.93f, 0.93f, 0.91f);
        private static readonly Color CorMadeira = new Color(0.48f, 0.33f, 0.20f);
        private static readonly Color CorMadeiraClara = new Color(0.72f, 0.56f, 0.36f);
        private static readonly Color CorMetal = new Color(0.30f, 0.31f, 0.34f);
        private static readonly Color CorQuadro = new Color(0.94f, 0.95f, 0.93f);
        private static readonly Color CorVidro = new Color(0.52f, 0.71f, 0.84f);
        private static readonly Color CorLuminaria = new Color(0.98f, 0.97f, 0.88f);
        private static readonly Color CorAssento = new Color(0.22f, 0.33f, 0.46f);
        private static readonly Color CorCaderno = new Color(0.88f, 0.87f, 0.82f);

        // Medidas repetidas por toda a cenografia.
        private const float EspessuraParede = 0.2f;
        private const float EspessuraTampo = 0.06f;
        private const float AlturaMesa = 0.74f;
        private const float AlturaAssento = 0.45f;

        // Um material por cor, compartilhado: sem isto cada primitivo instancia o seu.
        private readonly Dictionary<Color, Material> materiais = new Dictionary<Color, Material>();
        private Shader shaderPadrao;

        private void Start()
        {
            Montar(false);
        }

        /// <summary>
        /// Monta a sala. Tudo em espaço local: apresentador na origem, olhando para +Z.
        /// <para>
        /// Com <paramref name="previa"/>, o Editor chama isto fora do Play para que a sala
        /// seja visível na Scene view: aí nada é salvo na cena (<c>HideFlags.DontSave</c>) e
        /// a câmera fica onde está. Sem isso, quem abre a cena vê três objetos e conclui que
        /// falta montar o cenário — a sala existe, mas só nasce ao dar Play.
        /// </para>
        /// </summary>
        public void Montar(bool previa)
        {
            Limpar();

            Vector3 origem = scenario != null ? scenario.spawnPosition : Vector3.zero;
            float yaw = scenario != null ? scenario.spawnYaw : 0f;
            Quaternion frente = Quaternion.Euler(0f, yaw, 0f);

            Transform sala = new GameObject("Sala").transform;
            sala.SetParent(transform, false);
            sala.SetPositionAndRotation(origem, frente);

            float meiaLargura = MeiaLarguraDaSala();
            float zFrente = -2.6f;
            float zFundo = ZDaFileira(Mathf.Max(fileirasDeMesas - 1, 0)) + 1.8f;

            // As telas ficam fora da "Sala" porque mudam de escala a cada slide, para
            // acompanhar a proporção da imagem — e batch estático congela transform.
            Transform telas = new GameObject("Telas").transform;
            telas.SetParent(transform, false);
            telas.SetPositionAndRotation(origem, frente);

            MontarCasca(sala, meiaLargura, zFrente, zFundo);
            MontarFrente(sala, telas, meiaLargura, zFrente);
            MontarLaterais(sala, meiaLargura, zFrente, zFundo);
            MontarPulpito(sala, telas);
            MontarPlateia(sala);

            Transform banca = new GameObject("Banca").transform;
            banca.SetParent(transform, false);
            banca.SetPositionAndRotation(origem, frente);
            MontarBanca(sala, banca);

            // Só a cenografia entra no batch: os avatares da banca podem se mover.
            StaticBatchingUtility.Combine(sala.gameObject);

            if (previa)
            {
                DescartarAoSalvar();
                return;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = origem + Vector3.up * alturaOlhos;
                cam.transform.rotation = frente;
            }
        }

        /// <summary>Derruba a sala montada. Serve ao Play e à pré-visualização do Editor.</summary>
        public void Limpar()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Apagar(transform.GetChild(i).gameObject);
            }

            foreach (Material m in materiais.Values)
            {
                Apagar(m);
            }

            materiais.Clear();
        }

        /// <summary>
        /// A sala da pré-visualização não é conteúdo da cena: é desenho descartável. Sem a
        /// marca, um Ctrl+S depois de pré-visualizar gravaria centenas de primitivos no
        /// arquivo da cena, e o próximo Play montaria tudo de novo por cima.
        /// </summary>
        private void DescartarAoSalvar()
        {
            foreach (Transform filho in transform.GetComponentsInChildren<Transform>(true))
            {
                if (filho != transform)
                {
                    filho.gameObject.hideFlags = HideFlags.DontSave;
                }
            }

            foreach (Material m in materiais.Values)
            {
                m.hideFlags = HideFlags.DontSave;
            }
        }

        private void OnDestroy()
        {
            foreach (Material m in materiais.Values)
            {
                Apagar(m);
            }

            materiais.Clear();
        }

        /// <summary>Destroy não existe fora do Play; DestroyImmediate não pode ser usado dentro dele.</summary>
        private static void Apagar(UnityEngine.Object alvo)
        {
            if (alvo == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(alvo);
            }
            else
            {
                DestroyImmediate(alvo);
            }
        }

        // ----------------------------------------------------------------- sala

        private float MeiaLarguraDaSala()
        {
            float bordaDasMesas = XDaColuna(Mathf.Max(colunasDeMesas - 1, 0)) + 0.6f;
            float bordaDaBanca = LarguraDaBanca() * 0.5f;
            return Mathf.Max(4f, Mathf.Max(bordaDasMesas, bordaDaBanca) + 1.3f);
        }

        private float LarguraDaBanca()
        {
            int n = personas == null ? 0 : personas.Length;
            return Mathf.Max(2.4f, n * espacamentoAssentos + 1.2f);
        }

        // As colunas se dividem em duas metades, com um corredor no meio da sala.
        private float XDaColuna(int coluna)
        {
            float x = (coluna - (colunasDeMesas - 1) * 0.5f) * espacamentoColunas;
            float meioCorredor = larguraCorredor * 0.5f;
            return x + (coluna < colunasDeMesas * 0.5f ? -meioCorredor : meioCorredor);
        }

        private float ZDaFileira(int fileira)
        {
            return distanciaBanca + recuoPrimeiraFileira + fileira * espacamentoFileiras;
        }

        private void MontarCasca(Transform sala, float meiaLargura, float zFrente, float zFundo)
        {
            float comprimento = zFundo - zFrente;
            float centroZ = (zFundo + zFrente) * 0.5f;
            float largura = meiaLargura * 2f;

            // O Plane nasce com 10 x 10 metros.
            Criar(PrimitiveType.Plane, "Piso", new Vector3(0f, 0f, centroZ),
                  new Vector3(largura / 10f, 1f, comprimento / 10f), sala, CorPiso);

            // Paredes de cubo, e não de plano, para terem face visível vista de dentro.
            float meiaEspessura = EspessuraParede * 0.5f;
            Criar(PrimitiveType.Cube, "Parede da frente",
                  new Vector3(0f, alturaTeto * 0.5f, zFrente - meiaEspessura),
                  new Vector3(largura + EspessuraParede, alturaTeto, EspessuraParede), sala, CorParede);
            Criar(PrimitiveType.Cube, "Parede do fundo",
                  new Vector3(0f, alturaTeto * 0.5f, zFundo + meiaEspessura),
                  new Vector3(largura + EspessuraParede, alturaTeto, EspessuraParede), sala, CorParede);
            Criar(PrimitiveType.Cube, "Parede esquerda",
                  new Vector3(-meiaLargura - meiaEspessura, alturaTeto * 0.5f, centroZ),
                  new Vector3(EspessuraParede, alturaTeto, comprimento), sala, CorParede);
            Criar(PrimitiveType.Cube, "Parede direita",
                  new Vector3(meiaLargura + meiaEspessura, alturaTeto * 0.5f, centroZ),
                  new Vector3(EspessuraParede, alturaTeto, comprimento), sala, CorParede);

            if (comTeto)
            {
                Criar(PrimitiveType.Cube, "Teto", new Vector3(0f, alturaTeto + 0.08f, centroZ),
                      new Vector3(largura, 0.15f, comprimento), sala, CorTeto);
            }

            MontarLuminarias(sala, zFrente, zFundo);
        }

        // Com o teto fechado a luz direcional da cena não entra: a sala traz a sua.
        private void MontarLuminarias(Transform sala, float zFrente, float zFundo)
        {
            const int quantidade = 3;
            for (int i = 0; i < quantidade; i++)
            {
                float t = (i + 1f) / (quantidade + 1f);
                float z = Mathf.Lerp(zFrente, zFundo, t);

                GameObject painel = Criar(PrimitiveType.Cube, "Luminária " + (i + 1),
                                          new Vector3(0f, alturaTeto - 0.05f, z),
                                          new Vector3(1.6f, 0.08f, 0.45f), sala, CorLuminaria);
                SemColisor(painel);

                Light luz = new GameObject("Luz " + (i + 1)).AddComponent<Light>();
                luz.transform.SetParent(sala, false);
                luz.transform.localPosition = new Vector3(0f, alturaTeto - 0.4f, z);
                luz.type = LightType.Point;
                luz.color = CorLuminaria;
                luz.intensity = 1.6f;
                luz.range = 12f;
                luz.shadows = LightShadows.None;
            }
        }

        // Parede que o apresentador tem às costas: quadro, tela de projeção, calha e relógio.
        private void MontarFrente(Transform sala, Transform telas, float meiaLargura, float zFrente)
        {
            float larguraQuadro = Mathf.Min(5f, meiaLargura * 2f - 1.6f);
            float zQuadro = zFrente + 0.06f;

            Criar(PrimitiveType.Cube, "Moldura do quadro", new Vector3(0f, 1.65f, zQuadro),
                  new Vector3(larguraQuadro + 0.12f, 1.42f, 0.06f), sala, CorMetal);
            Criar(PrimitiveType.Cube, "Quadro", new Vector3(0f, 1.65f, zQuadro + 0.04f),
                  new Vector3(larguraQuadro, 1.3f, 0.04f), sala, CorQuadro);
            Criar(PrimitiveType.Cube, "Calha do quadro", new Vector3(0f, 0.92f, zQuadro + 0.06f),
                  new Vector3(larguraQuadro + 0.12f, 0.06f, 0.14f), sala, CorMetal);

            GameObject relogio = Criar(PrimitiveType.Cylinder, "Relógio",
                                       new Vector3(larguraQuadro * 0.5f + 0.55f, 2.55f, zQuadro),
                                       Quaternion.Euler(90f, 0f, 0f),
                                       new Vector3(0.34f, 0.03f, 0.34f), sala, CorQuadro);
            SemColisor(relogio);

            // Tela de projeção à frente do quadro, como a de sala de aula que desce por cima
            // dele. A moldura fica no tamanho cheio mesmo quando o slide é mais estreito:
            // é ela que dá a borda escura de projetor.
            float zTela = zQuadro + 0.08f;
            GameObject moldura = Criar(PrimitiveType.Cube, "Moldura da tela",
                                       new Vector3(0f, alturaDaTelaNaParede, zTela),
                                       new Vector3(larguraDaTela + 0.14f, alturaDaTela + 0.14f, 0.05f),
                                       sala, CorMetal);
            SemColisor(moldura);

            // Girada 180°: o Quad tem a face visível apontando para o próprio -Z, e quem
            // assiste está do lado oposto da parede.
            CriarTela(telas, "Tela de projeção",
                      new Vector3(0f, alturaDaTelaNaParede, zTela + 0.04f),
                      Quaternion.Euler(0f, 180f, 0f), larguraDaTela, alturaDaTela);
        }

        // Janelas de um lado, porta do outro.
        private void MontarLaterais(Transform sala, float meiaLargura, float zFrente, float zFundo)
        {
            const int janelas = 3;
            float primeira = zFrente + 2.2f;
            float ultima = zFundo - 1.6f;

            for (int i = 0; i < janelas; i++)
            {
                float t = janelas == 1 ? 0.5f : i / (janelas - 1f);
                float z = Mathf.Lerp(primeira, ultima, t);

                GameObject moldura = Criar(PrimitiveType.Cube, "Moldura da janela " + (i + 1),
                                           new Vector3(-meiaLargura + 0.03f, 1.7f, z),
                                           new Vector3(0.06f, 1.4f, 1.5f), sala, CorMetal);
                GameObject vidro = Criar(PrimitiveType.Cube, "Janela " + (i + 1),
                                         new Vector3(-meiaLargura + 0.08f, 1.7f, z),
                                         new Vector3(0.04f, 1.26f, 1.36f), sala, CorVidro);
                SemColisor(moldura);
                SemColisor(vidro);
            }

            float zPorta = zFundo - 1.4f;
            Criar(PrimitiveType.Cube, "Batente da porta",
                  new Vector3(meiaLargura - 0.03f, 1.08f, zPorta),
                  new Vector3(0.06f, 2.16f, 1.1f), sala, CorMadeira);
            Criar(PrimitiveType.Cube, "Porta", new Vector3(meiaLargura - 0.08f, 1.05f, zPorta),
                  new Vector3(0.06f, 2.05f, 0.95f), sala, CorMadeiraClara);
            GameObject macaneta = Criar(PrimitiveType.Sphere, "Maçaneta",
                                        new Vector3(meiaLargura - 0.14f, 1f, zPorta + 0.36f),
                                        new Vector3(0.08f, 0.08f, 0.08f), sala, CorMetal);
            SemColisor(macaneta);
        }

        private void MontarPulpito(Transform sala, Transform telas)
        {
            Vector3 posicaoDoPulpito = new Vector3(0f, 0f, 0.65f);
            Transform pulpito = Filho(sala, "Púlpito", posicaoDoPulpito);

            Criar(PrimitiveType.Cube, "Corpo", new Vector3(0f, 0.55f, 0f),
                  new Vector3(0.62f, 1.1f, 0.42f), pulpito, CorMadeira);
            Criar(PrimitiveType.Cube, "Tampo", new Vector3(0f, 1.13f, 0f),
                  new Vector3(0.76f, 0.06f, 0.54f), pulpito, CorMadeiraClara);
            GameObject anotacoes = Criar(PrimitiveType.Cube, "Anotações",
                                         new Vector3(0.22f, 1.17f, 0.02f),
                                         new Vector3(0.24f, 0.02f, 0.32f), pulpito, CorQuadro);
            SemColisor(anotacoes);

            if (!comMonitorDeRetorno)
            {
                return;
            }

            // O monitor repete o slide virado para quem apresenta: a tela grande está às
            // costas dele, e virar para trás para saber em que slide está quebra a
            // apresentação — no ensaio e na defesa.
            GameObject pe = Criar(PrimitiveType.Cube, "Pé do monitor",
                                  new Vector3(-0.14f, 1.21f, -0.06f),
                                  new Vector3(0.05f, 0.1f, 0.05f), pulpito, CorMetal);
            SemColisor(pe);

            const float larguraDoMonitor = 0.38f;
            const float alturaDoMonitor = 0.22f;

            GameObject molduraDoMonitor = Criar(PrimitiveType.Cube, "Moldura do monitor",
                                                new Vector3(-0.14f, 1.37f, -0.05f),
                                                Quaternion.Euler(14f, 0f, 0f),
                                                new Vector3(larguraDoMonitor + 0.03f,
                                                            alturaDoMonitor + 0.03f, 0.02f),
                                                pulpito, CorMetal);
            SemColisor(molduraDoMonitor);

            // Sem giro em Y: a face visível do Quad já aponta para -Z, que é onde está quem
            // apresenta. A inclinação é a de um monitor de mesa.
            CriarTela(telas, "Monitor de retorno",
                      posicaoDoPulpito + new Vector3(-0.14f, 1.37f, -0.065f),
                      Quaternion.Euler(14f, 0f, 0f), larguraDoMonitor, alturaDoMonitor);
        }

        // -------------------------------------------------------------- pessoas

        private void MontarBanca(Transform sala, Transform banca)
        {
            MontarMesa(sala, "Mesa da banca", new Vector3(0f, 0f, distanciaBanca),
                       LarguraDaBanca(), 0.95f);

            int n = personas == null ? 0 : personas.Length;
            for (int i = 0; i < n; i++)
            {
                PersonaDefinition p = personas[i];
                if (p == null)
                {
                    continue;
                }

                float x = (p.seatIndex - (n - 1) * 0.5f) * espacamentoAssentos;
                Vector3 lugar = new Vector3(x, 0f, distanciaBanca + 0.85f);
                // A banca olha para o apresentador, logo fica virada 180° em relação a ele.
                Quaternion olhandoParaFrente = Quaternion.Euler(0f, 180f, 0f);

                MontarCadeira(sala, "Cadeira da banca " + (i + 1), lugar);

                GameObject membro;
                if (p.avatarPrefab != null)
                {
                    membro = Instantiate(p.avatarPrefab, banca);
                    membro.transform.localPosition = lugar;
                    membro.transform.localRotation = olhandoParaFrente;
                }
                else
                {
                    // Sem avatar, a cápsula fica encolhida e apoiada no assento: de pé ela
                    // atravessaria a cadeira e a mesa.
                    membro = Criar(PrimitiveType.Capsule, p.displayName,
                                   lugar + Vector3.up * (AlturaAssento + 0.6f),
                                   olhandoParaFrente, new Vector3(0.9f, 0.62f, 0.9f), banca,
                                   CoresPersonas[i % CoresPersonas.Length]);
                }

                membro.name = p.displayName + " (" + p.apiValue + ")";
            }
        }

        private void MontarPlateia(Transform sala)
        {
            Transform plateia = Filho(sala, "Plateia", Vector3.zero);

            for (int fileira = 0; fileira < fileirasDeMesas; fileira++)
            {
                float z = ZDaFileira(fileira);
                for (int coluna = 0; coluna < colunasDeMesas; coluna++)
                {
                    float x = XDaColuna(coluna);
                    string sufixo = " F" + (fileira + 1) + "C" + (coluna + 1);

                    MontarMesa(plateia, "Mesa" + sufixo, new Vector3(x, 0f, z), 1.15f, 0.6f);
                    MontarCadeira(plateia, "Cadeira" + sufixo, new Vector3(x, 0f, z + 0.62f));
                }
            }
        }

        // --------------------------------------------------------------- móveis

        /// <summary>Mesa vista de frente pelo apresentador: painel em -Z, quem senta em +Z.</summary>
        private void MontarMesa(Transform pai, string nome, Vector3 centro, float largura,
                                float profundidade)
        {
            Transform mesa = Filho(pai, nome, centro);

            Criar(PrimitiveType.Cube, "Tampo", new Vector3(0f, AlturaMesa, 0f),
                  new Vector3(largura, EspessuraTampo, profundidade), mesa, CorMadeiraClara);
            Criar(PrimitiveType.Cube, "Painel",
                  new Vector3(0f, AlturaMesa * 0.6f, -profundidade * 0.5f + 0.04f),
                  new Vector3(largura * 0.94f, AlturaMesa * 0.62f, 0.04f), mesa, CorMadeiraClara);

            float xLateral = largura * 0.5f - 0.04f;
            Criar(PrimitiveType.Cube, "Lateral esquerda", new Vector3(-xLateral, AlturaMesa * 0.5f, 0f),
                  new Vector3(0.05f, AlturaMesa, profundidade * 0.86f), mesa, CorMetal);
            Criar(PrimitiveType.Cube, "Lateral direita", new Vector3(xLateral, AlturaMesa * 0.5f, 0f),
                  new Vector3(0.05f, AlturaMesa, profundidade * 0.86f), mesa, CorMetal);

            GameObject caderno = Criar(PrimitiveType.Cube, "Caderno",
                                       new Vector3(largura * 0.18f, AlturaMesa + 0.05f, 0.02f),
                                       new Vector3(0.24f, 0.03f, 0.32f), mesa, CorCaderno);
            SemColisor(caderno);
        }

        /// <summary>Cadeira de quem assiste: encosto em +Z, olhando para o apresentador.</summary>
        private void MontarCadeira(Transform pai, string nome, Vector3 centro)
        {
            Transform cadeira = Filho(pai, nome, centro);

            Criar(PrimitiveType.Cube, "Assento", new Vector3(0f, AlturaAssento, 0f),
                  new Vector3(0.46f, 0.06f, 0.44f), cadeira, CorAssento);
            Criar(PrimitiveType.Cube, "Encosto", new Vector3(0f, AlturaAssento + 0.3f, 0.2f),
                  new Vector3(0.46f, 0.5f, 0.06f), cadeira, CorAssento);
            Criar(PrimitiveType.Cube, "Pé esquerdo", new Vector3(-0.2f, AlturaAssento * 0.5f, 0f),
                  new Vector3(0.04f, AlturaAssento, 0.42f), cadeira, CorMetal);
            Criar(PrimitiveType.Cube, "Pé direito", new Vector3(0.2f, AlturaAssento * 0.5f, 0f),
                  new Vector3(0.04f, AlturaAssento, 0.42f), cadeira, CorMetal);
        }

        // ----------------------------------------------------------- auxiliares

        private static Transform Filho(Transform pai, string nome, Vector3 posicaoLocal)
        {
            Transform t = new GameObject(nome).transform;
            t.SetParent(pai, false);
            t.localPosition = posicaoLocal;
            return t;
        }

        /// <summary>
        /// Superfície que mostra o slide atual. Nasce apagada e só acende quando a
        /// <see cref="ApresentacaoDeSlides"/> encontra as imagens.
        /// </summary>
        private void CriarTela(Transform pai, string nome, Vector3 posicao, Quaternion rotacao,
                               float largura, float altura)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = nome;
            go.transform.SetParent(pai, false);
            go.transform.localPosition = posicao;
            go.transform.localRotation = rotacao;
            go.transform.localScale = new Vector3(largura, altura, 1f);
            SemColisor(go);

            TelaDeSlides tela = go.AddComponent<TelaDeSlides>();
            tela.Conectar(slides, largura, altura);
        }

        private GameObject Criar(PrimitiveType tipo, string nome, Vector3 posicao,
                                 Vector3 escala, Transform pai, Color cor)
        {
            return Criar(tipo, nome, posicao, Quaternion.identity, escala, pai, cor);
        }

        private GameObject Criar(PrimitiveType tipo, string nome, Vector3 posicao,
                                 Quaternion rotacao, Vector3 escala, Transform pai, Color cor)
        {
            GameObject go = GameObject.CreatePrimitive(tipo);
            go.name = nome;
            go.transform.SetParent(pai, false);
            go.transform.localPosition = posicao;
            go.transform.localRotation = rotacao;
            go.transform.localScale = escala;

            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = MaterialDe(cor, r);
            }

            return go;
        }

        private Material MaterialDe(Color cor, Renderer referencia)
        {
            if (materiais.TryGetValue(cor, out Material existente))
            {
                return existente;
            }

            // O shader vem do próprio primitivo: é o padrão do pipeline ativo.
            if (shaderPadrao == null)
            {
                shaderPadrao = referencia.sharedMaterial.shader;
            }

            Material material = new Material(shaderPadrao) { color = cor };
            materiais[cor] = material;
            return material;
        }

        // Enfeite não precisa de física; piso, paredes e móveis continuam colidindo.
        private static void SemColisor(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null)
            {
                Apagar(c);
            }
        }
    }
}
