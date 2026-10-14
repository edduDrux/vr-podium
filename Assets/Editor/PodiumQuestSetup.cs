using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager.UI;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

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
    /// E o mais silencioso de todos: sem perfil de interação ligado no OpenXR, o runtime não
    /// entrega controle nem mão nenhuma ao app. O headset mostra a sala, o rig existe, e não
    /// há raio, clique nem botão — foi exatamente o que o log do Quest 3 registrou em
    /// 2026-09-20 ("Action Sets: No action sets registered").
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
            feito.Add(LigarPerfisDeInteracao());
            feito.Add(AjustarAndroid());
            feito.Add(AjustarEntrada());
            feito.Add(AjustarQualidade());
            feito.Add(AjustarRenderizacaoDoQuest());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string relatorio = string.Join("\n  · ", feito.Where(x => !string.IsNullOrEmpty(x)));
            Debug.Log("[Podium] Projeto preparado para o Quest:\n  · " + relatorio +
                      "\n\nFalta: rodar Podium > Ligar a transcrição local e o headset na " +
                      "cena e trocar o baseUrl do PodiumApiConfig pelo IP do PC na rede.");

            ConferirValidacao();
        }

        /// <summary>
        /// Lista o que a validação do OpenXR reclama, em Android e em Standalone (o Quest
        /// Link). É a mesma lista da janela Project Validation, só que legível em modo de
        /// lote — que é como este projeto confere as configurações.
        /// </summary>
        [MenuItem("Podium/Conferir a validação do OpenXR")]
        public static void ConferirValidacao()
        {
            List<string> linhas = new List<string>();
            List<OpenXRFeature.ValidationRule> problemas = new List<OpenXRFeature.ValidationRule>();

            // O OpenXR 1.18 tem uma regra do Meta Quest Support que lê as configurações do
            // grupo SELECIONADO no Editor, e não do que está sendo validado — com o Standalone
            // selecionado, validar o Android estoura. Selecionar o grupo só muda a seleção da
            // janela de build; a plataforma ativa (e a reimportação que ela custa) fica igual.
            BuildTargetGroup selecionado = EditorUserBuildSettings.selectedBuildTargetGroup;

            try
            {
                foreach (BuildTargetGroup grupo in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
                {
                    EditorUserBuildSettings.selectedBuildTargetGroup = grupo;

                    try
                    {
                        OpenXRProjectValidation.GetCurrentValidationIssues(problemas, grupo);
                    }
                    catch (System.Exception e)
                    {
                        linhas.Add(grupo + ": a validação do pacote falhou (" + e.GetType().Name +
                                   ": " + e.Message + ")");
                        continue;
                    }

                    if (problemas.Count == 0)
                    {
                        linhas.Add(grupo + ": nenhum problema");
                        continue;
                    }

                    foreach (OpenXRFeature.ValidationRule regra in problemas)
                    {
                        linhas.Add(grupo + ": " + (regra.error ? "ERRO " : "aviso ") + regra.message);
                    }
                }
            }
            finally
            {
                EditorUserBuildSettings.selectedBuildTargetGroup = selecionado;
            }

            Debug.Log("[Podium] Validação do OpenXR:\n  · " + string.Join("\n  · ", linhas));
        }

        /// <summary>
        /// Os perfis que dizem ao runtime quais controles o app entende — Touch (Quest 2),
        /// Touch Plus (Quest 3/3S) e Touch Pro — e o perfil de mão padrão do OpenXR
        /// (<c>XR_EXT_hand_interaction</c>), que entrega a mira e a pinça das mãos com os
        /// mesmos nomes que as ações do XR Interaction Toolkit já escutam. Com ele as mãos
        /// apertam os botões do painel sem pacote novo: nem XR Hands, nem SDK da Meta.
        /// <para>
        /// Mais o "Meta Quest Support" no Android, que põe no manifesto o que faz o Quest
        /// abrir o app como VR — e não como uma janela plana.
        /// </para>
        /// </summary>
        private static string LigarPerfisDeInteracao()
        {
            List<string> ligados = new List<string>();

            foreach (BuildTargetGroup grupo in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                OpenXRSettings openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(grupo);
                if (openXR == null)
                {
                    ligados.Add("ATENÇÃO: sem configurações do OpenXR para " + grupo);
                    continue;
                }

                Ligar<OculusTouchControllerProfile>(openXR, grupo, ligados);
                Ligar<MetaQuestTouchPlusControllerProfile>(openXR, grupo, ligados);
                Ligar<MetaQuestTouchProControllerProfile>(openXR, grupo, ligados);
                Ligar<HandInteractionProfile>(openXR, grupo, ligados);

                if (grupo == BuildTargetGroup.Android)
                {
                    Ligar<MetaQuestFeature>(openXR, grupo, ligados);

                    // A entrada passa a ser lida depois de esperar o quadro, mais perto da hora
                    // em que ele aparece: menos atraso entre a pinça (ou o gatilho) e o clique.
                    // É a recomendação da própria validação do Meta Quest Support.
                    if (openXR.latencyOptimization != OpenXRSettings.LatencyOptimization.PrioritizeInputPolling)
                    {
                        openXR.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
                        ligados.Add("latência priorizando a leitura da entrada (Android)");
                    }
                }

                EditorUtility.SetDirty(openXR);
            }

            return ligados.Count > 0
                ? "OpenXR: " + string.Join(", ", ligados)
                : "perfis de controle e de mão já estavam ligados";
        }

        private static void Ligar<T>(OpenXRSettings openXR, BuildTargetGroup grupo, List<string> ligados)
            where T : OpenXRFeature
        {
            T recurso = openXR.GetFeature<T>();
            if (recurso == null)
            {
                ligados.Add("ATENÇÃO: " + typeof(T).Name + " não existe em " + grupo);
                return;
            }

            if (recurso.enabled)
            {
                return;
            }

            recurso.enabled = true;
            EditorUtility.SetDirty(recurso);
            ligados.Add(typeof(T).Name + " (" + grupo + ")");
        }

        /// <summary>
        /// O asset de URP do nível Mobile — o do Quest — vinha do template de celular: sem
        /// MSAA, com HDR e renderizando a 80%. No headset isso é serrilhado cintilando em
        /// toda borda e texto do painel borrado. MSAA 4x é quase de graça na GPU de tiles do
        /// Quest; HDR não serve a esta cena (não há pós-processamento) e força uma cópia da
        /// imagem inteira a cada quadro.
        /// </summary>
        private static string AjustarRenderizacaoDoQuest()
        {
            int mobile = System.Array.FindIndex(
                QualitySettings.names, n => n.Equals("Mobile", System.StringComparison.OrdinalIgnoreCase));

            UniversalRenderPipelineAsset urp = mobile >= 0
                ? QualitySettings.GetRenderPipelineAssetAt(mobile) as UniversalRenderPipelineAsset
                : null;

            if (urp == null)
            {
                return "ATENÇÃO: o nível Mobile não tem asset de URP.";
            }

            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            urp.renderScale = 1f;
            EditorUtility.SetDirty(urp);

            return "URP do Quest: MSAA 4x, sem HDR, escala 1,0 (" + urp.name + ")";
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
