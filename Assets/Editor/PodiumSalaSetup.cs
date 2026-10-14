using System.Collections.Generic;
using Podium.Audio;
using Podium.Scenarios;
using Podium.Session;
using Podium.Slides;
using Podium.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Whisper;

namespace Podium.EditorTools
{
    /// <summary>
    /// Liga na cena da Sala de Aula as peças novas: a transcrição local, o rig do headset, o
    /// painel que aparece dentro dele e os comandos de controle e de mão.
    /// <para>
    /// Existe pelo mesmo motivo do <see cref="PodiumTestSetup"/>: arrastar asset para campo
    /// do Inspector é o passo que falha em silêncio. O campo fica nulo, o Play não estoura
    /// — só não transcreve, ou só não mostra painel nenhum dentro do headset — e a
    /// conclusão errada é que a funcionalidade não funciona.
    /// </para>
    /// <para>
    /// É idempotente: rodar de novo conserta o que estiver faltando e não duplica o que já
    /// existe.
    /// </para>
    /// </summary>
    public static class PodiumSalaSetup
    {
        private const string CaminhoDaCena = "Assets/Podium/Scenes/SalaDeAula.unity";

        [MenuItem("Podium/Ligar a transcrição local e o headset na cena")]
        public static void Ligar()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            Scene cena = EditorSceneManager.OpenScene(CaminhoDaCena, OpenSceneMode.Single);

            PodiumSession session = Object.FindAnyObjectByType<PodiumSession>();
            if (session == null)
            {
                Debug.LogError("[Podium] Nenhum PodiumSession em " + CaminhoDaCena +
                               " — a cena não é a esperada.");
                return;
            }

            GameObject alvo = session.gameObject;
            List<string> feito = new List<string>();

            WhisperManager whisper = Garantir<WhisperManager>(alvo, feito, "WhisperManager");
            ConfigurarWhisper(whisper);

            TranscritorLocal transcritor = Garantir<TranscritorLocal>(alvo, feito, "TranscritorLocal");
            Atribuir(transcritor, "whisper", whisper);

            Atribuir(session, "transcritor", transcritor);

            PodiumXRRig rig = Garantir<PodiumXRRig>(alvo, feito, "PodiumXRRig");
            Atribuir(rig, "scenario", PrimeiroCenario());
            Atribuir(rig, "rigPrefab", CarregarRig(feito));

            SalaDeAulaController controlador = Object.FindAnyObjectByType<SalaDeAulaController>();
            ApresentacaoDeSlides slides = Object.FindAnyObjectByType<ApresentacaoDeSlides>();

            PainelEmVR painel = Garantir<PainelEmVR>(alvo, feito, "PainelEmVR");
            Atribuir(painel, "controlador", controlador);
            Atribuir(painel, "session", session);
            Atribuir(painel, "recorder", Object.FindAnyObjectByType<PresentationRecorder>());
            Atribuir(painel, "slides", slides);
            Atribuir(painel, "rig", rig);

            // Os comandos que não pedem mira: A/B/X/Y nos slides e nas perguntas, Menu e
            // pinça longa para trazer o painel. Sem eles, no headset só o raio funciona.
            ComandosDoApresentador comandos =
                Garantir<ComandosDoApresentador>(alvo, feito, "ComandosDoApresentador");
            Atribuir(comandos, "controlador", controlador);
            Atribuir(comandos, "slides", slides);
            Atribuir(comandos, "painel", painel);
            Atribuir(comandos, "rig", rig);

            Atribuir(painel, "comandos", comandos);

            EditorSceneManager.MarkSceneDirty(cena);
            EditorSceneManager.SaveScene(cena);
            AssetDatabase.SaveAssets();

            Debug.Log("[Podium] Cena ligada:\n  · " + string.Join("\n  · ", feito) +
                      "\n\nConfira o modelo em " + whisper.ModelPath +
                      " (Podium > Baixar o modelo de transcrição).");
        }

        private static T Garantir<T>(GameObject alvo, List<string> feito, string nome)
            where T : Component
        {
            T existente = alvo.GetComponent<T>();
            if (existente != null)
            {
                feito.Add(nome + " já estava na cena");
                return existente;
            }

            T novo = Undo.AddComponent<T>(alvo);
            feito.Add(nome + " adicionado");
            return novo;
        }

        /// <summary>
        /// Português e um segmento por passo: é a configuração de transcrição contínua, e
        /// não a de transcrever um arquivo pronto. `translateToEnglish` fica desligado
        /// porque a banca pergunta sobre o que foi dito, na língua em que foi dito.
        /// </summary>
        private static void ConfigurarWhisper(WhisperManager whisper)
        {
            SerializedObject so = new SerializedObject(whisper);

            so.FindProperty("language").stringValue = "pt";
            so.FindProperty("translateToEnglish").boolValue = false;
            so.FindProperty("initOnAwake").boolValue = true;
            so.FindProperty("isModelPathInStreamingAssets").boolValue = true;
            so.FindProperty("enableTokens").boolValue = false;

            // A janela deslizante do whisper.cpp: 3 s por inferência, 10 s de contexto.
            // Passos menores dariam texto mais cedo e gastariam CPU que o Quest usa para
            // desenhar a sala.
            so.FindProperty("stepSec").floatValue = 3f;
            so.FindProperty("lengthSec").floatValue = 10f;
            so.FindProperty("useVad").boolValue = true;

            SerializedProperty modelo = so.FindProperty("modelPath");
            if (string.IsNullOrEmpty(modelo.stringValue) ||
                modelo.stringValue == "Whisper/ggml-tiny.bin")
            {
                modelo.stringValue = PodiumModeloSetup.CaminhoRelativo;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ScenarioDefinition PrimeiroCenario()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ScenarioDefinition"))
            {
                return AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
            }

            return null;
        }

        private static GameObject CarregarRig(List<string> feito)
        {
            string caminho = PodiumQuestSetup.ProcurarRig();
            if (caminho == null)
            {
                feito.Add("ATENÇÃO: prefab do rig não encontrado — rode " +
                          "Podium > Preparar o projeto para o Quest primeiro.");
                return null;
            }

            feito.Add("rig do XR Interaction Toolkit ligado");
            return AssetDatabase.LoadAssetAtPath<GameObject>(caminho);
        }

        /// <summary>
        /// Escreve num campo privado marcado com <c>[SerializeField]</c>. Sem isto, ligar a
        /// cena exigiria tornar público campo que só o Inspector deveria tocar.
        /// </summary>
        private static void Atribuir(Object alvo, string campo, Object valor)
        {
            if (alvo == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(alvo);
            SerializedProperty p = so.FindProperty(campo);

            if (p == null)
            {
                Debug.LogWarning("[Podium] Campo '" + campo + "' não existe em " +
                                 alvo.GetType().Name + ".");
                return;
            }

            p.objectReferenceValue = valor;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(alvo);
        }
    }
}
