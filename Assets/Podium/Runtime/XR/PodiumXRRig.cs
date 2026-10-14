using Podium.Scenarios;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Management;

namespace Podium.XR
{
    /// <summary>
    /// Põe a pessoa dentro da sala quando há um headset, e sai do caminho quando não há.
    /// <para>
    /// A cena continua abrindo e funcionando no PC com a câmera fixa do Editor — é assim
    /// que se ensaia sem vestir nada, e é o caminho que os testes usam. Só quando um
    /// dispositivo XR está de fato ativo é que o rig entra, e aí quem manda na altura dos
    /// olhos é o tracking, não o número que a cenografia escreveu.
    /// </para>
    /// <para>
    /// O rig vem do prefab do XR Interaction Toolkit (<c>XR Origin (XR Rig)</c>, das
    /// Starter Assets), e não de um montado à mão: controles, raios e interação com
    /// interface são justamente a parte que não compensa reescrever. Sem o prefab
    /// atribuído, o componente monta uma origem mínima com a própria câmera da cena — dá
    /// para olhar em volta e ler o painel, não dá para apontar.
    /// </para>
    /// <para>
    /// O rig nasce assim que o headset fica ativo: quase sempre já no <c>Awake</c>, porque
    /// o XR Management sobe o OpenXR antes da primeira cena. Se nesse instante o loader já
    /// subiu mas o display ainda não, o componente espera quadro a quadro em vez de
    /// concluir que é PC — decidir cedo demais deixava o headset sem rig e sem painel.
    /// Por isso quem depende do rig pergunta <see cref="EmHeadset"/> enquanto a resposta for
    /// não, em vez de perguntar uma vez só no <c>Start</c>.
    /// </para>
    /// </summary>
    public class PodiumXRRig : MonoBehaviour
    {
        [Tooltip("XR Origin (XR Rig) das Starter Assets do XR Interaction Toolkit. " +
                 "Use Podium > Preparar o projeto para o Quest para importar e atribuir.")]
        [SerializeField] private GameObject rigPrefab;

        [Tooltip("Onde a pessoa nasce. Sem isto, a origem da cena.")]
        [SerializeField] private ScenarioDefinition scenario;

        [Tooltip("Desligue para ensaiar a cena no PC mesmo com um headset conectado.")]
        [SerializeField] private bool usarHeadset = true;

        [Header("Conforto")]
        [Tooltip("Tira do rig o andar contínuo pelo analógico e o pulo. Quem apresenta fica " +
                 "no púlpito; andar pelo analógico enjoa, e o pulo ocupa o botão A, que aqui " +
                 "passa o slide. Girar e teletransportar continuam valendo.")]
        [SerializeField] private bool paradoNoPulpito = true;

        private GameObject rig;

        /// <summary>O rig entrou, ou seja: a sessão está sendo vista de dentro do headset.</summary>
        public bool EmHeadset
        {
            get { return rig != null; }
        }

        /// <summary>
        /// A raiz do rig montado (o XR Origin), ou null no PC. É por ela que os comandos
        /// acham os controles, os raios e a vibração.
        /// </summary>
        public GameObject Rig
        {
            get { return rig; }
        }

        /// <summary>Câmera que enxerga a sala — a do rig no headset, a da cena no PC.</summary>
        public Camera Olho
        {
            get
            {
                if (rig != null)
                {
                    XROrigin origem = rig.GetComponent<XROrigin>();
                    if (origem != null && origem.Camera != null)
                    {
                        return origem.Camera;
                    }
                }

                return Camera.main;
            }
        }

        /// <summary>
        /// Um dispositivo XR está rodando de verdade. Não basta o pacote estar instalado:
        /// no Editor sem headset o OpenXR é carregado e nenhum display fica ativo, e montar
        /// o rig aí deixaria a cena sem câmera utilizável.
        /// </summary>
        public static bool HeadsetAtivo()
        {
            if (!XRSettings.isDeviceActive)
            {
                return false;
            }

            return XRCarregado();
        }

        /// <summary>O XR Management tem um loader de pé: o headset pode estar a caminho.</summary>
        private static bool XRCarregado()
        {
            XRGeneralSettings geral = XRGeneralSettings.Instance;
            return geral != null && geral.Manager != null && geral.Manager.activeLoader != null;
        }

        /// <summary>
        /// Em <c>Awake</c>, e não em <c>Start</c>, porque o painel de VR e a cenografia
        /// perguntam em <c>Start</c> se o rig entrou. Entre dois <c>Start</c> do mesmo
        /// objeto a ordem é indefinida, e a versão errada do sorteio deixaria o headset sem
        /// painel — ou a câmera arrancada de dentro do rig pela cenografia.
        /// </summary>
        private void Awake()
        {
            if (!usarHeadset)
            {
                enabled = false;
                return;
            }

            TentarMontar();
        }

        /// <summary>
        /// Só trabalha enquanto o headset não ficou ativo mas o loader do XR está de pé.
        /// Sem loader não há o que esperar — é o PC —, e o componente se desliga.
        /// </summary>
        private void Update()
        {
            if (EmHeadset || !XRCarregado())
            {
                enabled = false;
                return;
            }

            TentarMontar();
        }

