using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using UnityEngine.Localization.Tables;

namespace EasyLocalization.Editor
{
    /// <summary>Editor-only, bounded Google Sheets batch reads/writes. Never appends by key or retries an uncertain write.</summary>
    public sealed class GoogleSheetsTransport : ITranslationTransport
    {
        private const string Missing = "[EasyLocalization:missing]";
        private const string Smart = "[EasyLocalization:smart]";
        private const string Plain = "[EasyLocalization:plain]";
        private readonly SheetsService service;
        private readonly SheetMapping mapping;
        private readonly string mappingIdentity;
        private readonly Dictionary<long, int> positions = new Dictionary<long, int>();
        private IList<RowData> grid = new List<RowData>();
        private int gridStart;
        private int capacity;
        private int nextRow;
        public bool CanWrite => true;

        public GoogleSheetsTransport(SheetsService service, SheetMapping mapping)
        {
            this.service = service; this.mapping = mapping; mappingIdentity = mapping.Fingerprint();
        }

        private void CheckMapping()
        {
            if (mapping.Fingerprint() != mappingIdentity)
                throw new InvalidOperationException("The collection mapping changed during this operation. Preview again before applying; no further request was sent.");
        }

        public async Task<SyncSnapshot> ReadAsync(CancellationToken cancellation)
        {
            CheckMapping();
            int width = Math.Max(Math.Max(mapping.idColumn, mapping.keyColumn), mapping.languages.Max(c => c.column));
            var metadataRequest = service.Spreadsheets.Get(mapping.spreadsheetId);
            metadataRequest.Fields = "sheets.properties";
            var metadata = await ReadWithRetry(token => metadataRequest.ExecuteAsync(token), cancellation);
            CheckMapping();
            var properties = metadata.Sheets?.FirstOrDefault(s => s.Properties.SheetId == mapping.sheetId)?.Properties;
            if (properties == null) throw new InvalidOperationException("Sheet ID was not found. Copy gid from the desired tab URL.");
            capacity = properties.GridProperties.RowCount ?? 0;
            if (capacity > mapping.headerRow + mapping.maximumRows)
                throw new InvalidOperationException($"The tab has {capacity} rows. Increase Maximum Rows to at least {capacity - mapping.headerRow} so no rows can be missed.");
            if (width > (properties.GridProperties.ColumnCount ?? 0))
                throw new InvalidOperationException("A mapped column is outside the tab. Add the column in Google Sheets or correct the mapping.");
            var request = service.Spreadsheets.GetByDataFilter(new GetSpreadsheetByDataFilterRequest
            {
                IncludeGridData = true,
                DataFilters = new List<DataFilter> { new DataFilter { GridRange = new GridRange
                { SheetId = mapping.sheetId, StartRowIndex = mapping.headerRow - 1, EndRowIndex = capacity, EndColumnIndex = width } } }
            }, mapping.spreadsheetId);
            request.Fields = "sheets(properties(sheetId,gridProperties),data(startRow,rowData(values(userEnteredValue,formattedValue,note))))";
            var spreadsheet = await ReadWithRetry(token => request.ExecuteAsync(token), cancellation);
            CheckMapping();
            var sheet = spreadsheet.Sheets?.FirstOrDefault(s => s.Properties.SheetId == mapping.sheetId);
            if (sheet == null) throw new InvalidOperationException("Sheet ID was not found. Copy the numeric gid from the desired Google tab URL.");
            capacity = sheet.Properties.GridProperties.RowCount ?? 0;
            var data = sheet.Data?.FirstOrDefault();
            grid = data?.RowData ?? new List<RowData>();
            gridStart = data?.StartRow ?? mapping.headerRow - 1;
            positions.Clear(); nextRow = mapping.headerRow;
            var result = new SyncSnapshot();
            if (grid.Count == 0) return result;
            foreach (var language in mapping.languages)
            {
                string heading = Value(Cell(mapping.headerRow - 1, language.column));
                string nativeHeading = new UnityEngine.Localization.LocaleIdentifier(language.languageCode).ToString();
                if (!string.IsNullOrEmpty(heading) && heading != language.languageCode && heading != nativeHeading)
                    throw new InvalidOperationException($"Column {language.column} header is '{heading}', expected '{language.languageCode}'. Fix Languages in the mapping.");
            }
            for (int index = mapping.headerRow; index < gridStart + grid.Count; index++)
            {
                var keyCell = Cell(index, mapping.keyColumn);
                string key = Value(keyCell);
                string idText = mapping.idColumn == 0 ? keyCell.Note : Value(Cell(index, mapping.idColumn));
                bool hasValues = mapping.languages.Any(c => !string.IsNullOrEmpty(Value(Cell(index, c.column))));
                if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(idText) && !hasValues) continue;
                if (index >= mapping.headerRow + mapping.maximumRows)
                    throw new InvalidOperationException("Read limit reached. Increase Maximum Rows and preview again; no partial sheet will be applied.");
                if (!long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                    throw new InvalidOperationException($"Row {index + 1}: missing or invalid stable ID. Create the row in Unity and send it, or enter a unique positive Int64 ID as plain text.");
                if (positions.ContainsKey(id)) throw new InvalidOperationException($"Row {index + 1}: duplicate ID {id}. Restore the original unique IDs.");
                positions.Add(id, index); nextRow = index + 1;
                var row = new TranslationRow { id = id, key = key };
                foreach (var language in mapping.languages)
                {
                    var cell = Cell(index, language.column);
                    var local = (mapping.collection.GetTable(language.languageCode) as StringTable)?.GetEntry(id);
                    string note = cell.Note ?? "";
                    row.cells.Add(new TranslationCell { language = language.languageCode, text = Value(cell),
                        present = !note.Contains(Missing), smart = note.Contains(Smart) || (!note.Contains(Plain) && (local?.IsSmart ?? false)) });
                }
                result.rows.Add(row);
            }
            return result;
        }

