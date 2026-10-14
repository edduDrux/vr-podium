using System;
using System.Globalization;
using Podium.Session;
using Podium.Slides;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UsosDoInput = UnityEngine.InputSystem.CommonUsages;

namespace Podium.XR
{
    /// <summary>Com o que o headset está lendo a pessoa agora.</summary>
    public enum ModoDeEntrada
    {
        Nenhum,
        Controles,
        Maos
    }

    /// <summary>Para onde vão os comandos de avançar e voltar.</summary>
    public enum AlvoDaNavegacao
    {
        Nenhum,
        Slides,
        Perguntas
    }

    /// <summary>
    /// Os comandos de quem apresenta, reconhecidos tanto no controle quanto na mão.
    /// <para>
    /// Apertar os botões do painel funciona pelos dois caminhos sem código aqui: o raio do
    /// XR Interaction Toolkit segue a pose de mira do controle ou da mão, e as ações padrão
    /// dele já tratam o gatilho e a pinça (perfil de interação de mão do OpenXR,
    /// <c>XR_EXT_hand_interaction</c>). O que este componente acrescenta são os comandos que
    /// não pedem mira — os de quem está olhando para a banca e não para o painel:
    /// </para>
    /// <list type="bullet">
    /// <item>A ou X avança, B ou Y volta: slides durante a fala, perguntas no feedback — a
    /// mesma regra das setas do teclado no PC;</item>
    /// <item>Menu (controle esquerdo) traz o painel para perto;</item>
    /// <item>com as mãos, a pinça segurada por quase um segundo, apontando para fora do
    /// painel, faz o mesmo que o Menu.</item>
    /// </list>
    /// <para>
    /// As ações são criadas em código, e não num asset, pelo mesmo motivo que o painel usa a
    /// fonte embutida: a cena precisa funcionar em qualquer clone sem passo manual. Os
    /// caminhos usam os usos (<c>{PrimaryButton}</c>, <c>{MenuButton}</c>), que valem para
    /// Touch, Touch Plus e Touch Pro sem citar nenhum pelo nome.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(SalaDeAulaController))]
    public class ComandosDoApresentador : MonoBehaviour
    {
        [Header("Dependências")]
        [SerializeField] private SalaDeAulaController controlador;
        [SerializeField] private ApresentacaoDeSlides slides;
        [SerializeField] private PainelEmVR painel;
        [SerializeField] private PodiumXRRig rig;

        [Header("Mãos")]
        [Tooltip("Segundos de pinça segurada, com a mão apontando para fora do painel, para " +
                 "trazer o painel para perto. Curto demais confunde com o gesto de quem fala.")]
        [SerializeField] private float segundosDePincaLonga = 0.9f;

        [Header("Controles")]
        [Tooltip("Vibração curta que confirma o comando sem que a pessoa precise olhar.")]
        [SerializeField] private float intensidadeDaVibracao = 0.35f;

        [SerializeField] private float duracaoDaVibracao = 0.06f;

        // Nomes dos modelos de controle no rig das Starter Assets. Escondidos quando é a mão
        // que dirige aquele lado: um controle flutuando na ponta dos dedos confunde mais do
        // que ajuda.
        private const string VisualEsquerdo = "Left Controller Visual";
        private const string VisualDireito = "Right Controller Visual";

        private InputAction avancar;
        private InputAction voltar;
        private InputAction chamarPainel;
        private InputAction pincaLonga;

        private GameObject rigConhecido;
        private XRInputModalityManager modalidade;
        private GameObject visualEsquerdo;
        private GameObject visualDireito;

        /// <summary>O que o headset está lendo: controles, mãos ou nada.</summary>
        public ModoDeEntrada Modo { get; private set; }

        private void Awake()
        {
            if (controlador == null)
            {
                controlador = GetComponent<SalaDeAulaController>();
            }

            if (painel == null)
            {
                painel = GetComponent<PainelEmVR>();
            }

            if (slides == null)
            {
                slides = FindAnyObjectByType<ApresentacaoDeSlides>();
            }

            if (rig == null)
            {
                rig = FindAnyObjectByType<PodiumXRRig>();
            }

            avancar = new InputAction("Avançar", InputActionType.Button);
            avancar.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
            avancar.AddBinding("<XRController>{LeftHand}/{PrimaryButton}");
            avancar.performed += AoAvancar;

            voltar = new InputAction("Voltar", InputActionType.Button);
            voltar.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
            voltar.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
            voltar.performed += AoVoltar;

            // O Menu do controle direito é o botão da Meta, do sistema: só o esquerdo chega.
            chamarPainel = new InputAction("Chamar o painel", InputActionType.Button);
            chamarPainel.AddBinding("<XRController>{LeftHand}/{MenuButton}");
            chamarPainel.performed += AoChamarPainel;

            // `pointerActivated` é a pinça de mirar do perfil de mão do OpenXR — a mesma que
            // aperta os botões do painel. O "hold" é o que separa pedir o painel de clicar.
            pincaLonga = new InputAction("Pinça longa", InputActionType.Button,
                                         interactions: "hold(duration=" +
                                                       segundosDePincaLonga.ToString(CultureInfo.InvariantCulture) +
                                                       ")");
            pincaLonga.AddBinding("<HandInteraction>{LeftHand}/pointerActivated");
            pincaLonga.AddBinding("<HandInteraction>{RightHand}/pointerActivated");
            pincaLonga.performed += AoSegurarPinca;
        }

