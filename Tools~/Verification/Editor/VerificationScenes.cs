using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyLocalization.Verification.Editor
{
    public static class VerificationScenes
    {
        [Serializable] private sealed class State { public SceneSetup[] scenes; }
        private const string StatePath="Library/EasyLocalizationWork/previous-scenes.json";
        public static void Open(string scenePath)
        {
            for(int i=0;i<SceneManager.sceneCount;i++) if(SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save your open scenes before automated verification.");
            Directory.CreateDirectory("Library/EasyLocalizationWork");
            File.WriteAllText(StatePath,JsonUtility.ToJson(new State{scenes=EditorSceneManager.GetSceneManagerSetup()}));
            EditorSceneManager.OpenScene(scenePath);
        }
        public static void Restore()
        {
            if(File.Exists(StatePath))EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<State>(File.ReadAllText(StatePath)).scenes);
        }
    }
}
