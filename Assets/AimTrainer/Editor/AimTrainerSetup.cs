using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AimTrainer.EditorTools
{
    // First open: creates Main.unity with the game in it, opens it, and adds it to the build.
    [InitializeOnLoad]
    public static class AimTrainerSetup
    {
        const string ScenePath = "Assets/AimTrainer/Main.unity";

        static AimTrainerSetup() => EditorApplication.delayCall += Setup;

        // Also callable from the command line: -executeMethod AimTrainer.EditorTools.AimTrainerSetup.Setup
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("AimTrainer").AddComponent<Game>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty) EditorSceneManager.OpenScene(ScenePath);

            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || scenes[0].path != ScenePath)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "VAL Aim Trainer";
            IncludeShader("Standard");
        }

        // The scene is empty until runtime, so builds would strip shaders that only code uses.
        static void IncludeShader(string name)
        {
            var shader = Shader.Find(name);
            var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            if (shader == null || gs == null) return;
            var so = new SerializedObject(gs);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Aim Trainer/Open Main Scene")]
        static void OpenMain()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
