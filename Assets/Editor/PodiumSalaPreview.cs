using Podium.Scenarios;
using Podium.Session;
using Podium.Slides;
using UnityEditor;
using UnityEngine;

namespace Podium.EditorTools
{
    /// <summary>
    /// Monta a Sala de Aula na Scene view sem entrar em Play, e a derruba de novo.
    /// <para>
    /// A sala nasce em <c>Start()</c>, de primitivos criados por código, para que o material
    /// seja sempre o do pipeline ativo. O preço disso é que a cena salva tem três objetos:
    /// quem abre o arquivo vê um cenário vazio e conclui que falta montar a sala. Esta
    /// pré-visualização paga esse preço de volta — dá para conferir a tela de projeção, o
    /// monitor do púlpito e a distância da banca ajustando números no Inspector, sem o
    /// vaivém de dar Play a cada mudança.
    /// </para>
    /// <para>
    /// Nada do que ela cria é salvo: os objetos entram com <c>HideFlags.DontSave</c>.
    /// </para>
    /// </summary>
    public static class PodiumSalaPreview
    {
        [MenuItem("Podium/Pré-visualizar a Sala de Aula")]
        public static void PreVisualizar()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            SalaDeAulaCenografia cenografia = Object.FindAnyObjectByType<SalaDeAulaCenografia>();
            if (cenografia == null)
            {
                EditorUtility.DisplayDialog(
                    "Podium",
                    "Nenhuma SalaDeAulaCenografia na cena aberta.\n\n" +
                    "Abra Assets/Podium/Scenes/SalaDeAula.unity e tente de novo.",
                    "Fechar");
                return;
            }

            cenografia.Montar(true);
            MostrarPrimeiroSlide();

            // Sem marcar a cena como suja: a sala da pré-visualização não é conteúdo dela,
            // e pedir para salvar convidaria a gravar centenas de primitivos no arquivo.
            SceneView.RepaintAll();

            Debug.Log("[Podium] Sala montada para conferir na Scene view. " +
                      "Ela não é salva no arquivo da cena; use Podium > Limpar a " +
                      "pré-visualização para derrubá-la.");
        }

        [MenuItem("Podium/Limpar a pré-visualização da sala")]
        public static void Limpar()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            SalaDeAulaCenografia cenografia = Object.FindAnyObjectByType<SalaDeAulaCenografia>();
            if (cenografia != null)
            {
                cenografia.Limpar();
            }

            ApresentacaoDeSlides slides = Object.FindAnyObjectByType<ApresentacaoDeSlides>();
            if (slides != null)
            {
                slides.Limpar();
            }

            SceneView.RepaintAll();
        }

        /// <summary>
        /// Põe o slide 1 na parede usando o mesmo caminho que o lobby manda para o /init.
        /// Assim a pré-visualização mostra a tela com o material de verdade, e não um
        /// retângulo escuro que não diz se a pasta de imagens está no lugar.
        /// </summary>
        private static void MostrarPrimeiroSlide()
        {
            ApresentacaoDeSlides slides = Object.FindAnyObjectByType<ApresentacaoDeSlides>();
            SalaDeAulaController controller = Object.FindAnyObjectByType<SalaDeAulaController>();

            if (slides == null || controller == null)
            {
                return;
            }

            SerializedObject serializado = new SerializedObject(controller);
            SerializedProperty caminho = serializado.FindProperty("presentationPath");

            if (caminho == null || string.IsNullOrEmpty(caminho.stringValue))
            {
                return;
            }

            slides.Carregar(caminho.stringValue);
            Debug.Log("[Podium] Slides: " + slides.Diagnostico);
        }
    }
}
