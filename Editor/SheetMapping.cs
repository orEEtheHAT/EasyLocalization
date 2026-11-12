using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.Google;
using UnityEditor.Localization.Plugins.Google.Columns;
using UnityEngine;

namespace EasyLocalization.Editor
{
    public enum SheetConnection { GoogleOAuth, PublishedCsv }
    [Serializable]
    public sealed class LanguageColumn
    {
        [Tooltip("Locale code, for example en, ru or pt-BR. Must match a table in the collection.")]
        public string languageCode;
        [Min(1), Tooltip("Column number: A = 1, B = 2, C = 3.")]
        public int column;
    }

    [CreateAssetMenu(menuName = "EasyLocalization/Google Sheet Mapping", fileName = "Sheet Mapping")]
    public sealed class SheetMapping : ScriptableObject
    {
        public StringTableCollection collection;
        public SheetConnection connection;
        [Tooltip("CSV export link for a sheet shared as Anyone with the link / Viewer, or a published CSV link. Download only.")]
        public string publishedCsvUrl;
        [Tooltip("The part between /d/ and /edit in the Google Sheets URL.")]
        public string spreadsheetId;
        [Tooltip("The numeric gid from the tab URL, not its position.")]
        public int sheetId;
        [Min(1)] public int headerRow = 1;
        [Min(0), Tooltip("ID column, A = 1. Use 0 to store IDs in Key cell notes, as in Unity's KeyColumn.")]
        public int idColumn = 1;
        [Min(1)] public int keyColumn = 2;
        public List<LanguageColumn> languages = new List<LanguageColumn>();
        [Min(10), Tooltip("Maximum data rows read in one request. Increase if the sheet reaches this limit.")]
        public int maximumRows = 10000;
        [HideInInspector] public SyncSnapshot previousLocal;
        [HideInInspector] public SyncSnapshot previousRemote;
        [HideInInspector] public string baselineMapping;

        public void SetSpreadsheetUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "docs.google.com")
                throw new ArgumentException("Copy the Google Sheets edit link, including #gid for the desired tab.");
            var match = System.Text.RegularExpressions.Regex.Match(uri.AbsolutePath, @"^/spreadsheets/d/([A-Za-z0-9_-]+)/");
            if (!match.Success || match.Groups[1].Value == "e") throw new ArgumentException("Use the ordinary Google Sheets edit link here. A published CSV link goes in Advanced > Published CSV URL.");
            var tab = System.Text.RegularExpressions.Regex.Match(url, @"[?#&]gid=(\d+)");
            if (!tab.Success || !int.TryParse(tab.Groups[1].Value, out var gid)) throw new ArgumentException("Select a tab and copy its URL including #gid=….");
            spreadsheetId = match.Groups[1].Value; sheetId = gid;
            publishedCsvUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv&gid={sheetId}";
            EditorUtility.SetDirty(this);
        }

        public string Fingerprint() => SyncSnapshot.Hash(JsonUtility.ToJson(new MappingIdentity
        {
            collectionGuid = collection ? collection.SharedData.TableCollectionNameGuid.ToString() : "",
            connection = connection, publishedCsvUrl = publishedCsvUrl,
            spreadsheetId = spreadsheetId, sheetId = sheetId, headerRow = headerRow,
            idColumn = idColumn, keyColumn = keyColumn, maximumRows = maximumRows, languages = languages
        }));

        [Serializable] private class MappingIdentity
        {
            public string collectionGuid, spreadsheetId;
            public SheetConnection connection;
            public string publishedCsvUrl;
            public int sheetId, headerRow, idColumn, keyColumn, maximumRows;
            public List<LanguageColumn> languages;
        }

        public void Validate()
        {
            if (!collection) throw new InvalidOperationException("Assign String Table Collection in the mapping.");
            if (connection == SheetConnection.GoogleOAuth && (string.IsNullOrWhiteSpace(spreadsheetId) || spreadsheetId.Contains("/")))
                throw new InvalidOperationException("Spreadsheet ID must be the text between /d/ and /edit, not the full URL.");
            if (connection == SheetConnection.PublishedCsv)
            {
                if (idColumn == 0) throw new InvalidOperationException("CSV cannot carry cell notes. Set ID Column to a separate plain-text column.");
                if (!Uri.TryCreate(publishedCsvUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "docs.google.com" || !uri.AbsolutePath.StartsWith("/spreadsheets/"))
                    throw new InvalidOperationException("Paste the HTTPS CSV publication link from docs.google.com/spreadsheets/.");
            }
            if (headerRow < 1 || keyColumn < 1 || idColumn < 0 || sheetId < 0 || maximumRows < 10)
                throw new InvalidOperationException("Check Header Row, Key Column, ID Column, Sheet ID and Maximum Rows.");
            var columns = new HashSet<int> { keyColumn };
            if (idColumn > 0 && !columns.Add(idColumn)) throw new InvalidOperationException("ID and Key columns must be different.");
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (languages.Count == 0) throw new InvalidOperationException("Add at least one language column.");
            foreach (var language in languages)
            {
                if (string.IsNullOrWhiteSpace(language.languageCode) || !codes.Add(language.languageCode) || language.column < 1 || !columns.Add(language.column))
                    throw new InvalidOperationException("Each language needs a unique code and column; columns cannot overlap ID or Key.");
                if (!collection.GetTable(language.languageCode))
                    throw new InvalidOperationException($"Collection has no '{language.languageCode}' table. Add this language in First setup or correct the mapping.");
            }
        }

        /// <summary>Adopts only the standard key/ID/locale columns; does not change the existing extension.</summary>
        public bool AdoptUnityExtension()
        {
            var extension = collection?.Extensions.OfType<GoogleSheetsExtension>().FirstOrDefault();
            if (extension == null) return false;
            var key = extension.Columns.OfType<KeyColumn>().FirstOrDefault();
            if (key == null) throw new InvalidOperationException("The extension needs a standard Key column.");
            spreadsheetId = extension.SpreadsheetId;
            sheetId = extension.SheetId;
            keyColumn = key.ColumnIndex + 1;
            idColumn = 0; // Standard Unity KeyColumn stores the stable ID in its cell note.
            languages = extension.Columns.OfType<LocaleColumn>().Select(c => new LanguageColumn
            { languageCode = c.LocaleIdentifier.Code, column = c.ColumnIndex + 1 }).ToList();
            EditorUtility.SetDirty(this);
            return true;
        }
    }
}
