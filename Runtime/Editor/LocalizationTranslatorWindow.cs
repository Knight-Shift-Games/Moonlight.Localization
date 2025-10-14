using System;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Unity.EditorCoroutines.Editor;
using UnityEngine.Networking;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System.Collections.Generic;
using System.Linq;

namespace Moonlight.Localization
{
    public class LocalizationTranslatorWindow : OdinEditorWindow
    {
        private const string HashColumnName = "sourceHash";

        // --- Editor Window Setup ---
        [MenuItem("Tools/Localization/TSV Translator")]
        public static void ShowWindow() => GetWindow<LocalizationTranslatorWindow>("TSV Translator");

        [ShowInInspector, InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Hidden)]
        private TranslatorSettings settings;

        // --- Status Fields ---
        [Title("Status")]
        [ProgressBar(0, 1), ShowInInspector, ReadOnly]
        private float progress = 0f;
        [ShowInInspector, ReadOnly]
        private string currentStatus = "Idle";
        
        private bool isWorking = false;
        private EditorCoroutine currentCoroutine;

        protected override void OnEnable()
        {
            base.OnEnable();
            settings = TranslatorSettings.GetOrCreateSettings();
        }
        
        // --- Action Buttons ---
        [Button("Translate File", ButtonSizes.Large), PropertyOrder(1)]
        [GUIColor(0.8f, 1f, 0.7f)]
        [EnableIf("@!isWorking")]
        private void TranslateButton()
        {
            if (ValidateInputs())
                currentCoroutine = EditorCoroutineUtility.StartCoroutine(ProcessAndTranslateAll(), this);
        }

        [Button("Stop Process", ButtonSizes.Large), PropertyOrder(2)]
        [GUIColor(1f, 0.7f, 0.7f)]
        [EnableIf("@isWorking")]
        private void StopButton()
        {
            if (currentCoroutine != null) EditorCoroutineUtility.StopCoroutine(currentCoroutine);
            isWorking = false;
            currentStatus = "Process stopped by user.";
            progress = 0;
        }

        // --- Core Logic ---
        private System.Collections.IEnumerator ProcessAndTranslateAll()
        {
            isWorking = true;
            progress = 0f;
            bool fileModified = false;

            // --- Step 1: Parse and Validate TSV ---
            currentStatus = "Parsing TSV file...";
            var grid = ParseTsv(settings.sourceTsvPath);
            if (grid == null) { isWorking = false; yield break; }

            var header = grid[0];
            var languageColumns = new Dictionary<string, int>();
            for (int i = 0; i < header.Length; i++) languageColumns[header[i].Trim()] = i;

            int sourceLangIndex = FindSourceLanguageIndex(languageColumns);
            if (sourceLangIndex == -1)
            {
                EditorUtility.DisplayDialog("Error", $"Could not find a column for source language '{settings.sourceLanguageFullName}' in the TSV header.", "OK");
                isWorking = false;
                yield break;
            }

            // --- Step 2: Ensure sourceHash column exists ---
            int hashColumnIndex;
            if (!languageColumns.TryGetValue(HashColumnName, out hashColumnIndex))
            {
                currentStatus = $"Adding '{HashColumnName}' column...";
                hashColumnIndex = grid[0].Length;
                var newGrid = new List<string[]>();
                newGrid.Add(grid[0].Concat(new[] { HashColumnName }).ToArray());
                for (int i = 1; i < grid.Count; i++) newGrid.Add(grid[i].Concat(new[] { "" }).ToArray());
                grid = newGrid;
                
                languageColumns[HashColumnName] = hashColumnIndex;
                fileModified = true;
                yield return new EditorWaitForSeconds(0.5f);
            }

            // --- Step 3: Verify hashes and clear stale translations ---
            currentStatus = "Verifying source text integrity...";
            int staleCount = 0;
            List<string> targetLangCodes = header.Where(h => h != "key" && h != header[sourceLangIndex] && h != HashColumnName).ToList();

            for (int i = 1; i < grid.Count; i++)
            {
                if (sourceLangIndex >= grid[i].Length || string.IsNullOrEmpty(grid[i][sourceLangIndex])) continue;

                string sourceText = grid[i][sourceLangIndex];
                string currentHash = grid[i][hashColumnIndex];
                string newHash = ComputeSha256Hash(sourceText);

                if (currentHash != newHash)
                {
                    staleCount++;
                    fileModified = true;
                    foreach(string targetCode in targetLangCodes)
                    {
                        if(languageColumns.TryGetValue(targetCode, out int targetIndex))
                        {
                            grid[i][targetIndex] = "";
                        }
                    }
                    grid[i][hashColumnIndex] = newHash;
                }
            }

            if(staleCount > 0)
            {
                currentStatus = $"Found and cleared {staleCount} stale translation(s).";
                yield return new EditorWaitForSeconds(1.5f);
            }

            // --- Step 4: Find and translate all empty cells ---
            var cellsToTranslate = new List<Tuple<int, int, string>>();
            for (int i = 1; i < grid.Count; i++)
            {
                string sourceText = grid[i][sourceLangIndex];
                if(string.IsNullOrEmpty(sourceText)) continue;

                foreach(string targetCode in targetLangCodes)
                {
                    if (languageColumns.TryGetValue(targetCode, out int targetIndex) && string.IsNullOrWhiteSpace(grid[i][targetIndex]))
                    {
                        cellsToTranslate.Add(new Tuple<int, int, string>(i, targetIndex, sourceText));
                    }
                }
            }

            if (cellsToTranslate.Count == 0 && !fileModified)
            {
                currentStatus = "No changes or empty cells to translate.";
                isWorking = false;
                yield break;
            }

            int translationsDone = 0;
            
            foreach(var cell in cellsToTranslate)
            {
                int row = cell.Item1;
                int col = cell.Item2;
                string sourceText = cell.Item3;
                string targetLangCode = grid[0][col];

                currentStatus = $"Translating '{sourceText}' to {targetLangCode}...";
                
                var request = CreateTranslationRequest(sourceText, targetLangCode);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<OpenAIAPIResponse>(request.downloadHandler.text);
                    string translatedText = "";
                    if (response?.choices != null && response.choices.Count > 0)
                    {
                        var message = response.choices[0].message;
                        if (message != null && !string.IsNullOrWhiteSpace(message.content))
                        {
                            string content = message.content.Trim();
                            if (!content.Equals("<null>", StringComparison.OrdinalIgnoreCase) && !content.Equals("null", StringComparison.OrdinalIgnoreCase))
                                translatedText = content;
                        }
                    }
                    grid[row][col] = translatedText;
                    fileModified = true;
                }
                else
                {
                    currentStatus = $"Error translating '{sourceText}': {request.error}";
                    yield return new EditorWaitForSeconds(2);
                }

                translationsDone++;
                progress = (float)translationsDone / cellsToTranslate.Count;
            }

