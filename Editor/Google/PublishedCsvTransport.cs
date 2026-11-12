using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Localization.Tables;

namespace EasyLocalization.Editor
{
    public sealed class PublishedCsvTransport : ITranslationTransport
    {
        private readonly SheetMapping mapping;
        public bool CanWrite => false;
        public PublishedCsvTransport(SheetMapping mapping) => this.mapping = mapping;

        public async Task<SyncSnapshot> ReadAsync(CancellationToken cancellation)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return ReadCsv(await DownloadAsync(cancellation), mapping); }
                catch (Exception exception) when (attempt < 2 && !cancellation.IsCancellationRequested &&
                    (exception is HttpRequestException || exception is OperationCanceledException))
                { await Task.Delay(500 * (1 << attempt), cancellation); }
            }
        }

        private async Task<string> DownloadAsync(CancellationToken cancellation)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) })
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                using (var response = await client.GetAsync(mapping.publishedCsvUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                using (timeout.Token.Register(response.Dispose))
                {
                    try
                    {
                        response.EnsureSuccessStatusCode();
                        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024);
                        timeout.Token.ThrowIfCancellationRequested();
                        var text = await response.Content.ReadAsStringAsync();
                        if (text.TrimStart().StartsWith("<")) throw new InvalidOperationException("Google returned a webpage. Enable Anyone with the link / Viewer and connect the tab URL, or use a published CSV link.");
                        return text;
                    }
                    catch (Exception) when (timeout.IsCancellationRequested) { throw new OperationCanceledException(timeout.Token); }
                }
            }
        }

        public Task WriteAsync(SyncSnapshot expected, SyncSnapshot result, CancellationToken cancellation) =>
            throw new InvalidOperationException("Published CSV is download-only. Choose Google OAuth to send translations.");

        public static SyncSnapshot ReadCsv(string text, SheetMapping mapping)
        {
            var grid = Parse(text);
            string Cell(int row, int column) => row < grid.Count && column > 0 && column <= grid[row].Count ? grid[row][column - 1] : "";
            if (grid.Count < mapping.headerRow) throw new InvalidOperationException("CSV has no configured header row.");
            foreach (var language in mapping.languages)
                if (Cell(mapping.headerRow - 1, language.column) != language.languageCode)
                    throw new InvalidOperationException($"CSV column {language.column} must have header '{language.languageCode}'.");
            var snapshot = new SyncSnapshot();
            for (int i = mapping.headerRow; i < grid.Count; i++)
            {
                if (grid[i].TrueForAll(string.IsNullOrEmpty)) continue;
                if (snapshot.rows.Count >= mapping.maximumRows) throw new InvalidOperationException("CSV exceeds Maximum Rows. Increase the limit.");
                if (!long.TryParse(Cell(i, mapping.idColumn), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                    throw new InvalidOperationException($"CSV row {i + 1}: supply a stable positive ID as plain text.");
                var row = new TranslationRow { id = id, key = Cell(i, mapping.keyColumn) };
                foreach (var language in mapping.languages)
                    row.cells.Add(new TranslationCell { language = language.languageCode, text = Cell(i, language.column),
                        present = language.column <= grid[i].Count,
                        smart = (mapping.collection.GetTable(language.languageCode) as StringTable)?.GetEntry(id)?.IsSmart ?? false });
                snapshot.rows.Add(row);
            }
            return snapshot;
        }

        // RFC 4180: quoted commas, escaped quotes, CRLF/LF and multiline translations.
        public static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder();
            bool quoted = false, closed = false;
            text = text.TrimStart('\uFEFF');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c != '"') { cell.Append(c); continue; }
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; continue; }
                    quoted = false; closed = true; continue;
                }
                if (c == '"' && cell.Length == 0 && !closed) { quoted = true; continue; }
                if (c == ',' || c == '\r' || c == '\n')
                {
                    row.Add(cell.ToString()); cell.Clear(); closed = false;
                    if (c != ',')
                    {
                        rows.Add(row); row = new List<string>();
                        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    }
                    continue;
                }
                if (closed || c == '"') throw new FormatException("Invalid CSV quoting.");
                cell.Append(c);
            }
            if (quoted) throw new FormatException("CSV ends inside a quoted translation.");
            if (cell.Length > 0 || row.Count > 0 || closed) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
