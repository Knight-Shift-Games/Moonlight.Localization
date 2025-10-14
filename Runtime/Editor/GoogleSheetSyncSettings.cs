using System.ComponentModel;
using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;
using System.IO;

namespace Moonlight.Localization
{
    // This ScriptableObject holds the persistent settings for the Google Sheet sync window.
    public class GoogleSheetSyncSettings : ScriptableObject
    {
        // The path where the settings asset will be saved.
        internal const string SettingsPath = "Assets/App/Editor/GoogleSheetSyncSettings.asset";

        [Title("Sync Configuration")]
        [InfoBox("The local TSV file that will be synced with the Google Sheet.")]
        [Sirenix.OdinInspector.FilePath(Extensions = ".tsv", RequireExistingPath = true)]
        [SerializeField]
        internal string localTsvPath = "";

        [InfoBox("Paste the full URL of your Google Sheet here. It must be set to 'Anyone with the link can view'.")]
        [SerializeField]
        internal string googleSheetUrl = "";

        [Title("Google API Credentials")]
        [InfoBox("Get these values from your project on the Google Cloud Platform.", InfoMessageType.None)]
        [SerializeField]
        internal string googleApiClientId = "";

        [SerializeField, PasswordPropertyText]
        internal string googleApiClientSecret = "";
        
        [Title("Automatic Checker")]
        [SerializeField]
        internal bool autoCheckForChanges = true;
        
        [SerializeField, Range(1, 120)]
        [InfoBox("How often (in minutes) to check the Google Sheet for changes.")]
        internal int checkIntervalMinutes = 5;
        
        // --- Internal State (Managed by the tool) ---
        [HideInInspector] public string lastKnownSheetHash = "";
        [HideInInspector] public long lastCheckTicks = 0;
        [HideInInspector] public string googleApiRefreshToken = ""; // This is stored after you authenticate once.

        internal static GoogleSheetSyncSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GoogleSheetSyncSettings>(SettingsPath);
            if (settings == null)
            {
                settings = CreateInstance<GoogleSheetSyncSettings>();
                string directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }
    }
}
