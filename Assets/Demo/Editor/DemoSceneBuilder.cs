using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tycoon.Demo.EditorTools
{
    /// <summary>Creates the demo scene (a camera and one object with <see cref="DemoMain"/>) if it is missing and makes sure it is complete.</summary>
    public static class DemoSceneBuilder
    {
        public const string ScenePath = "Assets/Demo/Scenes/GlobeDemo.unity";

        [MenuItem("Tycoon/Create Demo Scene")]
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects), ScenePath);

            var scene = EditorSceneManager.OpenScene(ScenePath);
            foreach (var light in Object.FindObjectsByType<Light>())            // everything is unlit
                Object.DestroyImmediate(light.gameObject);
            if (Object.FindAnyObjectByType<DemoMain>() == null) new GameObject("Demo").AddComponent<DemoMain>();
            EditorSceneManager.SaveScene(scene);
        }
    }
}
