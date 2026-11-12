using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyLocalization.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Tables;

namespace EasyLocalization.Tests
{
    public sealed class PlannerTests
    {
        public static SyncSnapshot Snapshot(string text = "Hello", string key = "welcome", long id = 1001) => new SyncSnapshot
        {
            rows = new List<TranslationRow> { new TranslationRow { id = id, key = key, cells = new List<TranslationCell>
            { new TranslationCell { language = "en", text = text }, new TranslationCell { language = "ru", text = "Привет" } } } }
        };
        private static SyncPlan Plan(SyncSnapshot a, SyncSnapshot b) => SyncPlanner.Create(a, b, a.Copy(), a.Copy(), SyncDirection.PullFromGoogle);

        [Test] public void RenameKeepsStableId() { var a = Snapshot(); var b = Snapshot(key:"renamed"); var p = Plan(a,b); Assert.That(p.Changes.Single().Kind, Is.EqualTo(SyncChangeKind.Renamed)); Assert.That(p.Result().rows[0].id, Is.EqualTo(1001)); }
        [Test] public void ReorderDoesNotChangeFingerprint() { var a = Snapshot(); a.rows.Add(Snapshot(id:1002,key:"second").rows[0]); var b=a.Copy(); b.rows.Reverse(); Assert.That(b.Fingerprint(),Is.EqualTo(a.Fingerprint())); Assert.That(Plan(a,b).Changes,Is.Empty); }
        [Test] public void DuplicateIdsBlockPlan() { var a=Snapshot(); a.rows.Add(Snapshot(key:"other").rows[0]); Assert.That(Plan(a,Snapshot()).Errors,Is.Not.Empty); }
        [Test] public void DuplicateKeysBlockPlan() { var a=Snapshot(); a.rows.Add(Snapshot(id:1002).rows[0]); Assert.That(Plan(a,Snapshot()).CanApply,Is.False); }
        [Test] public void ZeroIdIsInvalid() { Assert.That(Plan(Snapshot(id:0),Snapshot()).Errors,Is.Not.Empty); }
        [Test] public void EmptyTranslationRemainsPresent() { var p=Plan(Snapshot(),Snapshot("")); var c=p.Result().rows[0].cells[0]; Assert.That(c.present,Is.True); Assert.That(c.text,Is.Empty); }
        [Test] public void MissingTranslationIsDifferentFromEmpty() { var a=Snapshot(""); var b=a.Copy(); b.rows[0].cells[0].present=false; Assert.That(a.Fingerprint(),Is.Not.EqualTo(b.Fingerprint())); }
        [Test] public void MissingRowIsKeptByDefault() { var p=Plan(Snapshot(),new SyncSnapshot()); Assert.That(p.Changes[0].Resolution,Is.EqualTo(SyncResolution.KeepTarget)); Assert.That(p.Result().rows.Count,Is.EqualTo(1)); }
        [Test] public void ExplicitDeletionRemovesRow() { var p=Plan(Snapshot(),new SyncSnapshot()); p.Changes[0].Resolution=SyncResolution.DeleteTarget; Assert.That(p.Result().rows,Is.Empty); }
        [Test] public void ConcurrentChangesRequireResolution() { var p=SyncPlanner.Create(Snapshot("local"),Snapshot("remote"),Snapshot(),Snapshot(),SyncDirection.PullFromGoogle); Assert.That(p.Changes[0].Kind,Is.EqualTo(SyncChangeKind.Conflict)); Assert.That(p.CanApply,Is.False); }
        [Test] public void FirstDifferentRowsRequireResolution() { var p=SyncPlanner.Create(Snapshot(),Snapshot("remote"),null,null,SyncDirection.PullFromGoogle); Assert.That(p.CanApply,Is.False); }
        [Test] public void ResolveConflictInBothDirections() { foreach(var d in new[]{SyncDirection.PullFromGoogle,SyncDirection.PushToGoogle}) { var p=SyncPlanner.Create(Snapshot("local"),Snapshot("remote"),Snapshot(),Snapshot(),d); p.Changes[0].Resolution=SyncResolution.UseSource; Assert.That(p.Result().rows[0].cells[0].text,Is.EqualTo(d==SyncDirection.PullFromGoogle?"remote":"local")); } }
        [Test] public void KeepTargetIsRemembered() { var a=Snapshot("local"); var b=Snapshot("remote"); Assert.That(SyncPlanner.Create(a,b,a,b,SyncDirection.PullFromGoogle).Changes,Is.Empty); }
        [Test] public void SnapshotCopyDoesNotAliasData() { var a=Snapshot(); var b=a.Copy(); b.rows[0].cells[0].text="changed"; Assert.That(a.rows[0].cells[0].text,Is.EqualTo("Hello")); }
        [Test] public void SmartParameterMismatchBlocksPlan() { var a=Snapshot(); a.rows[0].cells[0].smart=true; a.rows[0].cells[0].text="{player}"; a.rows[0].cells[1].smart=true; a.rows[0].cells[1].text="{name}"; Assert.That(Plan(a,a).Errors.Any(e=>e.Contains("parameters differ")),Is.True); }
        [Test] public void SmartSyntaxErrorBlocksPlan() { var a=Snapshot("{0"); a.rows[0].cells[0].smart=true; Assert.That(Plan(a,a).Errors,Is.Not.Empty); }
        [Test] public void SmartPluralMayHaveDifferentLiteralBranches() { var a=Snapshot("{0:plural:one|many}"); a.rows[0].cells[0].smart=true; a.rows[0].cells[1].smart=true; a.rows[0].cells[1].text="{0:plural:один|несколько|много}"; Assert.That(Plan(a,a).Errors,Is.Empty); }
        [Test] public void CsvPreservesQuotesMultilineAndTrailingEmpty() { var rows=PublishedCsvTransport.Parse("ID,Key,en,ru\r\n1001,key,\"Hello, \"\"friend\"\"\nnext\",\r\n"); Assert.That(rows[1][2],Is.EqualTo("Hello, \"friend\"\nnext")); Assert.That(rows[1][3],Is.Empty); }
        [Test] public void CsvRejectsUnterminatedQuote() { Assert.Throws<FormatException>(()=>PublishedCsvTransport.Parse("a,\"unfinished")); }
    }

