using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

namespace EasyLocalization.Editor
{
    [CustomEditor(typeof(SheetMapping))]
    public sealed class SheetMappingInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            try { ((SheetMapping)target).Validate(); }
            catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Warning); }
        }
    }

    [CustomEditor(typeof(LocalizedCatalog))]
    public sealed class LocalizedCatalogInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            bool changed = DrawDefaultInspector();
            var catalog = (LocalizedCatalog)target;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in catalog.entries ?? Array.Empty<CatalogTranslation>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id))
                { EditorGUILayout.HelpBox("Each entry needs a unique stable definition ID or result code. Remove duplicates; do not use a translated display name as the ID.",MessageType.Error); break; }
                if (entry.text == null || entry.text.IsEmpty)
                { EditorGUILayout.HelpBox("Assign a standard Localized String for '"+entry.id+"'. Select its table and entry.",MessageType.Warning); break; }
            }
            if (changed && Application.isPlaying) catalog.NotifyChanged();
        }
    }

    [CustomEditor(typeof(LocalizedCatalogCache))]
    public sealed class LocalizedCatalogCacheInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            bool changed = DrawDefaultInspector();
            var cache = (LocalizedCatalogCache)target;
            if (!cache.catalog) EditorGUILayout.HelpBox("Create a Localized Catalog and assign it here. Missing translations use the caller's fallback.", MessageType.Info);
            if (changed && Application.isPlaying) cache.Refresh();
        }
    }

    [CustomEditor(typeof(LocalizedText))]
    public sealed class LocalizedTextInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var text=(LocalizedText)target;
            if(text.text==null || text.text.IsEmpty)EditorGUILayout.HelpBox("Choose a table and entry in Text. Connect Text Changed to your UI text setter. For parameters call SetArguments(...).",MessageType.Info);
        }
    }
}
