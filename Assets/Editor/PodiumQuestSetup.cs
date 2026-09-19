using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager.UI;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

namespace Podium.EditorTools
{
    /// <summary>
    /// Deixa o projeto pronto para gerar um APK que roda no Meta Quest, num comando só.
    /// <para>
    /// Cada item daqui é um lugar onde esquecer um clique custa uma build inteira: sem o
    /// loader do OpenXR o app abre em 2D dentro do headset; sem IL2CPP/ARM64 a Meta recusa
    /// o pacote; sem HTTP em texto claro liberado toda chamada à API local falha com erro
    /// genérico de rede; e com a entrada só no sistema novo os atalhos de teclado que a
    /// apresentação usa passam a lançar exceção.
    /// </para>
    /// <para>
    /// Roda em modo de lote também (<c>-executeMethod</c>), que é como isto foi conferido.
    /// </para>
    /// </summary>
    public static class PodiumQuestSetup
    {
        private const string PacoteDoToolkit = "com.unity.xr.interaction.toolkit";
        private const string AmostraDoRig = "Starter Assets";
        private const string CaminhoDoRig = "XR Origin (XR Rig).prefab";

        private const string LoaderDoOpenXR = "UnityEngine.XR.OpenXR.OpenXRLoader";

        // Android 10. É o piso que a Meta exige do Quest 2 em diante.
        private const int ApiMinima = 29;

        [MenuItem("Podium/Preparar o projeto para o Quest")]
        public static void Preparar()
        {
            List<string> feito = new List<string>();

            feito.Add(ImportarRig());
            feito.Add(LigarOpenXR());
            feito.Add(AjustarAndroid());
            feito.Add(AjustarEntrada());
            feito.Add(AjustarQualidade());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string relatorio = string.Join("\n  · ", feito.Where(x => !string.IsNullOrEmpty(x)));
            Debug.Log("[Podium] Projeto preparado para o Quest:\n  · " + relatorio +
                      "\n\nFalta: atribuir o prefab do rig no componente PodiumXRRig da " +
                      "cena e trocar o baseUrl do PodiumApiConfig pelo IP do PC na rede.");
        }

        /// <summary>
        /// Traz as Starter Assets do XR Interaction Toolkit para dentro de Assets/. É de lá
        /// que sai o rig com controles, raios e interação com interface — a parte que não
        /// compensa reescrever.
        /// </summary>
        private static string ImportarRig()
        {
            string prefab = ProcurarRig();
            if (prefab != null)
            {
                return "rig já importado (" + prefab + ")";
            }

            IEnumerable<Sample> amostras = Sample.FindByPackage(PacoteDoToolkit, string.Empty);
            if (amostras == null)
            {
                return "ATENÇÃO: o XR Interaction Toolkit não está instalado.";
            }

            foreach (Sample amostra in amostras)
            {
                if (amostra.displayName == AmostraDoRig)
                {
                    amostra.Import(Sample.ImportOptions.OverridePreviousImports);
                    AssetDatabase.Refresh();
                    prefab = ProcurarRig();
                    return prefab != null
                        ? "Starter Assets importadas (" + prefab + ")"
                        : "ATENÇÃO: importei as Starter Assets mas não achei o prefab do rig.";
                }
            }

            return "ATENÇÃO: amostra '" + AmostraDoRig + "' não encontrada no pacote.";
        }

        /// <summary>Caminho do prefab do rig dentro de Assets, ou null se ainda não veio.</summary>
        public static string ProcurarRig()
        {
            foreach (string guid in AssetDatabase.FindAssets("XR Origin t:Prefab"))
            {
                string caminho = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(caminho) == CaminhoDoRig)
                {
                    return caminho;
                }
            }

            return null;
        }