    public sealed class TableAndExchangeTests
    {
        private SheetMapping mapping;
        private string folder, journal;
        private sealed class FakeTransport : ITranslationTransport
        {
            public SyncSnapshot Data = new SyncSnapshot();
            public bool CanWrite => true;
            public int Writes;
            public bool FailAfterCommit, PartialFailure;
            public Action DuringWrite;
            public Task<SyncSnapshot> ReadAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(Data.Copy()); }
            public Task WriteAsync(SyncSnapshot expected, SyncSnapshot result, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested(); Writes++;
                if (PartialFailure) { Data.rows=result.rows.Take(1).Select(r=>r.Copy()).ToList(); PartialFailure=false; throw new IOException("simulated partial service"); }
                Data=result.Copy(); DuringWrite?.Invoke();
                if (FailAfterCommit) { FailAfterCommit=false; throw new IOException("simulated lost response"); }
                return Task.CompletedTask;
            }
        }

        [OneTimeSetUp] public void CreateFixture()
        {
            folder="Assets/PackageTests/EasyLocalization/Automated/"+Guid.NewGuid().ToString("N");
            mapping=LocalizationSetup.Create(folder,"en",new[]{"ru"},"EasyLocalization Test "+Guid.NewGuid().ToString("N"));
            mapping.spreadsheetId="test-only";
            journal=Path.GetFullPath("Temp/EasyLocalization/test-"+Guid.NewGuid().ToString("N")+".json");
        }
        [SetUp] public void Reset()
        {
            mapping.previousLocal=null; mapping.previousRemote=null; mapping.baselineMapping=null;
            if(File.Exists(journal)) File.Delete(journal);
            UnityTableStore.Apply(mapping,PlannerTests.Snapshot());
        }
        [OneTimeTearDown] public void Cleanup() { AssetDatabase.DeleteAsset(folder); if(File.Exists(journal)) File.Delete(journal); }
        private SyncSession Session(FakeTransport fake) => new SyncSession(mapping,fake,journal);

