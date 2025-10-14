using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Moonlight.Localization
{
    [Serializable, InlineProperty, HideLabel, HideReferenceObjectPicker]
    public class L10nString
    {
        [SerializeField, OnValueChanged("ValueChanged"), HorizontalGroup("1", Title = "@$property.Parent.NiceName", Width = 50), 
         HideLabel, OnStateUpdate("StateUpdate"), OnInspectorInit("InspectorInit"), ValueDropdown("@Language.SupportedLanguages")] private string _lang;
        [SerializeField, HorizontalGroup("1"), HideLabel] private string _key;
        [SerializeField, TextArea(2, 10), HideLabel] private string _localizedValue;

        [SerializeField] private Dictionary<string, IValueGetter> Getters = new();

        public L10nString()
        {
            _lang = Language.English;
            this._key = Guid.NewGuid().ToString();
            _localizedValue = "";
        }
        
        public L10nString(string key)
        {
            _lang = Language.English;
            this._key = key;
            _localizedValue = LocalizationConfig.Instance.GetLocalizedString(_key, _localizedValue);
        }

        public L10nString(string key, string value)
        {
            this._key = key;
            this._localizedValue = value;
        }

        private bool _initialized;
        private void EnsureInitialized()
        {
            if (_initialized) return;

            // Safe to touch config here when actually used (inspector or runtime),
            // not during Unity's serialization pass.
            var cfg = LocalizationConfig.Instance;
            if (cfg != null)
            {
                if (string.IsNullOrEmpty(_lang))
                    _lang = cfg.CurrentLanguage;

                // If you want to prefill the cached value:
                _localizedValue = cfg.GetLocalizedString(_key, _localizedValue);
            }

            _initialized = true;
        }
        
        // This button will only appear if the key does not already exist in the localization file.
        [Button("Add to Sheet"), ShowIf("@!KeyExistsInSheet()"), GUIColor(0f, 1f, 0.6f)]
        public void AddToLocSheet()
        {
            var path = LocalizationConfig.Instance.FilePath;
            var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            var header = lines[0].Split('\t');
            var sourceLangIndex = Array.IndexOf(header, _lang);

            if (sourceLangIndex == -1)
            {
                EditorUtility.DisplayDialog("Error", $"Language '{_lang}' not found in the TSV header. Cannot add new entry.", "OK");
                return;
            }

            string[] newRow = new string[header.Length];
            newRow[0] = _key;
            for (int i = 1; i < header.Length; i++) newRow[i] = ""; // Fill with empty strings
            newRow[sourceLangIndex] = _localizedValue; // Set the source text

            string newLine = "\n" + string.Join("\t", newRow);
            File.AppendAllText(path, newLine, Encoding.UTF8);

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", $"Successfully added key '{_key}' to the localization sheet.", "OK");
            
            // Force the config to reload to reflect the change immediately.
            LocalizationConfig.Instance.LoadLocalizationDataForLanguage(_lang);
        }
        
        private void InspectorInit()
        {
            EnsureInitialized();
        }

        [Button, ShowIf("@_localizedValue != LocalizationConfig.Instance.GetLocalizedString(_key, _localizedValue)")]
        private void UpdateSourceText()
        {
            
        }

        [Button]
        private void ShowPreview()
        {
            Debug.Log(Localized());
        }
        
        private void ValueChanged()
        {
            EnsureInitialized();
            
            if (LocalizationConfig.Instance.CurrentLanguage != _lang)
            {
                LocalizationConfig.Instance.CurrentLanguage = _lang;
                LocalizationConfig.Instance.LoadLocalizationDataForLanguage(_lang);
            }
            
            _localizedValue = LocalizationConfig.Instance.GetLocalizedString(_key, _localizedValue);
        }
        
        private void StateUpdate()
        {
            EnsureInitialized();
            _lang = LocalizationConfig.Instance.CurrentLanguage;
            _localizedValue = LocalizationConfig.Instance.GetLocalizedString(_key, _localizedValue);
        }
        
        private bool KeyExistsInSheet()
        {
            return LocalizationConfig.Instance.DoesKeyExist(_key);
        }
        
        /// <summary>
        /// Retrieves the localized string from the LocalizationManager.
        /// </summary>
        /// <returns>The translated string for the current language.</returns>
        public string Localized()
        {
            // Return an empty string if the key is not set to avoid errors.
            if (string.IsNullOrEmpty(_key))
            {
                return "";
            }

            // Access the singleton instance of the LocalizationManager to get the string.
            // This is the central point of translation lookup.
            // var localizedString =  LocalizationConfig.Instance.GetLocalizedString(_key, "Not found");
            var localizedString = _localizedValue;
            foreach (var valueGetter in Getters)
            {
                localizedString = localizedString.Replace($"{{{valueGetter.Key}}}", valueGetter.Value.GetValue());
            }
            
            return localizedString;
        }

        /// <summary>
        /// Implicitly converts an L10nString to a string.
        /// This allows you to use an L10nString object directly where a string is expected,
        /// for example: myTextComponent.text = myL10nString;
        /// </summary>
        public static implicit operator string(L10nString l10nString)
        {
            return l10nString.Localized();
        }
    }
}