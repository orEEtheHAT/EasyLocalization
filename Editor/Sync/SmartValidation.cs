using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.SmartFormat.Core.Parsing;
using UnityEngine.Localization.SmartFormat.Core.Settings;

namespace EasyLocalization.Editor
{
    public static class SmartValidation
    {
        public static void Validate(TranslationRow row, string side, List<string> errors)
        {
            HashSet<string> expected = null;
            string expectedLanguage = null;
            var formatter = LocalizationSettings.StringDatabase.SmartFormatter;
            var parser = formatter.Parser;
            var previous = parser.Settings.ParseErrorAction;
            try
            {
                parser.Settings.ParseErrorAction = ErrorAction.ThrowError;
                foreach (var cell in row.cells.Where(c => c.present && c.smart && !string.IsNullOrEmpty(c.text)))
                {
                    Format format = null;
                    try
                    {
                        format = parser.ParseFormat(cell.text, formatter.GetNotEmptyFormatterExtensionNames());
                        var selectors = new HashSet<string>(StringComparer.Ordinal);
                        Collect(format, selectors);
                        if (expected != null && !expected.SetEquals(selectors))
                            errors.Add($"{side}/{row.key}/{cell.language}: Smart String parameters differ from {expectedLanguage}. Expected: {string.Join(", ", expected)}; found: {string.Join(", ", selectors)}.");
                        expected = selectors;
                        expectedLanguage = cell.language;
                    }
                    catch (Exception exception) { errors.Add($"{side}/{row.key}/{cell.language}: invalid Smart String: {exception.Message}"); }
                    finally { format?.ReleaseToPool(); }
                }
            }
            finally { parser.Settings.ParseErrorAction = previous; }
        }

        private static void Collect(Format format, HashSet<string> selectors)
        {
            foreach (var item in format.Items)
                if (item is Placeholder placeholder)
                {
                    if (placeholder.Selectors.Count > 0)
                        selectors.Add(string.Join(".", placeholder.Selectors.Select(s => s.RawText)));
                    if (placeholder.Format != null) Collect(placeholder.Format, selectors);
                }
        }
    }
}
