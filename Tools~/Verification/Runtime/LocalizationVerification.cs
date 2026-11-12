using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace EasyLocalization.Verification
{
    public sealed class LocalizationVerification : MonoBehaviour
    {
        public TableReference table;
        public bool quitWhenFinished = true;
        private static readonly ProfilerMarker SwitchMarker = new ProfilerMarker("EasyLocalization.SwitchCall");
        [Serializable] public sealed class Measurement
        {
            public int subscribers, switches;
            public double meanSwitchCallMs, meanSwitchAndTextMs;
            public long meanAllocatedBytesPerSwitch, managedMemoryBefore, managedMemoryAfter;
            public int allocationFrames;
        }
        [Serializable] public sealed class Report
        {
            public string unity, platform, utc;
            public bool success;
            public List<string> checks = new List<string>();
            public List<Measurement> measurements = new List<Measurement>();
            public string error;
        }
        private async void Start()
        {
            var report = new Report { unity = Application.unityVersion, platform = Application.platform.ToString(), utc = DateTime.UtcNow.ToString("o") };
            try
            {
                // Deliberately request a string before explicit initialization.
                var first = await Localization.TextAsync(new LocalizedString(table, 1002), new object[] { 7 });
                Require(first.Contains("7"), "Cold start and Smart parameter", report);
                await Localization.SetLanguageAsync("en"); var captured = Localization.CaptureState();
                await Localization.SetLanguageAsync("ru");
                Require((await Localization.TextAsync(new LocalizedString(table,1001))).Contains("Добро"), "Russian table in standalone player", report);
                await Localization.RestoreStateAsync(captured);
                Require(Localization.LanguageCode == "en", "Language state restore", report);
                await Localization.SetLanguageAsync("xx-unavailable");
                Require(Localization.LanguageCode == "en", "Unavailable locale fallback", report);
                var missing = await Localization.TextAsync(new LocalizedString(table,99999), fallback:"Missing");
                Require(missing == "Missing", "Missing entry fallback", report);
                for (int i=0;i<20;i++) { _ = Localization.SetLanguageAsync("ru"); await Localization.SetLanguageAsync("en"); }
                Require(Localization.LanguageCode == "en", "Rapid language requests", report);
                foreach (int count in new[] { 100, 1000, 5000 }) report.measurements.Add(await Measure(count));
                report.success = true;
            }
            catch (Exception exception) { report.error = exception.ToString(); Debug.LogException(exception); }
            string path = Argument("-easyLocalizationReport") ?? Path.Combine(Application.persistentDataPath,"EasyLocalizationReport.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(report,true));
            Debug.Log("EASY_LOCALIZATION_REPORT " + path + " success=" + report.success);
            if (quitWhenFinished && !Application.isEditor) Application.Quit(report.success ? 0 : 1);
        }

        private async Task<Measurement> Measure(int count)
        {
            var root = new GameObject("Subscribers " + count); root.SetActive(false);
            int updated = 0;
            for (int i=0;i<count;i++)
            {
                var go = new GameObject("Text"); go.transform.SetParent(root.transform);
                var text = go.AddComponent<LocalizedText>(); text.text = new LocalizedString(table,1002);
                text.SetArguments(7); text.textChanged.AddListener(_ => updated++);
            }
            root.SetActive(true); await Until(()=>updated>=count);
            for (int i=0;i<6;i++) { updated=0; await Localization.SetLanguageAsync(i%2==0?"ru":"en"); await Until(()=>updated>=count); }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var result = new Measurement { subscribers=count, switches=20, managedMemoryBefore=Profiler.GetMonoUsedSizeLong() };
            using var allocationRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",2048);
            if (!allocationRecorder.Valid) throw new InvalidOperationException("GC allocation recorder is unavailable.");
            double calls=0, totals=0;
            var watch = new Stopwatch();
            for (int i=0;i<result.switches;i++)
            {
                updated=0; watch.Restart(); Task<string> change;
                using(SwitchMarker.Auto()) change=Localization.SetLanguageAsync(i%2==0?"ru":"en");
                calls += watch.Elapsed.TotalMilliseconds;
                await change; await Until(()=>updated>=count); totals += watch.Elapsed.TotalMilliseconds;
                await Task.Yield(); // Flush frame allocation counter, including async subscriber completions.
            }
            allocationRecorder.Stop();
            var samples = new List<ProfilerRecorderSample>(); allocationRecorder.CopyTo(samples);
            long allocated=0; foreach(var sample in samples) allocated+=sample.Value;
            result.allocationFrames=samples.Count;
            result.meanAllocatedBytesPerSwitch=allocated/result.switches;
            if (result.allocationFrames==0 || allocated==0) throw new InvalidOperationException("GC allocation counter produced no samples.");
            result.meanSwitchCallMs=calls/result.switches; result.meanSwitchAndTextMs=totals/result.switches;
            result.managedMemoryAfter=Profiler.GetMonoUsedSizeLong();
            Destroy(root); await Task.Yield(); return result;
        }
        private static async Task Until(Func<bool> condition)
        {
            float deadline=Time.realtimeSinceStartup+30;
            while(!condition()) { if(Time.realtimeSinceStartup>deadline) throw new TimeoutException("Subscriber updates did not finish."); await Task.Yield(); }
        }
        private static void Require(bool value,string name,Report report) { if(!value) throw new Exception(name); report.checks.Add(name); }
        private static string Argument(string key) { var args=Environment.GetCommandLineArgs(); for(int i=0;i<args.Length-1;i++) if(args[i]==key)return args[i+1]; return null; }
    }
}