        private void TentarMontar()
        {
            if (EmHeadset || !HeadsetAtivo())
            {
                return;
            }

            Vector3 origem = scenario != null ? scenario.spawnPosition : Vector3.zero;
            Quaternion frente = Quaternion.Euler(0f, scenario != null ? scenario.spawnYaw : 0f, 0f);

            if (rigPrefab != null)
            {
                rig = Instantiate(rigPrefab, origem, frente);
                rig.name = "XR Origin (Podium)";
                AjustarOrigem(rig);
                AjustarParaApresentar(rig);
                DesligarCameraDaCena(rig);
            }
            else
            {
                // Sem prefab, ao menos a cabeça acompanha: uma imagem parada dentro de um
                // headset não é uma interface simplificada, é enjoo.
                rig = MontarOrigemMinima(origem, frente);
                if (rig == null)
                {
                    return;
                }

                Debug.LogWarning("[Podium] Rig do XR Interaction Toolkit não atribuído: só a " +
                                 "cabeça é rastreada, sem controles nem mãos. Use Podium > " +
                                 "Preparar o projeto para o Quest.");
            }
        }

        /// <summary>
        /// Chão como referência de altura: é o que faz a mesa da banca ficar na altura da
        /// mesa de quem está de pé. Com a referência no dispositivo, a sala inteira sobe ou
        /// desce conforme a altura em que o headset foi ligado.
        /// </summary>
        private static void AjustarOrigem(GameObject rig)
        {
            XROrigin origem = rig.GetComponent<XROrigin>();
            if (origem != null)
            {
                origem.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            }
        }

        /// <summary>
        /// O rig das Starter Assets vem pronto para explorar uma cena: anda pelo analógico
        /// esquerdo e pula no botão A. Numa defesa de TCC a pessoa fica no púlpito — andar
        /// pelo analógico só a tiraria de lá (e enjoa), e o A é o botão de passar slide.
        /// </summary>
        private void AjustarParaApresentar(GameObject instancia)
        {
            if (!paradoNoPulpito)
            {
                return;
            }

            foreach (ContinuousMoveProvider andar in
                     instancia.GetComponentsInChildren<ContinuousMoveProvider>(true))
            {
                andar.enabled = false;
            }

            foreach (JumpProvider pulo in instancia.GetComponentsInChildren<JumpProvider>(true))
            {
                pulo.enabled = false;
            }
        }

        private static void DesligarCameraDaCena(GameObject rig)
        {
            XROrigin origem = rig.GetComponent<XROrigin>();
            Camera doRig = origem != null ? origem.Camera : null;

            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                // Duas câmeras marcadas MainCamera fazem `Camera.main` virar sorteio, e o
                // resto do projeto pergunta por ela para saber onde a pessoa está.
                if (camera != doRig && camera.CompareTag("MainCamera"))
                {
                    camera.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Um XR Origin feito à mão em volta da câmera da cena. A câmera solta, só com o
        /// rastreamento, ficaria na altura do chão (o rastreamento sem origem não sabe onde
        /// o chão está) e ignoraria o ponto de nascimento do cenário.
        /// </summary>
        private static GameObject MontarOrigemMinima(Vector3 origem, Quaternion frente)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[Podium] Sem rig e sem Main Camera: o headset não terá imagem.");
                return null;
            }

            // Montado desligado: o XROrigin lê os próprios campos no Awake, e eles só estão
            // preenchidos no fim deste método.
            GameObject raiz = new GameObject("XR Origin (Podium, sem controles)");
            raiz.SetActive(false);
            raiz.transform.SetPositionAndRotation(origem, frente);

            GameObject deslocamento = new GameObject("Camera Offset");
            deslocamento.transform.SetParent(raiz.transform, false);

            camera.transform.SetParent(deslocamento.transform, false);
            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;

            // Perto o bastante para o painel e as mãos não sumirem cortados na frente do rosto.
            camera.nearClipPlane = 0.02f;

            XROrigin xrOrigin = raiz.AddComponent<XROrigin>();
            xrOrigin.Camera = camera;
            xrOrigin.CameraFloorOffsetObject = deslocamento;
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            // Os campos vêm vazios quando o componente é criado em tempo de execução: sem as
            // ligações, o driver existe e não rastreia nada.
            TrackedPoseDriver driver = camera.GetComponent<TrackedPoseDriver>();
            if (driver == null)
            {
                driver = camera.gameObject.AddComponent<TrackedPoseDriver>();
            }

            driver.positionInput = new InputActionProperty(
                new InputAction("Posição da cabeça", InputActionType.Value,
                                "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(
                new InputAction("Rotação da cabeça", InputActionType.Value,
                                "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            driver.trackingStateInput = new InputActionProperty(
                new InputAction("Rastreamento da cabeça", InputActionType.Value,
                                "<XRHMD>/trackingState", expectedControlType: "Integer"));

            raiz.SetActive(true);
            return raiz;
        }
    }
}
