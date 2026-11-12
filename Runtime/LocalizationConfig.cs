using System;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace EasyLocalization
{
    /// <summary>Initial project configuration. The player's choice is kept in LanguageState.</summary>
    [CreateAssetMenu(menuName = "EasyLocalization/Project Configuration", fileName = "EasyLocalizationConfig")]
    public sealed class LocalizationConfig : ScriptableObject
    {
        [Tooltip("Stable locale code used when the requested language or translation is unavailable, for example en or pt-BR.")]
        public string fallbackLanguage = "en";
        [Tooltip("Optional tables to preload before InitializeAsync completes.")]
        public TableReference[] preloadTables = Array.Empty<TableReference>();
    }

    [Serializable]
    public struct LanguageState
    {
        public string languageCode;
        public LanguageState(string code) => languageCode = code;
    }
}
