using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EasyLocalization.Editor;
using Google.Apis.Http;
using Google.Apis.Json;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using NUnit.Framework;
using UnityEditor;

namespace EasyLocalization.Tests
{
    // Exercises the actual bundled Google SDK and transport. HTTP is simulated, OAuth is not invoked.
    public sealed class GoogleTransportTests
    {
        private const long LargeId=9007199254741005;
        private SheetMapping mapping;
        private string folder;
        private SheetsService service;
        private FakeHandler handler;
        private GoogleSheetsTransport transport;

        private sealed class Factory : HttpClientFactory
        {
            private readonly HttpMessageHandler handler;
            public Factory(HttpMessageHandler handler)=>this.handler=handler;
            protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args)=>handler;
        }
        private sealed class FakeHandler : HttpMessageHandler
        {
            public int MetadataFailures, MetadataCalls, Writes;
            public bool LoseWriteResponse;
            public Action OnMetadata;
            public readonly List<RowData> Rows=new List<RowData>();
            public string LastBatch;
            public FakeHandler()
            {
                Rows.Add(Row("ID","Key","en","ru","Notes"));
                Rows.Add(Row(LargeId.ToString(),"original","Hello","Привет","KEEP"));
                Rows[1].Values[2].Note="Translator's note";
            }
            private static RowData Row(params string[] values)=>new RowData{Values=values.Select(v=>new CellData{UserEnteredValue=new ExtendedValue{StringValue=v}}).ToList()};
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if(request.Method==HttpMethod.Get)
                {
                    MetadataCalls++;
                    OnMetadata?.Invoke();
                    if(MetadataFailures-->0)return new HttpResponseMessage((HttpStatusCode)429){Content=new StringContent("{\"error\":{\"code\":429,\"message\":\"retry test\"}}")};
                    return Json(new Spreadsheet{Sheets=new List<Sheet>{new Sheet{Properties=Properties()}}});
                }
                if(request.RequestUri.AbsolutePath.EndsWith(":getByDataFilter",StringComparison.Ordinal))
                    return Json(new Spreadsheet{Sheets=new List<Sheet>{new Sheet{Properties=Properties(),Data=new List<GridData>{new GridData{StartRow=0,RowData=Rows}}}}});
                if(!request.RequestUri.AbsolutePath.EndsWith(":batchUpdate",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected HTTP request: "+request.RequestUri.AbsolutePath);
                Writes++;
                if(request.Content.Headers.ContentEncoding.Contains("gzip"))
                {
                    using(var compressed=await request.Content.ReadAsStreamAsync())
                    using(var gzip=new GZipStream(compressed,CompressionMode.Decompress))
                    using(var reader=new StreamReader(gzip))LastBatch=reader.ReadToEnd();
                }
                else LastBatch=await request.Content.ReadAsStringAsync();
                var batch=NewtonsoftJsonSerializer.Instance.Deserialize<BatchUpdateSpreadsheetRequest>(LastBatch);
                foreach(var change in batch.Requests.Where(r=>r.UpdateCells!=null))
                {
                    var update=change.UpdateCells;int row=update.Start.RowIndex.Value,column=update.Start.ColumnIndex.Value;
                    while(Rows.Count<=row)Rows.Add(Row("","","","",""));
                    Rows[row].Values[column].UserEnteredValue=update.Rows[0].Values[0].UserEnteredValue;
                    Rows[row].Values[column].Note=update.Rows[0].Values[0].Note;
                }
                if(LoseWriteResponse)throw new HttpRequestException("simulated lost response after server commit");
                return Json(new BatchUpdateSpreadsheetResponse{SpreadsheetId="test-only"});
            }
            private static SheetProperties Properties()=>new SheetProperties{SheetId=0,GridProperties=new GridProperties{RowCount=10,ColumnCount=5}};
            private static HttpResponseMessage Json(object value)=>new HttpResponseMessage(HttpStatusCode.OK)
            {Content=new StringContent(NewtonsoftJsonSerializer.Instance.Serialize(value),System.Text.Encoding.UTF8,"application/json")};
        }
        [OneTimeSetUp]public void SetupCollection()
        {
            folder="Assets/PackageTests/EasyLocalization/GoogleTests/"+Guid.NewGuid().ToString("N");
            mapping=LocalizationSetup.Create(folder,"en",new[]{"ru"},"HTTP Test "+Guid.NewGuid().ToString("N"));mapping.spreadsheetId="test-only";
        }
        [SetUp]public void Setup()
        {
            handler=new FakeHandler();
            service=new SheetsService(new BaseClientService.Initializer{HttpClientFactory=new Factory(handler),ApplicationName="EasyLocalization Tests",DefaultExponentialBackOffPolicy=ExponentialBackOffPolicy.None});
            transport=new GoogleSheetsTransport(service,mapping);
        }
        [TearDown]public void Dispose()=>service.Dispose();
        [OneTimeTearDown]public void Cleanup()=>AssetDatabase.DeleteAsset(folder);