        [Test] public void NativeRenamePreservesIdAndMetadata()
        {
            var en=(StringTable)mapping.collection.GetTable("en"); en.GetEntry(1001).AddMetadata(new Comment{CommentText="keep me"});
            UnityTableStore.Apply(mapping,PlannerTests.Snapshot(key:"new.key"));
            Assert.That(mapping.collection.SharedData.GetId("new.key"),Is.EqualTo(1001));
            Assert.That(en.GetEntry(1001).GetMetadata<Comment>().CommentText,Is.EqualTo("keep me"));
        }
        [Test] public void NativeKeySwapPreservesBothEntries()
        {
            var a=PlannerTests.Snapshot(key:"a"); a.rows.Add(PlannerTests.Snapshot("Second","b",1002).rows[0]); UnityTableStore.Apply(mapping,a);
            a.rows[0].key="b"; a.rows[1].key="a"; UnityTableStore.Apply(mapping,a);
            Assert.That(mapping.collection.SharedData.GetId("b"),Is.EqualTo(1001)); Assert.That(mapping.collection.SharedData.GetId("a"),Is.EqualTo(1002));
        }
        [Test] public void NativeEmptyAndMissingEntriesRemainDistinct()
        {
            var a=PlannerTests.Snapshot(""); a.rows[0].cells[1].present=false; UnityTableStore.Apply(mapping,a);
            Assert.That(((StringTable)mapping.collection.GetTable("en")).GetEntry(1001),Is.Not.Null);
            Assert.That(((StringTable)mapping.collection.GetTable("ru")).GetEntry(1001),Is.Null);
        }
        [Test] public async Task LostResponseIsRecoveredWithoutDuplicateWrite()
        {
            var fake=new FakeTransport { FailAfterCommit=true }; var session=Session(fake);
            var plan=await session.PreviewAsync(SyncDirection.PushToGoogle);
            Assert.ThrowsAsync<IOException>(async()=>await session.ApplyAsync(plan));
            Assert.That(mapping.previousRemote,Is.Null); Assert.That(File.Exists(journal),Is.True);
            var retry=await session.PreviewAsync(SyncDirection.PushToGoogle);
            Assert.That(retry.Changes,Is.Empty); Assert.That(fake.Writes,Is.EqualTo(1)); Assert.That(File.Exists(journal),Is.False);
        }
        [Test] public async Task PartialFailureNeedsNewPlanAndRetryConverges()
        {
            var local=PlannerTests.Snapshot(); local.rows.Add(PlannerTests.Snapshot("Two","second",1002).rows[0]); UnityTableStore.Apply(mapping,local);
            var fake=new FakeTransport { PartialFailure=true }; var session=Session(fake);
            Assert.ThrowsAsync<IOException>(async()=>await session.ApplyAsync(await session.PreviewAsync(SyncDirection.PushToGoogle)));
            Assert.That(mapping.previousRemote,Is.Null);
            await session.ApplyAsync(await session.PreviewAsync(SyncDirection.PushToGoogle));
            Assert.That(fake.Data.Fingerprint(),Is.EqualTo(local.Fingerprint())); Assert.That(fake.Data.rows.Select(r=>r.id).Distinct().Count(),Is.EqualTo(2));
            Assert.That((await session.PreviewAsync(SyncDirection.PushToGoogle)).Changes,Is.Empty);
        }
        [Test] public async Task LocalEditAfterPreviewStopsApply()
        {
            var fake=new FakeTransport(); var session=Session(fake); var plan=await session.PreviewAsync(SyncDirection.PushToGoogle);
            UnityTableStore.Apply(mapping,PlannerTests.Snapshot("changed"));
            Assert.ThrowsAsync<StaleSyncPlanException>(async()=>await session.ApplyAsync(plan)); Assert.That(fake.Writes,Is.Zero);
        }
        [Test] public async Task RemoteEditAfterPreviewStopsApply()
        {
            var fake=new FakeTransport(); var session=Session(fake); var plan=await session.PreviewAsync(SyncDirection.PushToGoogle);
            fake.Data=PlannerTests.Snapshot("changed"); Assert.ThrowsAsync<StaleSyncPlanException>(async()=>await session.ApplyAsync(plan)); Assert.That(fake.Writes,Is.Zero);
        }
        [Test] public async Task MappingEditAfterPreviewStopsApply()
        {
            var fake=new FakeTransport(); var session=Session(fake); var plan=await session.PreviewAsync(SyncDirection.PushToGoogle);
            mapping.sheetId++;
            try { Assert.ThrowsAsync<StaleSyncPlanException>(async()=>await session.ApplyAsync(plan)); Assert.That(fake.Writes,Is.Zero); } finally { mapping.sheetId--; }
        }
        [Test] public async Task CancelBeforeApplyDoesNotWrite()
        {
            var fake=new FakeTransport(); var session=Session(fake); var plan=await session.PreviewAsync(SyncDirection.PushToGoogle);
            using(var cts=new CancellationTokenSource()) { cts.Cancel(); Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(),async()=>await session.ApplyAsync(plan,cts.Token)); }
            Assert.That(fake.Writes,Is.Zero);
        }
        [Test] public async Task UnresolvedConflictDoesNotWrite()
        {
            var fake=new FakeTransport { Data=PlannerTests.Snapshot("remote") }; var session=Session(fake); var plan=await session.PreviewAsync(SyncDirection.PushToGoogle);
            Assert.ThrowsAsync<InvalidOperationException>(async()=>await session.ApplyAsync(plan)); Assert.That(fake.Writes,Is.Zero);
        }
        [Test] public async Task PullAppliesExactlySelectedResolution()
        {
            var fake=new FakeTransport { Data=PlannerTests.Snapshot("remote","renamed") }; var session=Session(fake); var plan=await session.PreviewAsync(SyncDirection.PullFromGoogle);
            plan.Changes[0].Resolution=SyncResolution.UseSource; await session.ApplyAsync(plan);
            Assert.That(UnityTableStore.Read(mapping).Fingerprint(),Is.EqualTo(fake.Data.Fingerprint())); Assert.That(fake.Writes,Is.Zero);
        }
        [Test] public void InvalidLanguageMappingIsActionable() { mapping.languages[1].column=mapping.languages[0].column; try { Assert.Throws<InvalidOperationException>(mapping.Validate); } finally { mapping.languages[1].column=4; } }
        [Test] public void CsvMappingPreservesExistingSmartFlag() { var t=(StringTable)mapping.collection.GetTable("en"); t.GetEntry(1001).IsSmart=true; var snapshot=PublishedCsvTransport.ReadCsv("ID,Key,en,ru\n1001,welcome,{0},Привет",mapping); Assert.That(snapshot.rows[0].cells[0].smart,Is.True); }
    }
}
