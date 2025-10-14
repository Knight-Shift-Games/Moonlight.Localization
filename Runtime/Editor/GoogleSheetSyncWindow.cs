using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Unity.EditorCoroutines.Editor;
using UnityEngine.Networking;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace Moonlight.Localization
{
    public class GoogleSheetSyncWindow : OdinEditorWindow
    {
        // --- Static Fields for Background Task ---
        private static bool isWorking = false;
        private static SyncStatusInfo currentSyncStatus = new SyncStatusInfo { Status = SyncStatus.Unknown, Message = "Awaiting first check."};
        private static event Action OnStatusChanged;
        
        private const string GoogleAuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string GoogleSheetsScope = "https://www.googleapis.com/auth/spreadsheets";

        // --- Editor Window Setup ---
        [MenuItem("Tools/Localization/Google Sheet Sync")]
        public static void ShowWindow() => GetWindow<GoogleSheetSyncWindow>("Google Sheet Sync");

        [ShowInInspector, InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Hidden)]
        private GoogleSheetSyncSettings settings;

        [ShowInInspector, PropertyOrder(0)]
        [InfoBox("$" + nameof(GetStatusMessage), InfoMessageType = InfoMessageType.Info)]
        private readonly bool syncStatusDisplay;

        private string GetStatusMessage() => currentSyncStatus.Message;
        private InfoMessageType GetStatusColor() => currentSyncStatus.Color;
        
        static GoogleSheetSyncWindow() => EditorApplication.update += PeriodicCheck;

        protected override void OnEnable()
        {
            base.OnEnable();
            settings = GoogleSheetSyncSettings.GetOrCreateSettings();
            OnStatusChanged += Repaint;
            CheckForChanges(true);
        }

        protected override void OnDisable() => OnStatusChanged -= Repaint;

        // --- Action Buttons ---
        [Button("Re-Check Status Now", ButtonSizes.Large), PropertyOrder(1)]
        [EnableIf("@!isWorking")]
        private void CheckForChangesNow() => CheckForChanges(true);
        
        [Button("Import from Google Sheet", ButtonSizes.Large), PropertyOrder(2)]
        [GUIColor(0.7f, 0.9f, 1f)]
        [EnableIf("@!isWorking && currentSyncStatus.Status == SyncStatus.OutOfSync")]
        private void ImportButton()
        {
            if (ValidateInputs(true) && ConfirmOverwrite()) 
                EditorCoroutineUtility.StartCoroutine(DownloadSheet(), this);
        }
        
        [Button("Export to Google Sheet", ButtonSizes.Large), PropertyOrder(3)]
        [GUIColor(0.8f, 1f, 0.7f)]
        [EnableIf("@!isWorking && HasRefreshToken()")]
        private void ExportButton()
        {
            if (ValidateInputs(true) && ConfirmExport())
                EditorCoroutineUtility.StartCoroutine(UploadToSheet(), this);
        }

        [Button("Authenticate with Google", ButtonSizes.Large), PropertyOrder(4)]
        [GUIColor(0.9f, 0.9f, 0.6f)]
        [EnableIf("@!isWorking && !HasRefreshToken()")]
        private void AuthenticateButton()
        {
            if (string.IsNullOrEmpty(settings.googleApiClientId) || string.IsNullOrEmpty(settings.googleApiClientSecret))
            {
                EditorUtility.DisplayDialog("Error", "Please fill in the Google API Client ID and Secret in the settings.", "OK");
                return;
            }
            EditorCoroutineUtility.StartCoroutineOwnerless(DoGoogleAuth(settings));
        }
        
        private bool HasRefreshToken() => !string.IsNullOrEmpty(settings.googleApiRefreshToken);

        // --- Authentication Flow ---
        private static System.Collections.IEnumerator DoGoogleAuth(GoogleSheetSyncSettings authSettings)
        {
            isWorking = true;
            string redirectUri = "http://127.0.0.1:51234/";
            SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.Checking, Message = "Waiting for Google authentication in browser..." });

            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();
            
            string authUrl = $"{GoogleAuthEndpoint}?client_id={authSettings.googleApiClientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={Uri.EscapeDataString(GoogleSheetsScope)}&access_type=offline&prompt=consent";
            Application.OpenURL(authUrl);
            
            var contextTask = listener.GetContextAsync();
            yield return new WaitUntil(() => contextTask.IsCompleted);
            
            var context = contextTask.Result;
            var response = context.Response;
            string authCode = context.Request.QueryString.Get("code");

            byte[] buffer = Encoding.UTF8.GetBytes("<html><body><b>Authentication successful!</b> You can now close this browser tab.</body></html>");
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
            listener.Stop();

            if (string.IsNullOrEmpty(authCode))
            {
                SetSyncStatus(new SyncStatusInfo { Status = SyncStatus.Error, Message = "Authentication failed. No authorization code received." });
                isWorking = false;
                yield break;
            }
            
            SetSyncStatus(new SyncStatusInfo { Status = SyncStatus.Checking, Message = "Exchanging code for tokens..." });
            
            var form = new WWWForm();
            form.AddField("code", authCode);
            form.AddField("client_id", authSettings.googleApiClientId);
            form.AddField("client_secret", authSettings.googleApiClientSecret);
            form.AddField("redirect_uri", redirectUri);
            form.AddField("grant_type", "authorization_code");

            var request = UnityWebRequest.Post(GoogleTokenEndpoint, form);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var tokenData = JsonUtility.FromJson<GoogleTokenResponse>(request.downloadHandler.text);
                authSettings.googleApiRefreshToken = tokenData.refresh_token;
                EditorUtility.SetDirty(authSettings);
                SetSyncStatus(new SyncStatusInfo { Status = SyncStatus.Unknown, Message = "Authentication successful! Ready to export." });
            }
            else
            {
                SetSyncStatus(new SyncStatusInfo { Status = SyncStatus.Error, Message = $"Token exchange failed: {request.error}"});
            }

            isWorking = false;
        }

        // --- Core Logic ---
        private System.Collections.IEnumerator UploadToSheet()
        {
            isWorking = true;
            SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.Checking, Message = "Getting fresh access token..."});
            
            string accessToken = null;
            var tokenTask = GetAccessToken();
            yield return new WaitUntil(() => tokenTask.IsCompleted);
            accessToken = tokenTask.Result;

            if (string.IsNullOrEmpty(accessToken))
            {
                SetSyncStatus(new SyncStatusInfo { Status = SyncStatus.Error, Message = "Failed to get access token. Please re-authenticate."});
                isWorking = false;
                yield break;
            }
            
            string sheetId = GetSheetID(settings.googleSheetUrl);
            string gid = GetSheetGID(settings.googleSheetUrl);

            if (string.IsNullOrEmpty(sheetId) || string.IsNullOrEmpty(gid))
            {
                 SetSyncStatus(new SyncStatusInfo { Status = SyncStatus.Error, Message = "Could not parse Sheet ID or GID from URL."});
                 isWorking = false;
                 yield break;
            }
            
            SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.Checking, Message = "Preparing data for batch update..."});

            string localText = File.ReadAllText(settings.localTsvPath, Encoding.UTF8);

            // --- FIXED: Manually construct the JSON to avoid JsonUtility's limitations ---
            // This ensures that the `requests` array contains two distinct request objects,
            // each with only one valid operation, as required by the Google Sheets API.

            // 1. Create the JSON for the "clear" part of the request.
            string clearRequestJson = JsonUtility.ToJson(new UpdateCellsRequest {
                range = new GridRange { sheetId = int.Parse(gid) },
                fields = "userEnteredValue" // A wildcard to clear everything
            });

            // 2. Create the JSON for the "paste" part of the request.
            string pasteRequestJson = JsonUtility.ToJson(new PasteDataRequest {
                coordinate = new GridCoordinate { sheetId = int.Parse(gid), rowIndex = 0, columnIndex = 0 },
                data = localText,
                type = "PASTE_NORMAL",
                delimiter = "\t"
            });

            // 3. Combine them into a valid batchUpdate payload string.
            string jsonBody = $@"{{
                ""requests"": [
                    {{ ""updateCells"": {clearRequestJson} }},
                    {{ ""pasteData"": {pasteRequestJson} }}
                ]
            }}";
            
            string updateUrl = $"https://sheets.googleapis.com/v4/spreadsheets/{sheetId}:batchUpdate";
            
            var updateRequest = new UnityWebRequest(updateUrl, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            updateRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            updateRequest.downloadHandler = new DownloadHandlerBuffer();
            updateRequest.SetRequestHeader("Content-Type", "application/json");
            updateRequest.SetRequestHeader("Authorization", "Bearer " + accessToken);

            SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.Checking, Message = "Sending batch update to Google Sheets..."});
            yield return updateRequest.SendWebRequest();

            if (updateRequest.result == UnityWebRequest.Result.Success)
            {
                SetSyncStatus(new SyncStatusInfo { Status = SyncStatus.InSync, Message = $"Export successful! Last updated: {DateTime.Now:G}"});
                EditorUtility.DisplayDialog("Success", "Successfully exported data to Google Sheet.", "OK");
                settings.lastKnownSheetHash = ComputeSha256Hash(localText);
                settings.lastCheckTicks = DateTime.UtcNow.Ticks;
                EditorUtility.SetDirty(settings);
            }
            else
            {
                 SetSyncStatus(new SyncStatusInfo { Status = SyncStatus.Error, Message = $"Export failed: {updateRequest.downloadHandler.text}"});
                 Debug.LogError($"Google Sheets API Error: {updateRequest.downloadHandler.text}");
            }

            isWorking = false;
        }
        
        private async Task<string> GetAccessToken()
        {
            var form = new WWWForm();
            form.AddField("client_id", settings.googleApiClientId);
            form.AddField("client_secret", settings.googleApiClientSecret);
            form.AddField("refresh_token", settings.googleApiRefreshToken);
            form.AddField("grant_type", "refresh_token");

            using (var request = UnityWebRequest.Post(GoogleTokenEndpoint, form))
            {
                var asyncOp = request.SendWebRequest();
                while (!asyncOp.isDone) await Task.Yield();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var tokenData = JsonUtility.FromJson<GoogleTokenResponse>(request.downloadHandler.text);
                    return tokenData.access_token;
                }
                else
                {
                     Debug.LogError("Failed to refresh access token: " + request.error);
                     settings.googleApiRefreshToken = ""; 
                     EditorUtility.SetDirty(settings);
                     return null;
                }
            }
        }

        private static void PeriodicCheck()
        {
            if (isWorking) return;
            var settings = GoogleSheetSyncSettings.GetOrCreateSettings();
            if (!settings.autoCheckForChanges) return;
            var interval = TimeSpan.FromMinutes(settings.checkIntervalMinutes);
            if (DateTime.UtcNow - new DateTime(settings.lastCheckTicks) > interval)
                CheckForChanges(false);
        }
        
        private static void CheckForChanges(bool force)
        {
            if (isWorking && !force) return;
            EditorCoroutineUtility.StartCoroutineOwnerless(CheckSheetStatusCoroutine());
        }
        
        private static System.Collections.IEnumerator CheckSheetStatusCoroutine()
        {
            isWorking = true;
            var settings = GoogleSheetSyncSettings.GetOrCreateSettings();
            if (!ValidateStaticInputs(settings))
            {
                isWorking = false;
                yield break;
            }
            SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.Checking, Message = "Downloading from Google Sheets..." });
            string downloadUrl = GetTsvDownloadUrl(settings.googleSheetUrl);
            var request = UnityWebRequest.Get(downloadUrl);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                string downloadedText = request.downloadHandler.text;
                string localText = File.ReadAllText(settings.localTsvPath, Encoding.UTF8);
                if (ComputeSha256Hash(downloadedText) == ComputeSha256Hash(localText))
                    SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.InSync, Message = $"In Sync. Last checked: {DateTime.Now:G}" });
                else
                    SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.OutOfSync, Message = $"Out of Sync! Google Sheet has changes. Last checked: {DateTime.Now:G}" });
            }
            else
            {
                SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.Error, Message = $"Error checking sheet: {request.error}" });
            }
            settings.lastCheckTicks = DateTime.UtcNow.Ticks;
            EditorUtility.SetDirty(settings);
            isWorking = false;
        }

        private System.Collections.IEnumerator DownloadSheet()
        {
            isWorking = true;
            SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.Checking, Message = "Importing data from Google Sheet..." });
            string downloadUrl = GetTsvDownloadUrl(settings.googleSheetUrl);
            var request = UnityWebRequest.Get(downloadUrl);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                string downloadedText = request.downloadHandler.text;
                File.WriteAllText(settings.localTsvPath, downloadedText, Encoding.UTF8);
                AssetDatabase.Refresh();
                settings.lastKnownSheetHash = ComputeSha256Hash(downloadedText);
                settings.lastCheckTicks = DateTime.UtcNow.Ticks;
                EditorUtility.SetDirty(settings);
                SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.InSync, Message = $"Import complete! {DateTime.Now:G}" });
                EditorUtility.DisplayDialog("Success", "Successfully imported data from Google Sheet.", "OK");
            }
            else
            {
                SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.Error, Message = $"Error importing: {request.error}" });
            }
            isWorking = false;
        }
        
        private static void SetSyncStatus(SyncStatusInfo newStatus)
        {
            currentSyncStatus = newStatus;
            OnStatusChanged?.Invoke();
        }

        private bool ValidateInputs(bool checkUrl)
        {
            if (string.IsNullOrEmpty(settings.localTsvPath) || !File.Exists(settings.localTsvPath))
            {
                EditorUtility.DisplayDialog("Error", "Please assign a valid local TSV file path.", "OK");
                return false;
            }
            if (checkUrl && string.IsNullOrEmpty(GetTsvDownloadUrl(settings.googleSheetUrl)))
            {
                EditorUtility.DisplayDialog("Error", "Please provide a valid Google Sheet URL.", "OK");
                return false;
            }
            return true;
        }
        
        private static bool ValidateStaticInputs(GoogleSheetSyncSettings s)
        {
            if (string.IsNullOrEmpty(s.googleSheetUrl) || string.IsNullOrEmpty(s.localTsvPath) || !File.Exists(s.localTsvPath))
            {
                SetSyncStatus(new SyncStatusInfo{ Status = SyncStatus.Error, Message = "Configure TSV Path and Sheet URL." });
                return false;
            }
            return true;
        }
        
        private static string GetSheetID(string url) => new Regex(@"/spreadsheets/d/([a-zA-Z0-9-_]+)").Match(url).Groups[1].Value;
        private static string GetSheetGID(string url)
        {
            if(string.IsNullOrEmpty(url)) return "";
            return new Regex(@"gid=([0-9]+)").Match(url).Groups[1].Value;
        }

        private static string GetTsvDownloadUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var match = new Regex(@"/spreadsheets/d/([a-zA-Z0-9-_]+).*gid=([0-9]+)").Match(url);
            return match.Success ? $"https://docs.google.com/spreadsheets/d/{match.Groups[1].Value}/export?format=tsv&gid={match.Groups[2].Value}" : null;
        }
        
        private static string ComputeSha256Hash(string rawData)
        {
            using var sha256Hash = SHA256.Create();
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            var builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }
        
        private bool ConfirmOverwrite() => EditorUtility.DisplayDialog("Confirm Overwrite", "This will overwrite your local file:\n\n" + settings.localTsvPath, "Yes, Overwrite", "Cancel");
        private bool ConfirmExport() => EditorUtility.DisplayDialog("Confirm Export", "This will overwrite the data in your Google Sheet.", "Yes, Export", "Cancel");
        
        // --- Helper Structs & Classes ---
        private enum SyncStatus { Unknown, Checking, InSync, OutOfSync, Error }
        private struct SyncStatusInfo
        {
            public SyncStatus Status; public string Message;
            public InfoMessageType Color => Status switch
            {
                SyncStatus.InSync => InfoMessageType.Info, SyncStatus.OutOfSync => InfoMessageType.Warning,
                SyncStatus.Error => InfoMessageType.Error, _ => InfoMessageType.Info,
            };
        }
        [Serializable] private class GoogleTokenResponse { public string access_token; public string refresh_token; }

        // --- Classes for batchUpdate Request ---
        // These are only used for serialization, so we don't need the main request wrapper.
        [Serializable]
        private class UpdateCellsRequest { public GridRange range; public string fields; }
        [Serializable]
        private class PasteDataRequest { public GridCoordinate coordinate; public string data; public string type; public string delimiter; }
        [Serializable]
        private class GridRange { public int sheetId; }
        [Serializable]
        private class GridCoordinate { public int sheetId; public int rowIndex; public int columnIndex; }
    }
}
