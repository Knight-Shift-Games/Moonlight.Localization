using System.IO;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace Moonlight.Localization
{
    [Searchable, CreateAssetMenu(menuName = "Moonlight/Localization/Config", order = -100000), GlobalConfig("Assets/App/Resources/")]
    public class LocalizationConfig : GlobalConfig<LocalizationConfig>
    {
        [field: SerializeField, OnValueChanged("ValueChanged"), ValueDropdown("@Language.SupportedLanguages")]
        public string CurrentLanguage { get; set; } = Language.English;

        private string defaultLanguage = "en";

        [Header("Localization Data")]
        [SerializeField, FilePath(Extensions = ".tsv", RequireExistingPath = true)]
        private string localizationFilePath;

        private SerializedDictionary<string, string> localizedStrings = new();

        public string FilePath => localizationFilePath;
        
        private void OnEnable()
        {
            if (localizedStrings.Count == 0)
            {
                LoadLocalizationDataForLanguage(CurrentLanguage);
            }
        }

        private void ValueChanged()
        {
            LoadLocalizationDataForLanguage(CurrentLanguage);
        }
        
        /// <summary>
        /// Reads the TSV and populates the dictionary for ONLY the selected language.
        /// </summary>
        public void LoadLocalizationDataForLanguage(string languageToLoad)
        {
            // Check if the file path has been set in the inspector.
            if (string.IsNullOrEmpty(localizationFilePath))
            {
                Debug.LogError("Localization file path is not set in LocalizationConfig.");
                return;
            }
            
            // Check if the file actually exists at the given path.
            if (!File.Exists(localizationFilePath))
            {
                Debug.LogError($"Localization file not found at path: {localizationFilePath}");
                return;
            }

            // Clear out the old language's data.
            localizedStrings.Clear();
            
            // Read all text from the .tsv file directly.
            string tsvText = File.ReadAllText(localizationFilePath);
            StringReader reader = new StringReader(tsvText);

            // --- 1. Read the Header to find the correct column index for our language ---
            string header = reader.ReadLine();
            if (header == null) return;
            string[] languages = header.Split('\t');

            int languageIndex = -1;
            int defaultLanguageIndex = -1;

            for (int i = 0; i < languages.Length; i++)
            {
                // Use Trim() to remove any potential whitespace from the header cells
                if (languages[i].Trim() == languageToLoad)
                {
                    languageIndex = i;
                }

                if (languages[i].Trim() == defaultLanguage)
                {
                    defaultLanguageIndex = i;
                }
            }

            if (languageIndex == -1)
            {
                Debug.LogWarning(
                    $"Language '{languageToLoad}' not found in TSV file. Falling back to default '{defaultLanguage}'.");
                languageIndex = defaultLanguageIndex;
            }

            if (languageIndex == -1)
            {
                Debug.LogError("Default language not found in TSV file. Cannot load any text.");
                return;
            }

            // --- 2. Read each line and store only the text from our language's column ---
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] values = line.Split('\t');
                if (values.Length <= languageIndex) continue; // Skip malformed lines

                string key = values[0];
                string value = values[languageIndex];

                // Fallback: If the translation for the current language is empty, use the default language.
                if (string.IsNullOrEmpty(value) && defaultLanguageIndex != -1 && values.Length > defaultLanguageIndex)
                {
                    value = values[defaultLanguageIndex];
                }

                localizedStrings[key] = value;
            }
        }
        
        /// <summary>
        /// Gets the translated string for a given key.
        /// </summary>
        public string GetLocalizedString(string key, string defaultValue)
        {
            // If for some reason the dictionary is empty, try to load it.
            if (localizedStrings.Count == 0 && !string.IsNullOrEmpty(localizationFilePath))
            {
                LoadLocalizationDataForLanguage(CurrentLanguage);
            }
            
            if (localizedStrings.TryGetValue(key, out string value))
            {
                return value;
            }

            // TODO: Show this warning so we can add it to the db
            // Debug.LogWarning($"Localized string for key '{key}' not found in current language '{CurrentLanguage}'.");
            return defaultValue;
        }

        public bool DoesKeyExist(string key)
        {
            return localizedStrings.ContainsKey(key);
        }
    }
}