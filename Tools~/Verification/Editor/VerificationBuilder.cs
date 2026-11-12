using System.IO;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyLocalization.Verification.Editor
{
    public static class VerificationBuilder
    {
        public static string Create(string folder)
        {
            EasyLocalization.Samples.Editor.FirstRunBuilder.Create(folder+"/FirstRun");
            string path=folder+"/Benchmark.unity";
            if(File.Exists(path))return path;
            var previous=SceneManager.GetActiveScene();
            bool restorePrevious=!string.IsNullOrEmpty(previous.path);
            if(!restorePrevious&&previous.isDirty)throw new System.InvalidOperationException("Save the current untitled scene before creating the benchmark.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,restorePrevious?NewSceneMode.Additive:NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            new GameObject("Camera",typeof(Camera));
            var verification=new GameObject("Localization verification").AddComponent<LocalizationVerification>();
            verification.table=LocalizationEditorSettings.GetStringTableCollection("EasyLocalization First Run").SharedData.TableCollectionNameGuid;
            EditorSceneManager.SaveScene(scene,path); if(restorePrevious){EditorSceneManager.CloseScene(scene,true); SceneManager.SetActiveScene(previous);}
            return path;
        }
    }
}
