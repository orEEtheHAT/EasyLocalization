using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Util.Store;
using UnityEditor.Localization.Plugins.Google;
using UnityEngine;

namespace EasyLocalization.Editor
{
    /// <summary>Uses the official Localization OAuth provider without creating a credentials asset.</summary>
    public static class GoogleConnection
    {
        public static string LocalFolder => Path.GetFullPath("Library/EasyLocalization");
        public static string CredentialsPath => Path.Combine(LocalFolder, "oauth-client.json");
        public static bool HasCredentials => File.Exists(CredentialsPath);

        public static void ImportCredentials(string path)
        {
            var json = File.ReadAllText(path);
            var provider = ScriptableObject.CreateInstance<SheetsServiceProvider>();
            try
            {
                provider.SetOAuthCredentials(json); // Validates the official Desktop OAuth JSON.
                Directory.CreateDirectory(LocalFolder);
                File.WriteAllText(CredentialsPath, json);
            }
            finally { UnityEngine.Object.DestroyImmediate(provider); }
        }

        public static void ImportProvider(SheetsServiceProvider existing)
        {
            if (!existing || existing.Authentication != AuthenticationType.OAuth)
                throw new InvalidOperationException("Select an existing OAuth Sheets Service Provider.");
            var data = new Credentials { installed = new Installed { client_id = existing.ClientId, client_secret = existing.ClientSecret } };
            Directory.CreateDirectory(LocalFolder);
            File.WriteAllText(CredentialsPath, JsonUtility.ToJson(data));
        }

        [Serializable] private class Credentials { public Installed installed; }
        [Serializable] private class Installed { public string client_id; public string client_secret; }

        public static async Task<SheetsService> ConnectAsync(CancellationToken cancellation)
        {
            if (!HasCredentials) throw new InvalidOperationException("Click Import OAuth JSON, then Connect Google account. Credentials stay in Library/EasyLocalization, outside Git and builds.");
            var provider = ScriptableObject.CreateInstance<SheetsServiceProvider>();
            provider.hideFlags = HideFlags.HideAndDontSave;
            provider.name = "EasyLocalization";
            try
            {
                provider.SetOAuthCredentials(File.ReadAllText(CredentialsPath));
                provider.DataStore = new FileDataStore(Path.Combine(LocalFolder, "Tokens"), true);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));
                    var credentials = await provider.AuthorizeOAuthAsync(timeout.Token);
                    var service = new SheetsService(new BaseClientService.Initializer
                    { HttpClientInitializer = credentials, ApplicationName = "EasyLocalization Unity Editor",
                      DefaultExponentialBackOffPolicy = Google.Apis.Http.ExponentialBackOffPolicy.None });
                    service.HttpClient.Timeout = TimeSpan.FromSeconds(45);
                    return service;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(provider); }
        }
    }
}
