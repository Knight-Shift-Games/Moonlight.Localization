using System.ComponentModel;
using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;
using System.IO;

namespace Moonlight.Localization
{
    // This ScriptableObject holds the persistent settings for the translator window.
    public class TranslatorSettings : ScriptableObject
    {
        // The path where the settings asset will be saved.
        internal const string SettingsPath = "Assets/App/Editor/TranslatorSettings.asset";

        [Title("Source File")]
        [Sirenix.OdinInspector.FilePath(Extensions = ".tsv,.txt", RequireExistingPath = true)]
        [SerializeField]
        internal string sourceTsvPath = "";

        [Title("API Settings")]
        [SerializeField, PasswordPropertyText]
        internal string apiKey = "";
        
        [SerializeField]
        internal string sourceLanguageFullName = "English";

        [Title("Custom Instructions for AI")]
        [SerializeField, TextArea(3, 5)]
        [InfoBox("Add rules for the AI translator. For example, instruct it to ignore text within curly braces {like_this}.")]
        internal string customInstructions = "Do not translate any text found inside curly braces, such as {playerName} or {score}. Keep the text inside the braces exactly as it is in the final translation.";

        [Title("Save Options")]
        [SerializeField]
        internal bool overwriteOriginalFile = false;

        // A helper method to load the settings asset, or create it if it doesn't exist.
        internal static TranslatorSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TranslatorSettings>(SettingsPath);
            if (settings == null)
            {
                settings = CreateInstance<TranslatorSettings>();

                string directory = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Created new TranslatorSettings asset at: " + SettingsPath);
            }
            return settings;
        }
    }
}
