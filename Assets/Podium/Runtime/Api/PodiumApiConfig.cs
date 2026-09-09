using UnityEngine;

namespace Podium.Api
{
    /// <summary>
    /// Endereço e tempos da API, em asset — e não em campo de MonoBehaviour — porque o
    /// mesmo endereço vale para todas as cenas e muda o tempo todo durante o
    /// desenvolvimento (Editor fala com localhost, headset fala com o IP da LAN).
    /// </summary>
    [CreateAssetMenu(fileName = "PodiumApiConfig", menuName = "Podium/API Config")]
    public class PodiumApiConfig : ScriptableObject
    {
        [Header("Endereço")]
        [Tooltip("Sem barra no fim. No headset use o IP da máquina na LAN — 'localhost' " +
                 "é o próprio Quest, não o seu PC. Ex.: http://192.168.0.10:8000")]
        public string baseUrl = "http://localhost:8000";

        [Header("Tempos (segundos)")]
        [Tooltip("Requisições curtas: init, status, feedback.")]
        public int requestTimeout = 30;

        [Tooltip("Upload de áudio: um chunk de 60 s em Wi-Fi doméstico cabe folgado aqui.")]
        public int uploadTimeout = 180;

        [Tooltip("Intervalo entre consultas de status. Abaixo de 2 s só gera tráfego: " +
                 "o pipeline leva minutos, não milissegundos.")]
        public float pollIntervalSeconds = 3f;

        [Tooltip("Teto da espera pelo Feedback Duplo. Medições reais do backend: STT a " +
                 "~0,28x tempo real + geração de 12-18 s. Uma fala de 15 min pode passar " +
                 "de 4 minutos de processamento.")]
        public float pollTimeoutSeconds = 900f;

        /// <summary>Prefixo versionado da API (settings.API_V1_PREFIX no backend).</summary>
        public string ApiRoot
        {
            get { return baseUrl.TrimEnd('/') + "/api/v1"; }
        }
    }
}
