using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyncDAT
{
    /// <summary>
    /// Configuration for a single character's WhoDAT.lua file location
    /// </summary>
    public class CharacterConfig
    {
        public string CharacterName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public DateTime? LastUpload { get; set; }
        public string? LastError { get; set; }

        [JsonIgnore]
        public bool Notified2_5MB { get; set; } = false;
        [JsonIgnore]
        public bool Notified5MB { get; set; } = false;
        [JsonIgnore]
        public bool Notified7MB { get; set; } = false;
    }

    /// <summary>
    /// Defines a single download-sync target: an API endpoint returning a Lua file
    /// to be written into a specific addon or other directory under the WoW install.
    /// To add a new addon sync, add a new SyncTarget entry - no other code changes needed.
    /// </summary>
    public class SyncTarget
    {
        /// <summary>Display name shown in the UI (e.g. "TheGrudge")</summary>
        public string Name { get; set; } = "";

        /// <summary>Relative endpoint path appended to ApiEndpoint base (e.g. "grudge_export.php")</summary>
        public string EndpointPath { get; set; } = "";

        /// <summary>Filename to write (e.g. "TheGrudgeDB.lua")</summary>
        public string OutputFileName { get; set; } = "";

        /// <summary>
        /// Full path to the directory where OutputFileName will be written.
        /// Defaults to {WoWBasePath}\Interface\AddOns\{AddonName} but can be
        /// set to any path, including locations outside the WoW base directory.
        /// </summary>
        public string OutputDirectory { get; set; } = "";

        /// <summary>Whether this sync target is enabled</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Last successful sync time</summary>
        public DateTime? LastSync { get; set; }

        /// <summary>Last error message, if any</summary>
        public string? LastError { get; set; }

        /// <summary>
        /// Returns the full resolved output path for this target.
        /// </summary>
        [JsonIgnore]
        public string FullOutputPath => Path.Combine(OutputDirectory, OutputFileName);
    }

    /// <summary>
    /// Main application configuration
    /// </summary>
    public class AppConfig
    {
        public string ApiKey { get; set; } = "";
        public string ApiEndpoint { get; set; } = "https://your-domain.com/api/";
        public List<CharacterConfig> Characters { get; set; } = new List<CharacterConfig>();
        public bool MinimizeToTray { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public int UploadDelaySeconds { get; set; } = 60;
        public bool EnableSizeNotifications { get; set; } = true;
        public bool EnableAutoBackup { get; set; } = false;
        public double BackupSizeThresholdMB { get; set; } = 5.0;

        // ── WoW base path ─────────────────────────────────────────────────────
        /// <summary>
        /// Root directory of the WoW installation (e.g. C:\World of Warcraft\_classic_era_\).
        /// Used as the starting point for file pickers and to seed default output paths
        /// for sync targets. Individual paths can still be set outside this directory.
        /// </summary>
        public string WoWBasePath { get; set; } = "";

        // ── Download sync targets ─────────────────────────────────────────────
        /// <summary>
        /// Download sync targets. Each entry maps an API endpoint to an output Lua file
        /// and its destination directory. Add new entries as additional WhoDASH-powered
        /// addons are created.
        /// </summary>
        public List<SyncTarget> SyncTargets { get; set; } = new List<SyncTarget>
        {
            new SyncTarget
            {
                Name = "TheGrudge",
                EndpointPath = "grudge_export.php",
                OutputFileName = "TheGrudgeDB.lua",
                OutputDirectory = "",   // seeded at runtime from WoWBasePath if empty
                Enabled = true
            }
        };

        /// <summary>Enable automatic periodic download sync</summary>
        public bool EnableAutoSync { get; set; } = false;

        /// <summary>Auto-sync interval in minutes</summary>
        public int AutoSyncIntervalMinutes { get; set; } = 30;

        [JsonIgnore]
        public string ConfigFilePath { get; private set; } = "";

        // ── Derived path helpers ──────────────────────────────────────────────

        /// <summary>
        /// Returns the WTF\Account folder under WoWBasePath, or an empty string if
        /// WoWBasePath is not configured.
        /// </summary>
        [JsonIgnore]
        public string WtfAccountPath =>
            string.IsNullOrWhiteSpace(WoWBasePath)
                ? ""
                : Path.Combine(WoWBasePath, "WTF", "Account");

        /// <summary>
        /// Returns the Interface\AddOns folder under WoWBasePath, or an empty string
        /// if WoWBasePath is not configured.
        /// </summary>
        [JsonIgnore]
        public string AddOnsPath =>
            string.IsNullOrWhiteSpace(WoWBasePath)
                ? ""
                : Path.Combine(WoWBasePath, "Interface", "AddOns");

        /// <summary>
        /// Returns the default output directory for a named addon, seeded from WoWBasePath.
        /// Returns an empty string if WoWBasePath is not configured.
        /// </summary>
        public string GetDefaultAddonOutputDirectory(string addonFolderName) =>
            string.IsNullOrWhiteSpace(WoWBasePath)
                ? ""
                : Path.Combine(WoWBasePath, "Interface", "AddOns", addonFolderName);

        // ── Load / Save ───────────────────────────────────────────────────────

        public static AppConfig Load()
        {
            string configPath = GetConfigPath();

            if (!File.Exists(configPath))
            {
                return new AppConfig { ConfigFilePath = configPath };
            }

            try
            {
                string json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                config.ConfigFilePath = configPath;

                // Ensure the default TheGrudge target exists in upgraded configs
                if (config.SyncTargets == null || config.SyncTargets.Count == 0)
                {
                    config.SyncTargets = new List<SyncTarget>
                    {
                        new SyncTarget
                        {
                            Name = "TheGrudge",
                            EndpointPath = "grudge_export.php",
                            OutputFileName = "TheGrudgeDB.lua",
                            OutputDirectory = "",
                            Enabled = true
                        }
                    };
                }

                return config;
            }
            catch
            {
                try
                {
                    string backupPath = configPath + ".backup." + DateTime.Now.ToString("yyyyMMddHHmmss");
                    File.Copy(configPath, backupPath);
                }
                catch { }

                return new AppConfig { ConfigFilePath = configPath };
            }
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(ConfigFilePath) ?? "";
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save configuration: {ex.Message}", ex);
            }
        }

        private static string GetConfigPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "SyncDAT");
            return Path.Combine(appFolder, "config.json");
        }

        public void AddCharacter(string name, string filePath)
        {
            Characters.Add(new CharacterConfig { CharacterName = name, FilePath = filePath, Enabled = true });
            Save();
        }

        public void RemoveCharacter(CharacterConfig character)
        {
            Characters.Remove(character);
            Save();
        }

        public void UpdateLastUpload(CharacterConfig character, DateTime uploadTime, string? error = null)
        {
            character.LastUpload = uploadTime;
            character.LastError = error;
            Save();
        }

        public void UpdateLastSync(SyncTarget target, DateTime syncTime, string? error = null)
        {
            target.LastSync = syncTime;
            target.LastError = error;
            Save();
        }
    }
}