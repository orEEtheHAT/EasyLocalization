using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace EasyLocalization.Editor
{
    public interface ITranslationTransport
    {
        bool CanWrite { get; }
        Task<SyncSnapshot> ReadAsync(CancellationToken cancellation);
        Task WriteAsync(SyncSnapshot expected, SyncSnapshot result, CancellationToken cancellation);
    }

    public sealed class StaleSyncPlanException : Exception
    {
        public SyncPlan UpdatedPlan { get; }
        public StaleSyncPlanException(SyncPlan plan) : base("Data or mapping changed after preview. Review the refreshed plan and choose resolutions again.") => UpdatedPlan = plan;
    }

    public sealed class SyncSession
    {
        private readonly SheetMapping mapping;
        private readonly ITranslationTransport transport;
        private readonly string journalPath;
        public Action<string> Progress;

        [Serializable] private sealed class Journal
        {
            public string mappingFingerprint;
            public SyncDirection direction;
            public SyncSnapshot local, remote, result;
        }

        public SyncSession(SheetMapping mapping, ITranslationTransport transport, string journalPath = null)
        {
            this.mapping = mapping; this.transport = transport;
            string identity = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mapping));
            if (string.IsNullOrEmpty(identity)) identity = mapping.GetInstanceID().ToString();
            this.journalPath = journalPath ?? Path.Combine(GoogleConnection.LocalFolder, "journal-" + identity + ".json");
        }

        public async Task<SyncPlan> PreviewAsync(SyncDirection direction, CancellationToken cancellation = default)
        {
            mapping.Validate();
            if (direction == SyncDirection.PushToGoogle && !transport.CanWrite)
                throw new InvalidOperationException("This connection only downloads translations. Choose OAuth for sending changes.");
            string identity = mapping.Fingerprint();
            Progress?.Invoke("Reading Google data…");
            var remote = await transport.ReadAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (identity != mapping.Fingerprint()) throw new InvalidOperationException("Mapping changed during the request. Preview again.");
            var local = UnityTableStore.Read(mapping);
            string recovery = Recover(local, remote, identity);
            var plan = Plan(local, remote, direction);
            if (recovery != null) plan.Warnings.Add(recovery);
            Progress?.Invoke($"Preview ready: {plan.Changes.Count} differing rows. Nothing applied.");
            return plan;
        }

        public async Task ApplyAsync(SyncPlan plan, CancellationToken cancellation = default)
        {
            if (!plan.CanApply) throw new InvalidOperationException("Resolve every conflict and validation error first.");
            mapping.Validate();
            Progress?.Invoke("Checking both sides again…");
            var remote = await transport.ReadAsync(cancellation);
            var local = UnityTableStore.Read(mapping);
            if (mapping.Fingerprint() != plan.MappingFingerprint || local.Fingerprint() != plan.Local.Fingerprint() || remote.Fingerprint() != plan.Remote.Fingerprint())
                throw new StaleSyncPlanException(Plan(local, remote, plan.Direction));
            cancellation.ThrowIfCancellationRequested();
            var result = plan.Result();
            var errors = new System.Collections.Generic.List<string>();
            SyncPlanner.Validate(result, "Result", errors, new System.Collections.Generic.List<string>());
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            var journal = new Journal { mappingFingerprint = mapping.Fingerprint(), direction = plan.Direction, local = local, remote = remote, result = result };
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath));
            string temporary = journalPath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(journal));
            if (File.Exists(journalPath)) File.Replace(temporary, journalPath, null); else File.Move(temporary, journalPath);
            if (plan.Direction == SyncDirection.PushToGoogle)
            {
                if (!transport.CanWrite) throw new InvalidOperationException("The connection is read-only.");
                Progress?.Invoke("Sending one batch; keep this window open…");
                await transport.WriteAsync(remote, result, cancellation);
                Progress?.Invoke("Verifying Google result…");
                var verified = await transport.ReadAsync(cancellation);
                if (verified.Fingerprint() != result.Fingerprint())
                    throw new InvalidOperationException("Google result differs from the plan. The journal was kept. Preview again to inspect the actual state.");
                SaveBaseline(local, verified, journal.mappingFingerprint);
            }
            else
            {
                Progress?.Invoke("Applying reviewed changes to Unity…");
                UnityTableStore.Apply(mapping, result);
                SaveBaseline(result, remote, journal.mappingFingerprint);
            }
            File.Delete(journalPath);
            Progress?.Invoke("Completed and verified. Baseline saved.");
        }

        private SyncPlan Plan(SyncSnapshot local, SyncSnapshot remote, SyncDirection direction)
        {
            bool sameMapping = mapping.baselineMapping == mapping.Fingerprint();
            var plan = SyncPlanner.Create(local, remote, sameMapping ? mapping.previousLocal : null,
                sameMapping ? mapping.previousRemote : null, direction);
            plan.MappingFingerprint = mapping.Fingerprint();
            return plan;
        }

        private string Recover(SyncSnapshot local, SyncSnapshot remote, string identity)
        {
            if (!File.Exists(journalPath)) return null;
            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(journalPath));
            if (journal.mappingFingerprint != identity) return "An interrupted operation belongs to the previous mapping. It has not been retried. Review this new mapping independently.";
            bool verified = journal.direction == SyncDirection.PushToGoogle
                ? remote.Fingerprint() == journal.result.Fingerprint()
                : local.Fingerprint() == journal.result.Fingerprint() && remote.Fingerprint() == journal.remote.Fingerprint();
            if (verified)
            {
                SaveBaseline(journal.direction == SyncDirection.PushToGoogle ? journal.local : journal.result,
                    journal.direction == SyncDirection.PushToGoogle ? journal.result : journal.remote, identity);
                File.Delete(journalPath);
                return "Verified the interrupted operation against actual data. It was already applied; no duplicate write was sent.";
            }
            return "An earlier operation was interrupted. This preview shows actual data, including any partial result. Review before retrying; the previous baseline is unchanged.";
        }

        private void SaveBaseline(SyncSnapshot local, SyncSnapshot remote, string identity)
        {
            mapping.previousLocal = local.Copy(); mapping.previousRemote = remote.Copy(); mapping.baselineMapping = identity;
            EditorUtility.SetDirty(mapping);
            if (AssetDatabase.Contains(mapping)) AssetDatabase.SaveAssetIfDirty(mapping);
        }
    }
}
