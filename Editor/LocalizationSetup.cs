using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace EasyLocalization.Editor
{
    public static class LocalizationSetup
    {
        public static SheetMapping Create(string folder, string primaryLanguage, IEnumerable<string> additionalLanguages,
            string collectionName, StringTableCollection existing = null)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Split('/').Any(p => p == ".." || p == "."))
                throw new ArgumentException("Choose a folder inside Assets, for example Assets/Localization.");
            var codes = new[] { primaryLanguage }.Concat(additionalLanguages ?? Array.Empty<string>())
                .Select(c => CultureInfo.GetCultureInfo(c.Trim()).Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (codes.Any(string.IsNullOrEmpty)) throw new ArgumentException("Enter a language code such as en or ru.");
            Directory.CreateDirectory(folder + "/Locales"); Directory.CreateDirectory(folder + "/Resources");
            AssetDatabase.Refresh();
            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            bool createdSettings = !settings;
            if (createdSettings)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(folder + "/Localization Settings.asset"));
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }
            var locales = new List<Locale>();
            foreach (string code in codes)
            {
                var locale = LocalizationEditorSettings.GetLocales().FirstOrDefault(l => l.Identifier.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                if (!locale)
                {
                    locale = Locale.CreateLocale(code);
                    AssetDatabase.CreateAsset(locale, AssetDatabase.GenerateUniqueAssetPath(folder + "/Locales/" + code + ".asset"));
                    LocalizationEditorSettings.AddLocale(locale, true);
                }
                locales.Add(locale);
            }
            if (createdSettings) { LocalizationSettings.ProjectLocale = locales[0]; EditorUtility.SetDirty(settings); }
            if (!existing && string.IsNullOrWhiteSpace(collectionName)) throw new ArgumentException("Enter a collection name or assign an existing collection.");
            var collection = existing ? existing : LocalizationEditorSettings.GetStringTableCollection(collectionName);
            if (!collection) collection = LocalizationEditorSettings.CreateStringTableCollection(collectionName, folder + "/Tables", locales);
            foreach (var locale in locales) if (!collection.GetTable(locale.Identifier)) collection.AddNewTable(locale.Identifier);
            string configPath = folder + "/Resources/EasyLocalizationConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(configPath);
            if (!config)
            {
                // There must only be one auto-loaded Resources configuration in a project.
                var candidates = AssetDatabase.FindAssets("t:LocalizationConfig").Select(AssetDatabase.GUIDToAssetPath);
                config = candidates.Where(p => p.EndsWith("/Resources/EasyLocalizationConfig.asset", StringComparison.Ordinal))
                    .Select(AssetDatabase.LoadAssetAtPath<LocalizationConfig>).FirstOrDefault();
            }
            if (!config)
            {
                config = ScriptableObject.CreateInstance<LocalizationConfig>(); config.fallbackLanguage = codes[0];
                AssetDatabase.CreateAsset(config, configPath);
            }
            string mappingPath = folder + "/" + collection.name + " Mapping.asset";
            var mapping = AssetDatabase.LoadAssetAtPath<SheetMapping>(mappingPath);
            if (!mapping)
            {
                mapping = ScriptableObject.CreateInstance<SheetMapping>(); mapping.collection = collection;
                mapping.languages = locales.Select((l, i) => new LanguageColumn { languageCode = l.Identifier.Code, column = i + 3 }).ToList();
                AssetDatabase.CreateAsset(mapping, AssetDatabase.GenerateUniqueAssetPath(mappingPath));
            }
            EditorUtility.SetDirty(collection); AssetDatabase.SaveAssets();
            return mapping;
        }
    }
}
