using Podium.Scenarios;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
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
    /// atribuído, o componente degrada para cabeça rastreada na própria câmera da cena —
    /// dá para olhar em volta, não dá para apontar.
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

        private GameObject rig;

        /// <summary>O rig entrou, ou seja: a sessão está sendo vista de dentro do headset.</summary>
        public bool EmHeadset
        {
            get { return rig != null; }
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
            if (!usarHeadset || !HeadsetAtivo())
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
                DesligarCameraDaCena(rig);
                return;
            }

            // Sem prefab, ao menos a cabeça acompanha: uma imagem parada dentro de um
            // headset não é uma interface simplificada, é enjoo.
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[Podium] Sem rig e sem Main Camera: o headset não terá imagem.");
                return;
            }

            camera.transform.SetPositionAndRotation(origem + Vector3.up * 1.6f, frente);

            if (camera.GetComponent<TrackedPoseDriver>() == null)
            {
                camera.gameObject.AddComponent<TrackedPoseDriver>();
            }

            Debug.LogWarning("[Podium] Rig do XR Interaction Toolkit não atribuído: só a " +
                             "cabeça é rastreada, sem controles. Use Podium > Preparar o " +
                             "projeto para o Quest.");
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
    }
}