        [Test]public async Task ActualSdkPreservesLargeStringIdNotesAndUnmappedCells()
        {
            var before=await transport.ReadAsync(default);Assert.That(before.rows[0].id,Is.EqualTo(LargeId));
            var result=before.Copy();result.rows[0].key="renamed";result.rows[0].cells[0].text="=literal, not a formula";
            await transport.WriteAsync(before,result,default);
            Assert.That(handler.Writes,Is.EqualTo(1));Assert.That(handler.LastBatch,Does.Contain(LargeId.ToString()));
            Assert.That(handler.Rows[1].Values[2].UserEnteredValue.FormulaValue,Is.Null);
            Assert.That(handler.Rows[1].Values[2].Note,Does.Contain("Translator's note"));
            Assert.That(handler.Rows[1].Values[4].UserEnteredValue.StringValue,Is.EqualTo("KEEP"));
            Assert.That((await transport.ReadAsync(default)).Fingerprint(),Is.EqualTo(result.Fingerprint()));
        }
        [Test]public async Task NumericIdsBeyondExactPrecisionAreRejected()
        {
            handler.Rows[1].Values[0].UserEnteredValue=new ExtendedValue{NumberValue=9007199254740992d};
            await Throws<InvalidOperationException>(async()=>await transport.ReadAsync(default));Assert.That(handler.Writes,Is.Zero);
        }
        [Test]public async Task DuplicateRemoteIdsAreRejectedBeforeWriting()
        {
            handler.Rows.Add(handler.Rows[1]);await Throws<InvalidOperationException>(async()=>await transport.ReadAsync(default));Assert.That(handler.Writes,Is.Zero);
        }
        [Test]public async Task TransientReadUsesBoundedRetry()
        {
            handler.MetadataFailures=2;var snapshot=await transport.ReadAsync(default);
            Assert.That(snapshot.rows.Count,Is.EqualTo(1));Assert.That(handler.MetadataCalls,Is.EqualTo(3));
        }
        [Test]public async Task UncertainWriteIsNotAutomaticallyRepeated()
        {
            var before=await transport.ReadAsync(default);var result=before.Copy();result.rows[0].key="changed";handler.LoseWriteResponse=true;
            await Throws<HttpRequestException>(async()=>await transport.WriteAsync(before,result,default));
            Assert.That(handler.Writes,Is.EqualTo(1));
            Assert.That((await transport.ReadAsync(default)).Fingerprint(),Is.EqualTo(result.Fingerprint()));
        }
        [Test]public async Task CancelledReadDoesNotReachHttp()
        {
            using(var source=new CancellationTokenSource())
            {source.Cancel();await Throws<OperationCanceledException>(async()=>await transport.ReadAsync(source.Token));}
            Assert.That(handler.MetadataCalls,Is.Zero);
        }
        [Test]public async Task MappingChangeDuringRequestCannotRedirectTheOperation()
        {
            handler.OnMetadata=()=>mapping.sheetId=123;
            try
            {
                await Throws<InvalidOperationException>(async()=>await transport.ReadAsync(default));
                Assert.That(handler.Writes,Is.Zero);
            }
            finally { mapping.sheetId=0; }
        }
        private static async Task Throws<T>(Func<Task> operation) where T:Exception
        {
            Exception caught=null;
            try{await operation();}catch(Exception exception){caught=exception;}
            Assert.That(caught,Is.InstanceOf<T>());
        }
    }
}