        /// <summary>
        /// Liga o OpenXR como loader do Android e do Standalone. Sem isto o app abre como
        /// uma janela plana dentro do headset — e é o sintoma que mais parece "o VR não
        /// funciona" sem que nada tenha dado erro.
        /// </summary>
        private static string LigarOpenXR()
        {
            List<string> ligados = new List<string>();

            foreach (BuildTargetGroup grupo in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                XRGeneralSettings geral = ObterOuCriarSettings(grupo);
                if (geral == null || geral.Manager == null)
                {
                    continue;
                }

                if (XRPackageMetadataStore.AssignLoader(geral.Manager, LoaderDoOpenXR, grupo))
                {
                    ligados.Add(grupo.ToString());
                }

                geral.InitManagerOnStart = true;
                EditorUtility.SetDirty(geral);
                EditorUtility.SetDirty(geral.Manager);
            }

            return ligados.Count > 0
                ? "OpenXR ligado em " + string.Join(" e ", ligados)
                : "OpenXR já estava ligado";
        }

        private static XRGeneralSettings ObterOuCriarSettings(BuildTargetGroup grupo)
        {
            XRGeneralSettingsPerBuildTarget porPlataforma;
            EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey, out porPlataforma);

            if (porPlataforma == null)
            {
                porPlataforma =
                    ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();

                const string pasta = "Assets/XR";
                Directory.CreateDirectory(pasta);
                AssetDatabase.CreateAsset(porPlataforma, pasta + "/XRGeneralSettings.asset");
                AssetDatabase.SaveAssets();
                EditorBuildSettings.AddConfigObject(
                    XRGeneralSettings.k_SettingsKey, porPlataforma, true);
            }

            if (!porPlataforma.HasManagerSettingsForBuildTarget(grupo))
            {
                porPlataforma.CreateDefaultManagerSettingsForBuildTarget(grupo);
            }

            return porPlataforma.SettingsForBuildTarget(grupo);
        }

        /// <summary>
        /// IL2CPP + ARM64, API mínima e HTTP em texto claro — os três que reprovam o APK
        /// ou derrubam a rede sem dizer o porquê.
        /// </summary>
        private static string AjustarAndroid()
        {
            NamedBuildTarget android = NamedBuildTarget.Android;

            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            if ((int)PlayerSettings.Android.minSdkVersion < ApiMinima)
            {
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)ApiMinima;
            }

            // A API do TCC é local e fala HTTP puro; o Android bloqueia isso desde a 9.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            // Vulkan: é o caminho que a Meta documenta para o Quest.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                                           new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);

            return "Android: IL2CPP, ARM64, API " + (int)PlayerSettings.Android.minSdkVersion +
                   ", HTTP liberado, Vulkan";
        }

        /// <summary>
        /// Entrada nos DOIS sistemas. O rig e o painel do headset usam o Input System; a
        /// navegação de slides por teclado usa o `Input` antigo, e num projeto só com o
        /// sistema novo cada tecla apertada viraria exceção.
        /// </summary>
        private static string AjustarEntrada()
        {
            const string caminho = "ProjectSettings/ProjectSettings.asset";
            SerializedObject settings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath(caminho)[0]);

            SerializedProperty entrada = settings.FindProperty("activeInputHandler");
            if (entrada == null)
            {
                return "ATENÇÃO: não consegui ajustar o sistema de entrada.";
            }

            if (entrada.intValue == 2)
            {
                return "entrada já estava nos dois sistemas";
            }

            entrada.intValue = 2;
            settings.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            return "entrada nos dois sistemas (REINICIE o Editor para valer)";
        }

        /// <summary>
        /// Android no nível Mobile de qualidade — é onde vive o asset de URP feito para o
        /// Quest, com MSAA e sombras dimensionados para o custo de GPU de lá.
        /// </summary>
        private static string AjustarQualidade()
        {
            string[] niveis = QualitySettings.names;
            int mobile = System.Array.FindIndex(
                niveis, n => n.Equals("Mobile", System.StringComparison.OrdinalIgnoreCase));

            if (mobile < 0)
            {
                return "ATENÇÃO: não há nível de qualidade 'Mobile'.";
            }

            QualitySettings.SetQualityLevel(mobile);
            return "qualidade do Android no nível Mobile (" + mobile + ")";
        }
    }
}
