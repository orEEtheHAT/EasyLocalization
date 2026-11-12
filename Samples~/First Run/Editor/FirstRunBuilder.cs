using System.IO;
using EasyLocalization.Editor;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;

namespace EasyLocalization.Samples.Editor
{
    public static class FirstRunBuilder
    {
        [MenuItem("Tools/EasyLocalization/Create First Run Scene")]
        public static void CreateFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Create("Assets/EasyLocalization Example", true);
        }

        public static string Create(string folder, bool open = false)
        {
            var mapping = LocalizationSetup.Create(folder, "en", new[] { "ru" }, "EasyLocalization First Run");
            var collection = mapping.collection;
            Add(collection, 1001, "demo.welcome", "Welcome!", "Добро пожаловать!", false);
            Add(collection, 1002, "demo.score", "Score: {0}", "Счёт: {0}", true);
            Add(collection, 1003, "demo.language", "Language", "Язык", false);
            AssetDatabase.SaveAssets();
            string path = folder + "/FirstRun.unity";
            if (File.Exists(path)) { if (open) EditorSceneManager.OpenScene(path); return path; }
            var previous = SceneManager.GetActiveScene();
            bool restorePrevious = !string.IsNullOrEmpty(previous.path);
            if (!restorePrevious && previous.isDirty) throw new System.InvalidOperationException("Save the current untitled scene before creating the example.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, restorePrevious ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("Camera", typeof(Camera));
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(.035f, .055f, .08f);
            var demo = new GameObject("First Run").AddComponent<FirstRunExample>();
            demo.table = collection.SharedData.TableCollectionNameGuid;
            EditorSceneManager.SaveScene(scene, path);
            if (!open && restorePrevious) { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
            return path;
        }

        private static void Add(StringTableCollection collection, long id, string key, string english, string russian, bool smart)
        {
            var shared = collection.SharedData.GetEntry(id);
            if (shared != null) return; // Rerunning the builder must preserve translations edited by the user.
            if (collection.SharedData.GetEntry(key) != null) throw new System.InvalidOperationException("Sample key already exists under another ID: " + key);
            collection.SharedData.AddKey(key, id);
            var en = collection.GetTable("en") as StringTable; var ru = collection.GetTable("ru") as StringTable;
            en.AddEntry(id, english).IsSmart = smart; ru.AddEntry(id, russian).IsSmart = smart;
            EditorUtility.SetDirty(collection.SharedData); EditorUtility.SetDirty(en); EditorUtility.SetDirty(ru);
        }
    }
}
