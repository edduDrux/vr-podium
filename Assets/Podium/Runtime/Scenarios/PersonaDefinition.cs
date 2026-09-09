using UnityEngine;

namespace Podium.Scenarios
{
    /// <summary>
    /// Uma persona da banca: como ela aparece no VR e o valor que o backend conhece.
    /// Mesma regra do <see cref="ScenarioDefinition"/> — `apiValue` é escrito à mão.
    /// <para>
    /// Cada persona é uma superfície de prompt validada e reportada no TCC. Adicionar uma
    /// aqui sem que exista no backend não cria persona nenhuma: cria um 422.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "Persona", menuName = "Podium/Persona")]
    public class PersonaDefinition : ScriptableObject
    {
        [Tooltip("Valor aceito pelo backend em PersonaType: professor_rigoroso, " +
                 "orientador_acolhedor ou especialista_tecnico.")]
        public string apiValue = "professor_rigoroso";

        public string displayName = "Professor rigoroso";

        [TextArea(2, 4)]
        public string description = "Cobra precisão conceitual e não deixa passar generalização.";

        [Tooltip("Avatar instanciado na cadeira da banca.")]
        public GameObject avatarPrefab;

        [Tooltip("Cadeira ocupada por esta persona na mesa da banca (índice na cena).")]
        public int seatIndex;
    }
}
