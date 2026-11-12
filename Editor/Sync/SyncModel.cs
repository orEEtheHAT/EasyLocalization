using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace EasyLocalization.Editor
{
    [Serializable]
    public sealed class TranslationCell
    {
        public string language;
        public string text = "";
        public bool present = true;
        public bool smart;
    }

    [Serializable]
    public sealed class TranslationRow
    {
        public long id;
        public string key;
        public List<TranslationCell> cells = new List<TranslationCell>();
        public TranslationRow Copy() => JsonUtility.FromJson<TranslationRow>(JsonUtility.ToJson(this));
        public string Fingerprint() => SyncSnapshot.Hash(JsonUtility.ToJson(new TranslationRow
        { id = id, key = key, cells = cells.OrderBy(c => c.language, StringComparer.Ordinal).ToList() }));
    }

    [Serializable]
    public sealed class SyncSnapshot
    {
        public List<TranslationRow> rows = new List<TranslationRow>();
        public SyncSnapshot Copy() => JsonUtility.FromJson<SyncSnapshot>(JsonUtility.ToJson(this));
        public string Fingerprint() => Hash(string.Join("|", rows.OrderBy(r => r.id).Select(r => r.Fingerprint())));
        internal static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }

    public enum SyncDirection { PullFromGoogle, PushToGoogle }
    public enum SyncResolution { Unresolved, UseSource, KeepTarget, DeleteTarget }
    public enum SyncChangeKind { Added, Renamed, Changed, MissingOnSource, Conflict }

    public sealed class SyncChange
    {
        public long Id;
        public TranslationRow Local;
        public TranslationRow Remote;
        public SyncChangeKind Kind;
        public SyncResolution Resolution;
    }

    public sealed class SyncPlan
    {
        public SyncDirection Direction;
        public SyncSnapshot Local;
        public SyncSnapshot Remote;
        public string MappingFingerprint;
        public readonly List<SyncChange> Changes = new List<SyncChange>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool CanApply => Errors.Count == 0 && Changes.All(c => c.Resolution != SyncResolution.Unresolved);

        public SyncSnapshot Result()
        {
            if (!CanApply) throw new InvalidOperationException("Fix validation errors and choose a resolution for every conflict.");
            var result = (Direction == SyncDirection.PullFromGoogle ? Local : Remote).Copy();
            foreach (var change in Changes)
            {
                if (change.Resolution == SyncResolution.KeepTarget) continue;
                result.rows.RemoveAll(r => r.id == change.Id);
                var source = Direction == SyncDirection.PullFromGoogle ? change.Remote : change.Local;
                if (change.Resolution == SyncResolution.UseSource && source != null) result.rows.Add(source.Copy());
            }
            return result;
        }
    }

    public static class SyncPlanner
    {
        public static SyncPlan Create(SyncSnapshot local, SyncSnapshot remote, SyncSnapshot previousLocal,
            SyncSnapshot previousRemote, SyncDirection direction)
        {
            var plan = new SyncPlan { Local = local.Copy(), Remote = remote.Copy(), Direction = direction };
            Validate(local, "Unity", plan.Errors, plan.Warnings);
            Validate(remote, "Google", plan.Errors, plan.Warnings);
            if (plan.Errors.Count != 0) return plan;
            var left = local.rows.ToDictionary(r => r.id);
            var right = remote.rows.ToDictionary(r => r.id);
            var oldLeft = previousLocal?.rows.ToDictionary(r => r.id) ?? new Dictionary<long, TranslationRow>();
            var oldRight = previousRemote?.rows.ToDictionary(r => r.id) ?? new Dictionary<long, TranslationRow>();
            foreach (var id in left.Keys.Union(right.Keys).OrderBy(id => id))
            {
                left.TryGetValue(id, out var a); right.TryGetValue(id, out var b);
                if (Equal(a, b)) continue;
                oldLeft.TryGetValue(id, out var oldA); oldRight.TryGetValue(id, out var oldB);
                if (Equal(a, oldA) && Equal(b, oldB)) continue; // A previously reviewed KeepTarget remains intentional.
                var source = direction == SyncDirection.PullFromGoogle ? b : a;
                var target = direction == SyncDirection.PullFromGoogle ? a : b;
                bool conflict = a != null && b != null && !Equal(a, oldA) && !Equal(b, oldB);
                // A source deletion versus a modified destination is a conflict too; deletions stay opt-in.
                if (source == null && target != null &&
                    !Equal(target, direction == SyncDirection.PullFromGoogle ? oldA : oldB)) conflict = true;
                var kind = source == null ? SyncChangeKind.MissingOnSource : target == null ? SyncChangeKind.Added :
                    conflict ? SyncChangeKind.Conflict : a.key != b.key ? SyncChangeKind.Renamed : SyncChangeKind.Changed;
                plan.Changes.Add(new SyncChange
                {
                    Id = id, Local = a, Remote = b, Kind = kind,
                    Resolution = source == null ? SyncResolution.KeepTarget : conflict ? SyncResolution.Unresolved : SyncResolution.UseSource
                });
            }
            return plan;
        }

        public static void Validate(SyncSnapshot snapshot, string side, List<string> errors, List<string> warnings)
        {
            var ids = new HashSet<long>(); var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in snapshot.rows)
            {
                if (row.id <= 0 || !ids.Add(row.id)) errors.Add($"{side}: invalid or duplicate ID {row.id}. Keep the original ID when renaming or moving a row.");
                if (string.IsNullOrWhiteSpace(row.key) || !keys.Add(row.key)) errors.Add($"{side}: empty or duplicate key '{row.key}'. Give each row a unique key.");
                var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cell in row.cells)
                {
                    if (string.IsNullOrWhiteSpace(cell.language) || !languages.Add(cell.language)) errors.Add($"{side}/{row.key}: duplicate or empty language code.");
                    if (!cell.present) warnings.Add($"{side}/{row.key}/{cell.language}: translation is missing.");
                    else if (string.IsNullOrEmpty(cell.text)) warnings.Add($"{side}/{row.key}/{cell.language}: intentionally empty translation.");
                }
                SmartValidation.Validate(row, side, errors);
            }
        }

        private static bool Equal(TranslationRow a, TranslationRow b) => a == null ? b == null : b != null && a.Fingerprint() == b.Fingerprint();
    }
}