        public async Task WriteAsync(SyncSnapshot expected, SyncSnapshot result, CancellationToken cancellation)
        {
            CheckMapping();
            // Re-read inside the transport so physical row positions are fresh even after reordering.
            var actual = await ReadAsync(cancellation);
            CheckMapping();
            if (actual.Fingerprint() != expected.Fingerprint()) throw new InvalidOperationException("Google changed before the write. Preview again.");
            var requests = new List<Request>();
            var before = expected.rows.ToDictionary(r => r.id);
            var after = result.rows.ToDictionary(r => r.id);
            SetCell(requests, mapping.headerRow - 1, mapping.keyColumn, "Key", Cell(mapping.headerRow - 1, mapping.keyColumn).Note);
            if (mapping.idColumn > 0) SetCell(requests, mapping.headerRow - 1, mapping.idColumn, "ID", Cell(mapping.headerRow - 1, mapping.idColumn).Note);
            foreach (var language in mapping.languages) SetCell(requests, mapping.headerRow - 1, language.column, language.languageCode, Cell(mapping.headerRow - 1, language.column).Note);
            foreach (var id in before.Keys.Union(after.Keys))
            {
                before.TryGetValue(id, out var old); after.TryGetValue(id, out var row);
                if (old != null && row != null && old.Fingerprint() == row.Fingerprint()) continue;
                int index = positions.TryGetValue(id, out var found) ? found : nextRow++;
                if (index >= mapping.headerRow + mapping.maximumRows) throw new InvalidOperationException("Write exceeds Maximum Rows. Increase the mapping limit.");
                SetCell(requests, index, mapping.keyColumn, row?.key ?? "", mapping.idColumn == 0 ? row?.id.ToString(CultureInfo.InvariantCulture) ?? "" : Cell(index, mapping.keyColumn).Note);
                if (mapping.idColumn > 0) SetCell(requests, index, mapping.idColumn, row?.id.ToString(CultureInfo.InvariantCulture) ?? "", Cell(index, mapping.idColumn).Note);
                foreach (var language in mapping.languages)
                {
                    var value = row?.cells.FirstOrDefault(c => c.language == language.languageCode);
                    string note = (Cell(index, language.column).Note ?? "").Replace(Missing, "").Replace(Smart, "").Replace(Plain, "").Trim();
                    if (value != null) note = (note + "\n" + (value.present ? "" : Missing) + (value.smart ? Smart : Plain)).Trim();
                    SetCell(requests, index, language.column, value?.text ?? "", note);
                }
            }
            if (nextRow > capacity) requests.Insert(0, new Request { AppendDimension = new AppendDimensionRequest
            { SheetId = mapping.sheetId, Dimension = "ROWS", Length = nextRow - capacity } });
            if (requests.Count > 20000) throw new InvalidOperationException("This batch exceeds 20,000 changed cells. Split the collection or synchronize smaller changes.");
            cancellation.ThrowIfCancellationRequested();
            await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, mapping.spreadsheetId).ExecuteAsync(cancellation);
        }

        private CellData Cell(int row, int column)
        {
            int index = row - gridStart;
            var values = index >= 0 && index < grid.Count ? grid[index].Values : null;
            return values != null && column > 0 && column <= values.Count ? values[column - 1] : new CellData();
        }

        private static string Value(CellData cell)
        {
            var value = cell.UserEnteredValue;
            if (value?.FormulaValue != null) throw new InvalidOperationException("A mapped cell contains a formula. Use plain text in ID, Key and translation columns.");
            if (value?.NumberValue != null)
            {
                if (Math.Abs(value.NumberValue.Value) > 9007199254740991d)
                    throw new InvalidOperationException("A numeric cell exceeds exact spreadsheet integer precision. Restore the original ID and format the ID column as Plain text.");
                return value.NumberValue.Value.ToString("R", CultureInfo.InvariantCulture);
            }
            return value?.StringValue ?? cell.FormattedValue ?? "";
        }

        private void SetCell(List<Request> requests, int row, int column, string value, string note)
        {
            requests.Add(new Request { UpdateCells = new UpdateCellsRequest
            {
                Start = new GridCoordinate { SheetId = mapping.sheetId, RowIndex = row, ColumnIndex = column - 1 },
                Fields = "userEnteredValue,note",
                Rows = new List<RowData> { new RowData { Values = new List<CellData> { new CellData
                { UserEnteredValue = new ExtendedValue { StringValue = value }, Note = note ?? "" } } } }
            } });
        }

        private static async Task<T> ReadWithRetry<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellation)
        {
            for (int attempt = 0; ; attempt++)
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    try { return await request(timeout.Token); }
                    catch (Exception exception) when (attempt < 2 && !cancellation.IsCancellationRequested &&
                        (exception is HttpRequestException || exception is OperationCanceledException ||
                         exception is GoogleApiException google && ((int)google.HttpStatusCode == 429 || (int)google.HttpStatusCode >= 500)))
                    { await Task.Delay(500 * (1 << attempt), cancellation); }
                }
            }
        }
    }
}