        private void OnEnable()
        {
            avancar.Enable();
            voltar.Enable();
            chamarPainel.Enable();
            pincaLonga.Enable();
        }

        private void OnDisable()
        {
            avancar.Disable();
            voltar.Disable();
            chamarPainel.Disable();
            pincaLonga.Disable();
        }

        private void OnDestroy()
        {
            avancar.Dispose();
            voltar.Dispose();
            chamarPainel.Dispose();
            pincaLonga.Dispose();
        }

        private void Update()
        {
            if (rig == null || !rig.EmHeadset)
            {
                Modo = ModoDeEntrada.Nenhum;
                return;
            }

            ConhecerRig();

            ModoDeEntrada esquerda = ModoDaMao(UsosDoInput.LeftHand);
            ModoDeEntrada direita = ModoDaMao(UsosDoInput.RightHand);
            Modo = Combinar(esquerda, direita);

            Mostrar(visualEsquerdo, esquerda != ModoDeEntrada.Maos);
            Mostrar(visualDireito, direita != ModoDeEntrada.Maos);
        }

        // ----------------------------------------------------------------- regras

        /// <summary>
        /// Avançar e voltar seguem a tela que está no ar: perguntas quando o navegador de
        /// perguntas está aberto, slides no resto — a mesma regra das setas no PC. Duas
        /// coisas andando com o mesmo botão seria surpresa, não atalho.
        /// </summary>
        public static AlvoDaNavegacao AlvoPara(bool navegandoPerguntas, bool temSlides)
        {
            if (navegandoPerguntas)
            {
                return AlvoDaNavegacao.Perguntas;
            }

            return temSlides ? AlvoDaNavegacao.Slides : AlvoDaNavegacao.Nenhum;
        }

