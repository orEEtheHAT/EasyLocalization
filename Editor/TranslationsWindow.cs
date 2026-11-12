using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Sheets.v4;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.Google;
using UnityEngine;

namespace EasyLocalization.Editor
{
    public sealed class TranslationsWindow : EditorWindow
    {
        [SerializeField] private SheetMapping mapping;
        private UnityEditor.Editor mappingEditor;
        private SyncPlan plan;
        private SyncSession session;
        private SheetsService service;
        private CancellationTokenSource cancellation;
        private string status = "Create or select a mapping, then preview the direction you need.";
        private bool setup = true, credentials, advanced;
        private string spreadsheetUrl = "";
        private string primary = "en", additional = "ru", collectionName = "Game Text", folder = "Assets/Localization";
        private StringTableCollection existing;
        private SheetsServiceProvider existingProvider;
        private Vector2 scroll;
        private bool busy => cancellation != null;

        [MenuItem("Tools/EasyLocalization/Translations")]
        public static void Open() => GetWindow<TranslationsWindow>("EasyLocalization");
        public static void OpenFor(SheetMapping selected)
        {
            var window=GetWindow<TranslationsWindow>("EasyLocalization");
            window.mapping=selected; window.setup=!selected; window.plan=null;
            if(selected && !string.IsNullOrEmpty(selected.spreadsheetId))
                window.spreadsheetUrl=$"https://docs.google.com/spreadsheets/d/{selected.spreadsheetId}/edit#gid={selected.sheetId}";
        }
        private void OnEnable() { minSize = new Vector2(660, 480); AssemblyReloadEvents.beforeAssemblyReload += Cancel; }
        private void OnDisable()
        {
            Cancel(); AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            service?.Dispose(); service = null;
            if (mappingEditor) DestroyImmediate(mappingEditor);
        }
        private void Cancel() => cancellation?.Cancel();

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("EasyLocalization", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(status, MessageType.Info);
            if (busy && GUILayout.Button("Cancel request")) Cancel();
            using (new EditorGUI.DisabledScope(busy || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                setup = EditorGUILayout.Foldout(setup, "First setup", true);
                if (setup) DrawSetup();
                EditorGUILayout.Space();
                var selected = (SheetMapping)EditorGUILayout.ObjectField("Collection mapping", mapping, typeof(SheetMapping), false);
                if (selected != mapping) { mapping = selected; plan = null; if (mappingEditor) DestroyImmediate(mappingEditor); }
                var mappings = AssetDatabase.FindAssets("t:SheetMapping").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<SheetMapping>).ToArray();
                if (mappings.Length > 0)
                {
                    int current = Array.IndexOf(mappings, mapping);
                    int next = EditorGUILayout.Popup("Saved collections", current, mappings.Select(m => m.name).ToArray());
                    if (next >= 0 && next != current) { mapping = mappings[next]; plan = null; if (mappingEditor) DestroyImmediate(mappingEditor); }
                }
                if (mapping)
                {
                    int mode = EditorGUILayout.Popup("Connection", (int)mapping.connection, new[] { "Google account: download + send", "Public link: download only" });
                    if (mode != (int)mapping.connection) { Undo.RecordObject(mapping,"Change connection"); mapping.connection = (SheetConnection)mode; EditorUtility.SetDirty(mapping); plan = null; }
                    spreadsheetUrl = EditorGUILayout.TextField("Google Sheets URL", spreadsheetUrl);
                    if (GUILayout.Button("Connect this URL")) Try(() => { mapping.SetSpreadsheetUrl(spreadsheetUrl); AssetDatabase.SaveAssetIfDirty(mapping); plan = null; status = "URL connected. Check the language columns in Advanced, then preview."; });
                    if (GUILayout.Button("Open Unity string tables")) AssetDatabase.OpenAsset(mapping.collection);
                    advanced = EditorGUILayout.Foldout(advanced,"Advanced: columns, IDs and existing extension",true);
                    if (advanced)
                    {
                        UnityEditor.Editor.CreateCachedEditor(mapping, null, ref mappingEditor);
                        mappingEditor.OnInspectorGUI();
                        if (GUILayout.Button("Use mapping from Unity Google Sheets Extension"))
                            Try(() => status = mapping.AdoptUnityExtension() ? "Existing mapping copied. Connect your local Google account below." : "This collection has no Google Sheets Extension.");
                    }
                    if (mapping.connection == SheetConnection.PublishedCsv)
                        EditorGUILayout.HelpBox("Share the test sheet as Anyone with the link / Viewer, then paste the tab URL including gid. CSV downloads only. Existing Unity Smart flags are preserved. Published CSV links also work in Advanced.", MessageType.Info);
                }
                credentials = EditorGUILayout.Foldout(credentials, "Google account (for sending and private sheets)", true);
                if (credentials) DrawCredentials();
                using (new EditorGUI.DisabledScope(!mapping))
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Preview Google → Unity")) Run(() => Preview(SyncDirection.PullFromGoogle));
                    using (new EditorGUI.DisabledScope(mapping && mapping.connection == SheetConnection.PublishedCsv))
                        if (GUILayout.Button("Preview Unity → Google")) Run(() => Preview(SyncDirection.PushToGoogle));
                    EditorGUILayout.EndHorizontal();
                }
                if (plan != null) DrawPlan();
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorGUILayout.HelpBox("Exit Play Mode before editing or synchronizing tables.", MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSetup()
        {
            primary = EditorGUILayout.TextField(new GUIContent("Primary language", "Stable code: en, ru, fr, pt-BR."), primary);
            additional = EditorGUILayout.TextField("Additional codes (comma separated)", additional);
            existing = (StringTableCollection)EditorGUILayout.ObjectField("Existing collection (optional)", existing, typeof(StringTableCollection), false);
            if (!existing) collectionName = EditorGUILayout.TextField("New collection name", collectionName);
            folder = EditorGUILayout.TextField("Assets folder", folder);
            if (GUILayout.Button("Create / connect languages and collection")) Try(() =>
            {
                mapping = LocalizationSetup.Create(folder, primary, additional.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), collectionName, existing);
                setup = false; plan = null;
                status = "Ready. Edit strings in the standard String Table Collection. Google connection is optional.";
                EditorGUIUtility.PingObject(mapping.collection);
            });
        }

        private void DrawCredentials()
        {
            EditorGUILayout.HelpBox("1. Enable Sheets API and create a Desktop OAuth client in Google Cloud.\n2. Import its JSON here.\n3. Connect Google account and grant Sheets access.\nSecrets and tokens are local in Library/EasyLocalization; mappings contain no secrets.", MessageType.Info);
            if (GUILayout.Button("Import OAuth JSON"))
            {
                string path = EditorUtility.OpenFilePanel("Select Desktop OAuth credentials", "", "json");
                if (!string.IsNullOrEmpty(path)) Try(() => { GoogleConnection.ImportCredentials(path); service?.Dispose(); service = null; status = "Credentials imported locally. Click Connect Google account."; });
            }
            existingProvider = (SheetsServiceProvider)EditorGUILayout.ObjectField("Existing Unity provider (optional)", existingProvider, typeof(SheetsServiceProvider), false);
            using (new EditorGUI.DisabledScope(!existingProvider))
                if (GUILayout.Button("Copy existing credentials to local storage")) Try(() => { GoogleConnection.ImportProvider(existingProvider); service?.Dispose(); service = null; status = "Copied. The original provider asset was not changed; remove secrets from your own tracked assets before publishing."; });
            if (GUILayout.Button("Connect Google account")) Run(async () => { service?.Dispose(); service = await GoogleConnection.ConnectAsync(cancellation.Token); status = "Google account connected. Select a mapping and preview."; });
        }

        private async Task Preview(SyncDirection direction)
        {
            mapping.Validate();
            ITranslationTransport transport;
            if (mapping.connection == SheetConnection.PublishedCsv) transport = new PublishedCsvTransport(mapping);
            else { if (service == null) service = await GoogleConnection.ConnectAsync(cancellation.Token); transport = new GoogleSheetsTransport(service, mapping); }
            session = new SyncSession(mapping, transport) { Progress = message => { status = message; Repaint(); } };
            plan = await session.PreviewAsync(direction, cancellation.Token);
        }

        private void DrawPlan()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(plan.Direction == SyncDirection.PullFromGoogle ? "Google → Unity" : "Unity → Google", EditorStyles.boldLabel);
            foreach (string error in plan.Errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (string warning in plan.Warnings.Take(30)) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            if (plan.Warnings.Count > 30) EditorGUILayout.LabelField($"{plan.Warnings.Count - 30} more warnings. Fix the first rows, then preview again.");
            foreach (var change in plan.Changes)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"{change.Kind} · ID {change.Id}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Unity key", change.Local?.key ?? "<missing row>");
                EditorGUILayout.LabelField("Google key", change.Remote?.key ?? "<missing row>");
                foreach (var code in mapping.languages.Select(l => l.languageCode))
                {
                    string Describe(TranslationRow row)
                    {
                        if (row == null) return "<missing row>";
                        var cell = row.cells.FirstOrDefault(c => c.language == code);
                        return cell == null || !cell.present ? "<missing translation>" : cell.text == "" ? "<empty translation>" : cell.text;
                    }
                    EditorGUILayout.LabelField(code + " · Unity", Describe(change.Local), EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(code + " · Google", Describe(change.Remote), EditorStyles.wordWrappedLabel);
                }
                var source = plan.Direction == SyncDirection.PullFromGoogle ? change.Remote : change.Local;
                if (source == null)
                    change.Resolution = EditorGUILayout.ToggleLeft("Delete target row explicitly (all its languages)", change.Resolution == SyncResolution.DeleteTarget) ? SyncResolution.DeleteTarget : SyncResolution.KeepTarget;
                else
                {
                    string[] labels = { "Choose resolution…", "Use source row", "Keep target row" };
                    int selected = change.Resolution == SyncResolution.UseSource ? 1 : change.Resolution == SyncResolution.KeepTarget ? 2 : 0;
                    int choice = EditorGUILayout.Popup("Resolution", selected, labels);
                    change.Resolution = choice == 1 ? SyncResolution.UseSource : choice == 2 ? SyncResolution.KeepTarget : SyncResolution.Unresolved;
                }
                EditorGUILayout.EndVertical();
            }
            using (new EditorGUI.DisabledScope(!plan.CanApply))
                if (GUILayout.Button("Apply exactly this preview")) Run(async () => { await session.ApplyAsync(plan, cancellation.Token); plan = null; });
        }

        private async void Run(Func<Task> operation)
        {
            if (busy) return;
            cancellation = new CancellationTokenSource();
            try { await operation(); }
            catch (StaleSyncPlanException exception) { plan = exception.UpdatedPlan; status = exception.Message; }
            catch (OperationCanceledException) { status = "Request cancelled or timed out. If sending had started, preview again to verify its outcome before retrying."; plan = null; }
            catch (Exception exception) { status = exception.Message + "\nPreview again before retrying an interrupted send."; plan = null; }
            finally { cancellation.Dispose(); cancellation = null; if (this) Repaint(); }
        }
        private void Try(Action action) { try { action(); } catch (Exception exception) { status = exception.Message; } }
    }
}
