using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace EasyLocalization.Verification.Editor
{
    public static class CleanInstallVerification
    {
        public static void CreateAndBuild()
        {
            try
            {
                var settings=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                var serialized=new SerializedObject(settings[0]);
                var input=serialized.FindProperty("activeInputHandler");
                if(input!=null){input.intValue=1;serialized.ApplyModifiedPropertiesWithoutUndo();}
                string firstRun=EasyLocalization.Samples.Editor.FirstRunBuilder.Create("Assets/EasyLocalization Example");
                string benchmark=VerificationBuilder.Create("Assets/VerificationScene");
                EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(firstRun,true),new EditorBuildSettingsScene(benchmark,true)};
                AssetDatabase.SaveAssets();
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
                { scenes=new[]{benchmark}, target=BuildTarget.StandaloneWindows64, locationPathName="Builds/FirstRun/Localization.exe", options=BuildOptions.Development });
                File.WriteAllText("clean-build-result.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors+"\nSize: "+report.summary.totalSize);
                if(report.summary.result!=BuildResult.Succeeded)EditorApplication.Exit(1);
            }
            catch(Exception exception){File.WriteAllText("clean-build-result.txt",exception.ToString());throw;}
        }
    }
}
