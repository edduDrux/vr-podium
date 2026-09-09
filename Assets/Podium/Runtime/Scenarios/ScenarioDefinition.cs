using UnityEngine;

namespace Podium.Scenarios
{
    /// <summary>
    /// Um cenário VR: a cena a carregar e o valor que o backend conhece.
    /// <para>
    /// `apiValue` é digitado à mão de propósito. Derivá-lo do nome do asset ou de um enum
    /// C# criaria a ilusão de que o compilador garante o casamento com o
    /// `ScenarioType` do backend — não garante. Escrito à mão, o descasamento aparece
    /// como 422 na primeira execução, e não como cenário silenciosamente ignorado.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "Scenario", menuName = "Podium/Cenário")]
    public class ScenarioDefinition : ScriptableObject
    {
        [Tooltip("Valor aceito pelo backend em ScenarioType. Hoje só existe 'sala_de_aula'.")]
        public string apiValue = "sala_de_aula";

        [Tooltip("Nome mostrado ao usuário no lobby.")]
        public string displayName = "Sala de aula";

        [TextArea(2, 4)]
        public string description = "Defesa de TCC diante de uma banca sentada à sua frente.";

        [Tooltip("Cena a carregar (precisa estar em Build Profiles > Scene List).")]
        public string sceneName = "SalaDeAula";

        [Tooltip("Onde o apresentador nasce, em coordenadas da cena.")]
        public Vector3 spawnPosition = Vector3.zero;

        public float spawnYaw;
    }
}