            if (fileModified)
            {
                currentStatus = "Saving changes to file...";
                SaveTsv(grid, settings.sourceTsvPath, settings.overwriteOriginalFile);
                currentStatus = "Process complete!";
            }

            isWorking = false;
        }
        
        private UnityWebRequest CreateTranslationRequest(string text, string targetLanguageCode)
        {
            // --- UPDATED: Combine base prompt with custom instructions ---
            string systemPrompt = $"Translate the given text to the language with code '{targetLanguageCode}'. Provide only the translated text, no commentary, formatting, or quotes.";
            if (!string.IsNullOrWhiteSpace(settings.customInstructions))
            {
                systemPrompt += " " + settings.customInstructions;
            }

            var request = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
            var apiRequestBody = new OpenAIAPIBody
            {
                model = "gpt-3.5-turbo",
                messages = new List<Message>
                {
                    new Message { role = "system", content = systemPrompt },
                    new Message { role = "user", content = text }
                }
            };

            string jsonBody = JsonUtility.ToJson(apiRequestBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + settings.apiKey);

            return request;
        }

        private List<string[]> ParseTsv(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"TSV file not found at path: {path}");
                return null;
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            var grid = new List<string[]>();
            if (lines.Length == 0) return grid;
            
            string[] header = lines[0].TrimEnd().Split('\t');
            int columnCount = header.Length;
            grid.Add(header);

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] values = line.TrimEnd().Split('\t');
                
                if (values.Length < columnCount)
                {
                    var paddedValues = new string[columnCount];
                    Array.Copy(values, paddedValues, values.Length);
                    for (int j = values.Length; j < columnCount; j++)
                    {
                        paddedValues[j] = "";
                    }
                    grid.Add(paddedValues);
                }
                else
                {
                    grid.Add(values);
                }
            }
            return grid;
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrEmpty(settings.sourceTsvPath) || !File.Exists(settings.sourceTsvPath))
            {
                EditorUtility.DisplayDialog("Error", "Please assign a valid source TSV file path.", "OK");
                return false;
            }
            if (string.IsNullOrEmpty(settings.apiKey))
            {
                EditorUtility.DisplayDialog("Error", "Please enter your API key.", "OK");
                return false;
            }
            if (string.IsNullOrEmpty(settings.sourceLanguageFullName))
            {
                EditorUtility.DisplayDialog("Error", "Source language cannot be empty.", "OK");
                return false;
            }
            return true;
        }

        private int FindSourceLanguageIndex(Dictionary<string, int> languageColumns)
        {
            foreach (var pair in languageColumns)
            {
                if (settings.sourceLanguageFullName.Equals(pair.Key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }
            foreach (var pair in languageColumns)
            {
                if (settings.sourceLanguageFullName.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }
            return -1;
        }

        private void SaveTsv(List<string[]> grid, string path, bool overwrite)
        {
            string finalPath = path;
            if (!overwrite)
            {
                string directory = Path.GetDirectoryName(path);
                string fileName = Path.GetFileNameWithoutExtension(path);
                string extension = Path.GetExtension(path);
                finalPath = EditorUtility.SaveFilePanel("Save Modified TSV", directory, $"{fileName}_modified{extension}", "tsv");
            }
            
            if (string.IsNullOrEmpty(finalPath))
            {
                currentStatus = "Save cancelled.";
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (string[] row in grid)
            {
                sb.AppendLine(string.Join("\t", row));
            }

            File.WriteAllText(finalPath, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", $"TSV file updated successfully at:\n{finalPath}", "OK");
        }
        
        private static string ComputeSha256Hash(string rawData)
        {
            using var sha256Hash = SHA256.Create();
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            var builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        [Serializable] private class OpenAIAPIBody { public string model; public List<Message> messages; }
        [Serializable] private class Message { public string role; public string content; }
        [Serializable] private class OpenAIAPIResponse { public List<Choice> choices; }
        [Serializable] private class Choice { public Message message; }
    }
}
