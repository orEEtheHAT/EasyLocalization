using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace EasyLocalization.Editor
{
    public static class UnityTableStore
    {
        public static SyncSnapshot Read(SheetMapping mapping)
        {
            var result = new SyncSnapshot();
            foreach (var shared in mapping.collection.SharedData.Entries)
            {
                var row = new TranslationRow { id = shared.Id, key = shared.Key };
                foreach (var language in mapping.languages)
                {
                    var table = mapping.collection.GetTable(language.languageCode) as StringTable;
                    var entry = table?.GetEntry(shared.Id);
                    row.cells.Add(new TranslationCell { language = language.languageCode,
                        present = entry != null, text = entry?.Value ?? "", smart = entry?.IsSmart ?? false });
                }
                result.rows.Add(row);
            }
            return result;
        }

        public static void Apply(SheetMapping mapping, SyncSnapshot result)
        {
            var errors = new List<string>(); var warnings = new List<string>();
            SyncPlanner.Validate(result, "Result", errors, warnings);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            var collection = mapping.collection;
            var tables = collection.StringTables;
            var objects = new List<UnityEngine.Object> { collection, collection.SharedData };
            objects.AddRange(tables);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("EasyLocalization pull");
            Undo.RegisterCompleteObjectUndo(objects.ToArray(), "EasyLocalization pull");
            try
            {
                var ids = new HashSet<long>(result.rows.Select(r => r.id));
                foreach (var existing in collection.SharedData.Entries.ToArray())
                    if (!ids.Contains(existing.Id)) collection.RemoveEntry(existing.Id);
                // Temporary unique names make swaps (A -> B, B -> A) preserve both IDs and metadata.
                string prefix = "__EasyLocalization_" + Guid.NewGuid().ToString("N") + "_";
                foreach (var row in result.rows)
                {
                    var existing = collection.SharedData.GetEntry(row.id);
                    if (existing != null && existing.Key != row.key) collection.SharedData.RenameKey(row.id, prefix + row.id);
                }
                foreach (var row in result.rows)
                {
                    var existing = collection.SharedData.GetEntry(row.id);
                    if (existing == null) collection.SharedData.AddKey(row.key, row.id);
                    else if (existing.Key != row.key) collection.SharedData.RenameKey(row.id, row.key);
                    foreach (var cell in row.cells)
                    {
                        var table = collection.GetTable(cell.language) as StringTable;
                        if (!table) throw new InvalidOperationException("Missing table: " + cell.language);
                        var entry = table.GetEntry(row.id);
                        if (!cell.present) { if (entry != null) table.Remove(row.id); }
                        else
                        {
                            if (entry == null) entry = table.AddEntry(row.id, cell.text ?? "");
                            else if (entry.Value != cell.text) entry.Value = cell.text ?? "";
                            if (entry.IsSmart != cell.smart) entry.IsSmart = cell.smart;
                        }
                    }
                }
                foreach (var asset in objects) EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(group);
            }
            catch { Undo.RevertAllDownToGroup(group); AssetDatabase.SaveAssets(); throw; }
        }
    }
}