        /// <summary>
        /// Um dispositivo de mão, e não um controle, pelo nome do layout. Todo layout de mão
        /// que o OpenXR e o XR Hands registram tem "Hand" no nome (HandInteraction,
        /// MetaAimHand, XRHandDevice, HoloLensHand); nenhum controle tem.
        /// </summary>
        public static bool EhMao(string layout)
        {
            return !string.IsNullOrEmpty(layout) &&
                   layout.IndexOf("Hand", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Um lado com controle já basta para a ajuda falar de botões: quem segura um
        /// controle numa mão está usando os botões dele, mesmo com a outra mão livre.
        /// </summary>
        public static ModoDeEntrada Combinar(ModoDeEntrada esquerda, ModoDeEntrada direita)
        {
            if (esquerda == ModoDeEntrada.Controles || direita == ModoDeEntrada.Controles)
            {
                return ModoDeEntrada.Controles;
            }

            if (esquerda == ModoDeEntrada.Maos || direita == ModoDeEntrada.Maos)
            {
                return ModoDeEntrada.Maos;
            }

            return ModoDeEntrada.Nenhum;
        }

        // ----------------------------------------------------------------- ações

        private void AoAvancar(InputAction.CallbackContext contexto)
        {
            Navegar(1, contexto);
        }

        private void AoVoltar(InputAction.CallbackContext contexto)
        {
            Navegar(-1, contexto);
        }

        private void Navegar(int passo, InputAction.CallbackContext contexto)
        {
            if (controlador == null || rig == null || !rig.EmHeadset)
            {
                return;
            }

            AlvoDaNavegacao alvo = AlvoPara(controlador.NoNavegadorDePerguntas,
                                            slides != null && slides.TemSlides);

            switch (alvo)
            {
                case AlvoDaNavegacao.Perguntas:
                    controlador.IrParaPergunta(controlador.PerguntaSelecionada + passo);
                    break;

                case AlvoDaNavegacao.Slides:
                    if (passo > 0)
                    {
                        slides.Proximo();
                    }
                    else
                    {
                        slides.Anterior();
                    }

                    break;

                default:
                    return;
            }

            Vibrar(contexto);
        }

        private void AoChamarPainel(InputAction.CallbackContext contexto)
        {
            if (painel == null || !painel.Montado)
            {
                return;
            }

            painel.TrazerParaPerto();
            Vibrar(contexto);
        }

        private void AoSegurarPinca(InputAction.CallbackContext contexto)
        {
            if (painel == null || !painel.Montado)
            {
                return;
            }

            // Pinça longa em cima do painel é alguém segurando um botão, não pedindo o painel.
            if (MaoApontandoParaInterface(Aparelho(contexto)))
            {
                return;
            }

            painel.TrazerParaPerto();
        }

        // ------------------------------------------------------------- auxiliares

        /// <summary>
        /// Guarda as peças do rig que os comandos usam: os dois lados (controle ou mão), para
        /// vibrar e para saber se o raio está no painel, e os modelos dos controles.
        /// </summary>
        private void ConhecerRig()
        {
            GameObject atual = rig.Rig;
            if (atual == rigConhecido)
            {
                return;
            }

            rigConhecido = atual;
            modalidade = atual != null ? atual.GetComponentInChildren<XRInputModalityManager>(true) : null;
            visualEsquerdo = Procurar(atual, VisualEsquerdo);
            visualDireito = Procurar(atual, VisualDireito);
        }

        /// <summary>
        /// Controle ou mão que está dirigindo cada lado agora. Entre vários dispositivos com
        /// o mesmo lado, vale o que recebeu dados por último — é assim que o próprio XR
        /// Interaction Toolkit escolhe.
        /// </summary>
        private static ModoDeEntrada ModoDaMao(InternedString lado)
        {
            ModoDeEntrada modo = ModoDeEntrada.Nenhum;
            double maisRecente = double.MinValue;

            ReadOnlyArray<InputDevice> aparelhos = InputSystem.devices;
            for (int i = 0; i < aparelhos.Count; i++)
            {
                XRController controle = aparelhos[i] as XRController;
                if (controle == null || NaoEhControleNemMao(controle.layout) ||
                    !TemUso(controle, lado) || !controle.isTracked.isPressed)
                {
                    continue;
                }

                if (controle.lastUpdateTime < maisRecente)
                {
                    continue;
                }

                maisRecente = controle.lastUpdateTime;
                modo = EhMao(controle.layout) ? ModoDeEntrada.Maos : ModoDeEntrada.Controles;
            }

            return modo;
        }

        /// <summary>
        /// Recursos do OpenXR que se registram como controle sem serem um: só acrescentam
        /// uma pose (palma) ou um direcional a outro dispositivo. O XR Interaction Toolkit
        /// ignora os mesmos dois ao decidir entre controle e mão.
        /// </summary>
        private static bool NaoEhControleNemMao(string layout)
        {
            return layout == "PalmPose" || layout == "DPad";
        }

        private static bool TemUso(InputDevice aparelho, InternedString uso)
        {
            ReadOnlyArray<InternedString> usos = aparelho.usages;
            for (int i = 0; i < usos.Count; i++)
            {
                if (usos[i] == uso)
                {
                    return true;
                }
            }

            return false;
        }

        private static InputDevice Aparelho(InputAction.CallbackContext contexto)
        {
            return contexto.control != null ? contexto.control.device : null;
        }

        /// <summary>
        /// O lado do rig (o objeto do controle) que corresponde ao aparelho do comando. Com o
        /// perfil de mão do OpenXR a mão dirige esse mesmo objeto — o raio dela é o raio dele.
        /// </summary>
        private GameObject LadoDoRig(InputDevice aparelho)
        {
            if (modalidade == null || aparelho == null)
            {
                return null;
            }

            if (TemUso(aparelho, UsosDoInput.LeftHand))
            {
                return modalidade.leftController;
            }

            if (TemUso(aparelho, UsosDoInput.RightHand))
            {
                return modalidade.rightController;
            }

            return null;
        }

        private bool MaoApontandoParaInterface(InputDevice aparelho)
        {
            GameObject lado = LadoDoRig(aparelho);
            if (lado == null)
            {
                return false;
            }

            NearFarInteractor raio = lado.GetComponentInChildren<NearFarInteractor>();
            RaycastResult resultado;
            return raio != null && raio.TryGetCurrentUIRaycastResult(out resultado);
        }

        private void Vibrar(InputAction.CallbackContext contexto)
        {
            GameObject lado = LadoDoRig(Aparelho(contexto));
            if (lado == null)
            {
                return;
            }

            HapticImpulsePlayer vibrador = lado.GetComponentInChildren<HapticImpulsePlayer>(true);
            if (vibrador != null)
            {
                vibrador.SendHapticImpulse(intensidadeDaVibracao, duracaoDaVibracao);
            }
        }

        private static void Mostrar(GameObject alvo, bool visivel)
        {
            if (alvo != null && alvo.activeSelf != visivel)
            {
                alvo.SetActive(visivel);
            }
        }

        private static GameObject Procurar(GameObject raiz, string nome)
        {
            if (raiz == null)
            {
                return null;
            }

            foreach (Transform filho in raiz.GetComponentsInChildren<Transform>(true))
            {
                if (filho.name == nome)
                {
                    return filho.gameObject;
                }
            }

            return null;
        }
    }
}
