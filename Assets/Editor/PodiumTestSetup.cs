using System.IO;
using Podium.Api;
using Podium.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Podium.EditorTools
{
    /// <summary>
    /// Monta a cena do teste de fumaça: cria o PodiumApiConfig, o GameObject com o
    /// componente e a ligação entre os dois.
    /// <para>
    /// Existe para que o primeiro teste seja "abrir e dar Play". Arrastar asset para
    /// campo do Inspector é o tipo de passo que falha em silêncio — o campo fica nulo, o
    /// Play estoura NullReference e a conclusão errada é "a API não responde".
    /// </para>
    /// </summary>
    public static class PodiumTestSetup
    {
        private const string ConfigPath = "Assets/Podium/PodiumApiConfig.asset";
        private const string ScenePath = "Assets/Podium/Samples/SmokeTest.unity";

        [MenuItem("Podium/Preparar cena de teste de fumaça")]
        public static void Setup()
        {
            if (AssetDatabase.LoadAssetAtPath<PodiumApiConfig>(ConfigPath) == null)
            {
                AssetDatabase.CreateAsset(
                    ScriptableObject.CreateInstance<PodiumApiConfig>(), ConfigPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(ConfigPath, ImportAssetOptions.ForceUpdate);
            }

            Scene cena = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // O asset é carregado DEPOIS de criar a cena, e não antes: NewScene descarrega
            // os assets que ninguém referencia, e uma referência carregada antes dela vira
            // "fake null" — o objeto C# continua vivo (GetType() responde), mas o objeto
            // nativo já morreu, e `== null` passa a ser verdadeiro. A cena era salva com o
            // campo vazio sem erro nenhum.
            PodiumApiConfig config = AssetDatabase.LoadAssetAtPath<PodiumApiConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError("[Podium] Não foi possível carregar " + ConfigPath);
                return;
            }

            GameObject go = new GameObject("SmokeTest");
            PodiumSmokeTest teste = go.AddComponent<PodiumSmokeTest>();
            teste.Config = config;
            EditorUtility.SetDirty(teste);

            // Conferir antes de salvar: uma cena com o campo vazio é pior do que nenhuma
            // cena, porque parece pronta.
            if (teste.Config == null)
            {
                Debug.LogError("[Podium] O campo 'config' continuou vazio — cena não salva.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(cena, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log("[Podium] Cena pronta e ligada ao config: " + ScenePath);
        }
    }
}
