using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SyncDAT
{
    /// <summary>
    /// Manages file watching and upload scheduling for WhoDAT.lua files
    /// </summary>
    public class FileWatcherService : IDisposable
    {
        private readonly AppConfig _config;
        private readonly Dictionary<string, FileSystemWatcher> _watchers;
        private readonly Dictionary<string, Timer> _uploadTimers;
        private readonly Dictionary<string, DateTime> _lastChangeLog; // Track when we last logged a change
        private readonly object _lockObject = new object();
        private static readonly HttpClient _httpClient;
        
        public event EventHandler<FileChangeEventArgs>? FileChanged;
        public event EventHandler<UploadScheduledEventArgs>? UploadScheduled;
        public event EventHandler<UploadEventArgs>? UploadStarted;
        public event EventHandler<UploadEventArgs>? UploadCompleted;
        public event EventHandler<UploadErrorEventArgs>? UploadError;
        public event EventHandler<FileSizeWarningEventArgs>? FileSizeWarning;
        public event EventHandler<BackupEventArgs>? BackupCompleted;

        static FileWatcherService()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true;

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        public FileWatcherService(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _watchers = new Dictionary<string, FileSystemWatcher>();
            _uploadTimers = new Dictionary<string, Timer>();
            _lastChangeLog = new Dictionary<string, DateTime>();
        }

        public void StartWatching()
        {
            foreach (var character in _config.Characters)
            {
                if (character.Enabled)
                {
                    StartWatchingCharacter(character);
                }
            }
        }

        public void StopWatching()
        {
            lock (_lockObject)
            {
                foreach (var watcher in _watchers.Values)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                _watchers.Clear();

                foreach (var timer in _uploadTimers.Values)
                {
                    timer.Dispose();
                }
                _uploadTimers.Clear();
                
                _lastChangeLog.Clear();
            }
        }

        public void RefreshWatchers()
        {
            lock (_lockObject)
            {
                var targetPaths = new HashSet<string>();
                foreach (var character in _config.Characters)
                {
                    if (character.Enabled)
                    {
                        targetPaths.Add(character.FilePath);
                    }
                }

                var pathsToRemove = _watchers.Keys.Except(targetPaths).ToList();
                foreach (var path in pathsToRemove)
                {
                    _watchers[path].EnableRaisingEvents = false;
                    _watchers[path].Dispose();
                    _watchers.Remove(path);
                    
                    if (_uploadTimers.ContainsKey(path))
                    {
                        _uploadTimers[path].Dispose();
                        _uploadTimers.Remove(path);
                    }
                    
                    _lastChangeLog.Remove(path);
                }

                foreach (var character in _config.Characters)
                {
                    if (character.Enabled && !_watchers.ContainsKey(character.FilePath))
                    {
                        StartWatchingCharacter(character);
                    }
                }
            }
        }

        private void StartWatchingCharacter(CharacterConfig character)
        {
            if (!File.Exists(character.FilePath))
            {
                OnUploadError(new UploadErrorEventArgs(character, $"File not found: {character.FilePath}"));
                return;
            }

            lock (_lockObject)
            {
                if (_watchers.ContainsKey(character.FilePath))
                {
                    _watchers[character.FilePath].Dispose();
                    _watchers.Remove(character.FilePath);
                }

                try
                {
                    string directory = Path.GetDirectoryName(character.FilePath) ?? "";
                    string fileName = Path.GetFileName(character.FilePath);

                    var watcher = new FileSystemWatcher(directory)
                    {
                        Filter = fileName,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };

                    watcher.Changed += (sender, e) => OnFileChanged(character, e);

                    _watchers[character.FilePath] = watcher;
                }
                catch (Exception ex)
                {
                    OnUploadError(new UploadErrorEventArgs(character, $"Failed to watch file: {ex.Message}"));
                }
            }
        }

        private void OnFileChanged(CharacterConfig character, FileSystemEventArgs e)
        {
            lock (_lockObject)
            {
                bool shouldLog = true;
                var now = DateTime.Now;
                
                // Debounce logging: Only log if we haven't logged for this file in the last 3 seconds
                if (_lastChangeLog.ContainsKey(character.FilePath))
                {
                    var timeSinceLastLog = now - _lastChangeLog[character.FilePath];
                    shouldLog = timeSinceLastLog.TotalSeconds >= 3;
                }
                
                if (shouldLog)
                {
                    _lastChangeLog[character.FilePath] = now;
                    OnFileChanged(new FileChangeEventArgs(character, e.ChangeType.ToString()));
                }

                // Always cancel existing timer and reschedule upload (this handles rapid file changes)
                if (_uploadTimers.ContainsKey(character.FilePath))
                {
                    _uploadTimers[character.FilePath].Dispose();
                    _uploadTimers.Remove(character.FilePath);
                }

                var scheduledTime = DateTime.Now.AddSeconds(_config.UploadDelaySeconds);
                var timer = new Timer(
                    async _ => await PerformUpload(character),
                    null,
                    _config.UploadDelaySeconds * 1000,
                    Timeout.Infinite
                );

                _uploadTimers[character.FilePath] = timer;
                
                // Only log the scheduling if we logged the file change
                if (shouldLog)
                {
                    OnUploadScheduled(new UploadScheduledEventArgs(character, scheduledTime));
                }
            }
        }

        private long GetFileSize(string filePath)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(filePath);
                fileInfo.Refresh();
                return fileInfo.Length;
            }
            catch
            {
                return 0;
            }
        }

        private async Task PerformUpload(CharacterConfig character)
        {
            OnUploadStarted(new UploadEventArgs(character));

            try
            {
                await Task.Delay(500);

                long fileSizeBytes = GetFileSize(character.FilePath);
                double fileSizeMB = fileSizeBytes / (1024.0 * 1024.0);

                bool shouldBackup = _config.EnableAutoBackup && fileSizeMB >= _config.BackupSizeThresholdMB;

                byte[] fileContent;
                try
                {
                    fileContent = await File.ReadAllBytesAsync(character.FilePath);
                }
                catch (IOException)
                {
                    try
                    {
                        await Task.Delay(1000);
                        using (FileStream fs = new FileStream(character.FilePath,
                            FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            fileContent = new byte[fs.Length];
                            await fs.ReadAsync(fileContent, 0, (int)fs.Length);
                        }
                    }
                    catch (IOException)
                    {
                        await Task.Delay(2000);
                        using (FileStream fs = new FileStream(character.FilePath,
                            FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            fileContent = new byte[fs.Length];
                            int totalRead = 0;
                            int remaining = (int)fs.Length;
                            while (remaining > 0)
                            {
                                int read = await fs.ReadAsync(fileContent, totalRead, remaining);
                                if (read == 0) break;
                                totalRead += read;
                                remaining -= read;
                            }
                        }
                    }
                }

                double actualSizeMB = fileContent.Length / (1024.0 * 1024.0);
                
                string sizeDebugInfo = $"File: {character.CharacterName} | " +
                                      $"Bytes: {fileContent.Length:N0} | " +
                                      $"KB: {fileContent.Length / 1024.0:F2} | " +
                                      $"MB: {actualSizeMB:F2}";
                
                OnFileSizeWarning(new FileSizeWarningEventArgs(character, 0, actualSizeMB, sizeDebugInfo));

                CheckFileSizeAndNotify(character, actualSizeMB);

                using var content = new MultipartFormDataContent();
                using var fileContent2 = new ByteArrayContent(fileContent);
                fileContent2.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                
                content.Add(fileContent2, "whodat_lua", "WhoDAT.lua");

                using var request = new HttpRequestMessage(HttpMethod.Post, _config.ApiEndpoint);
                request.Content = content;
                request.Headers.Add("X-API-Key", _config.ApiKey);

                var response = await _httpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _config.UpdateLastUpload(character, DateTime.Now);
                    OnUploadCompleted(new UploadEventArgs(character, responseText));
                    
                    if (shouldBackup)
                    {
                        await Task.Delay(500);
                        BackupFile(character, actualSizeMB);
                    }
                }
                else
                {
                    string error = $"HTTP {(int)response.StatusCode}: {responseText}";
                    _config.UpdateLastUpload(character, DateTime.Now, error);
                    OnUploadError(new UploadErrorEventArgs(character, error));
                }
            }
            catch (Exception ex)
            {
                string error = $"Upload failed: {ex.Message}";
                _config.UpdateLastUpload(character, DateTime.Now, error);
                OnUploadError(new UploadErrorEventArgs(character, error));
            }
            finally
            {
                lock (_lockObject)
                {
                    if (_uploadTimers.ContainsKey(character.FilePath))
                    {
                        _uploadTimers[character.FilePath].Dispose();
                        _uploadTimers.Remove(character.FilePath);
                    }
                }
            }
        }

        public async Task TriggerUpload(CharacterConfig character)
        {
            await PerformUpload(character);
        }

        public async Task<string> CheckFileSize(CharacterConfig character)
        {
            try
            {
                string result = $"Character: {character.CharacterName}\r\n";
                result += $"File Path: {character.FilePath}\r\n";
                result += $"---\r\n\r\n";

                if (!File.Exists(character.FilePath))
                {
                    result += "âŒ FILE NOT FOUND\r\n\r\n";
                    result += "Possible issues:\r\n";
                    result += "â€¢ File path is incorrect in config\r\n";
                    result += "â€¢ File was moved or deleted\r\n";
                    result += "â€¢ Drive letter changed\r\n\r\n";
                    result += "Check the path in Configuration â†’ Characters tab";
                    return result;
                }

                FileInfo fileInfo = new FileInfo(character.FilePath);
                fileInfo.Refresh();
                
                long sizeBytes = fileInfo.Length;
                double sizeKB = sizeBytes / 1024.0;
                double sizeMB = sizeBytes / (1024.0 * 1024.0);

                result += $"File exists: âœ“ YES\r\n";
                result += $"Last modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\r\n";
                result += $"Created: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}\r\n";
                result += $"---\r\n\r\n";

                result += $"FileInfo Size (from disk metadata):\r\n";
                result += $"  {sizeBytes:N0} bytes\r\n";
                result += $"  {sizeKB:F2} KB\r\n";
                result += $"  {sizeMB:F2} MB\r\n\r\n";

                byte[] fileContent;
                string readError = "";
                int attempts = 0;
                int maxAttempts = 3;
                
                while (attempts < maxAttempts)
                {
                    attempts++;
                    try
                    {
                        if (attempts == 1)
                        {
                            fileContent = await File.ReadAllBytesAsync(character.FilePath);
                        }
                        else if (attempts == 2)
                        {
                            using (FileStream fs = new FileStream(character.FilePath, 
                                FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                fileContent = new byte[fs.Length];
                                await fs.ReadAsync(fileContent, 0, (int)fs.Length);
                            }
                        }
                        else
                        {
                            using (FileStream fs = new FileStream(character.FilePath,
                                FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            {
                                fileContent = new byte[fs.Length];
                                int totalRead = 0;
                                int remaining = (int)fs.Length;
                                while (remaining > 0)
                                {
                                    int read = await fs.ReadAsync(fileContent, totalRead, remaining);
                                    if (read == 0) break;
                                    totalRead += read;
                                    remaining -= read;
                                }
                            }
                        }

                        long readBytes = fileContent.Length;
                        double readKB = readBytes / 1024.0;
                        double readMB = readBytes / (1024.0 * 1024.0);

                        result += $"Actual Read Size (attempt {attempts}):\r\n";
                        result += $"  {readBytes:N0} bytes\r\n";
                        result += $"  {readKB:F2} KB\r\n";
                        result += $"  {readMB:F2} MB\r\n\r\n";

                        if (sizeBytes != readBytes)
                        {
                            result += $"âš ï¸ Size mismatch detected!\r\n";
                            result += $"FileInfo says {sizeBytes:N0} bytes but read {readBytes:N0} bytes\r\n\r\n";
                        }

                        result += $"Backup Threshold: {_config.BackupSizeThresholdMB:F2} MB\r\n";
                        result += $"Backup Enabled: {(_config.EnableAutoBackup ? "YES" : "NO")}\r\n";
                        result += $"Will Backup: {(readMB >= _config.BackupSizeThresholdMB && _config.EnableAutoBackup ? "YES" : "NO")}\r\n\r\n";

                        if (readBytes == 0)
                        {
                            result += "âš ï¸ FILE IS EMPTY (0 bytes)\r\n\r\n";
                            result += "This could mean:\r\n";
                            result += "â€¢ File was recently backed up and reset\r\n";
                            result += "â€¢ WoW hasn't written to the file yet\r\n";
                            result += "â€¢ File was manually cleared\r\n\r\n";
                            result += "Open the file in Notepad to verify.";
                        }
                        else if (readMB < 0.1)
                        {
                            result += $"â„¹ï¸ File is very small ({readKB:F2} KB)";
                        }
                        else
                        {
                            string preview = System.Text.Encoding.UTF8.GetString(fileContent, 0, Math.Min(100, fileContent.Length));
                            preview = preview.Replace("\r", "").Replace("\n", " ");
                            if (preview.Length > 50)
                            {
                                preview = preview.Substring(0, 50) + "...";
                            }
                            result += $"âœ“ File contains data\r\n";
                            result += $"Preview: {preview}\r\n";
                        }

                        return result;
                    }
                    catch (IOException ex) when (attempts < maxAttempts)
                    {
                        readError = ex.Message;
                        await Task.Delay(1000);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        readError = $"Access denied: {ex.Message}";
                        break;
                    }
                    catch (Exception ex)
                    {
                        readError = $"{ex.GetType().Name}: {ex.Message}";
                        if (attempts < maxAttempts)
                        {
                            await Task.Delay(1000);
                        }
                    }
                }

                result += $"âŒ COULD NOT READ FILE CONTENTS\r\n\r\n";
                result += $"Attempts made: {attempts}\r\n";
                result += $"Last error: {readError}\r\n\r\n";
                result += $"Possible causes:\r\n";
                result += $"â€¢ File is locked by WoW or another program\r\n";
                result += $"â€¢ Antivirus is blocking access\r\n";
                result += $"â€¢ Insufficient permissions\r\n";
                result += $"â€¢ Network drive or connectivity issues\r\n";
                result += $"â€¢ File system error\r\n\r\n";
                result += $"FileInfo shows the file is {sizeMB:F2} MB, but we cannot read it.\r\n\r\n";
                result += $"Try:\r\n";
                result += $"â€¢ Close World of Warcraft completely\r\n";
                result += $"â€¢ Check if antivirus is blocking the file\r\n";
                result += $"â€¢ Run this program as Administrator\r\n";
                result += $"â€¢ Open the file in Notepad to verify you can access it\r\n";

                return result;
            }
            catch (Exception ex)
            {
                return $"âŒ ERROR checking file size:\r\n\r\n{ex.Message}\r\n\r\n{ex.StackTrace}";
            }
        }

        private void CheckFileSizeAndNotify(CharacterConfig character, double fileSizeMB)
        {
            if (!_config.EnableSizeNotifications) return;

            if (fileSizeMB >= 2.5 && !character.Notified2_5MB)
            {
                character.Notified2_5MB = true;
                OnFileSizeWarning(new FileSizeWarningEventArgs(character, 2.5, fileSizeMB));
            }
            else if (fileSizeMB >= 5.0 && !character.Notified5MB)
            {
                character.Notified5MB = true;
                OnFileSizeWarning(new FileSizeWarningEventArgs(character, 5.0, fileSizeMB));
            }
            else if (fileSizeMB >= 7.0 && !character.Notified7MB)
            {
                character.Notified7MB = true;
                OnFileSizeWarning(new FileSizeWarningEventArgs(character, 7.0, fileSizeMB));
            }
        }

        private void BackupFile(CharacterConfig character, double fileSizeMB)
        {
            try
            {
                if (!File.Exists(character.FilePath))
                {
                    OnUploadError(new UploadErrorEventArgs(character, "Backup cancelled: File not found"));
                    return;
                }

                string directory = Path.GetDirectoryName(character.FilePath) ?? "";
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(character.FilePath);
                string extension = Path.GetExtension(character.FilePath);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
                string backupPath = Path.Combine(directory, $"{fileNameWithoutExt}-bkp-{timestamp}{extension}");

                File.Move(character.FilePath, backupPath);
                File.WriteAllText(character.FilePath, "");

                character.Notified2_5MB = false;
                character.Notified5MB = false;
                character.Notified7MB = false;

                OnBackupCompleted(new BackupEventArgs(
                    character, 
                    backupPath, 
                    fileSizeMB,
                    $"File backed up: {Path.GetFileName(backupPath)} ({fileSizeMB:F2} MB)"
                ));
            }
            catch (Exception ex)
            {
                OnUploadError(new UploadErrorEventArgs(character, $"Backup failed: {ex.Message}"));
            }
        }

        public async Task<bool> TriggerBackup(CharacterConfig character)
        {
            try
            {
                if (!File.Exists(character.FilePath))
                {
                    OnUploadError(new UploadErrorEventArgs(character, "Cannot backup: File not found"));
                    return false;
                }

                byte[] fileContent = await File.ReadAllBytesAsync(character.FilePath);
                double fileSizeMB = fileContent.Length / (1024.0 * 1024.0);

                BackupFile(character, fileSizeMB);
                return true;
            }
            catch (Exception ex)
            {
                OnUploadError(new UploadErrorEventArgs(character, $"Manual backup failed: {ex.Message}"));
                return false;
            }
        }

        public void Dispose()
        {
            StopWatching();
        }

        protected virtual void OnFileChanged(FileChangeEventArgs e) => FileChanged?.Invoke(this, e);
        protected virtual void OnUploadScheduled(UploadScheduledEventArgs e) => UploadScheduled?.Invoke(this, e);
        protected virtual void OnUploadStarted(UploadEventArgs e) => UploadStarted?.Invoke(this, e);
        protected virtual void OnUploadCompleted(UploadEventArgs e) => UploadCompleted?.Invoke(this, e);
        protected virtual void OnUploadError(UploadErrorEventArgs e) => UploadError?.Invoke(this, e);
        protected virtual void OnFileSizeWarning(FileSizeWarningEventArgs e) => FileSizeWarning?.Invoke(this, e);
        protected virtual void OnBackupCompleted(BackupEventArgs e) => BackupCompleted?.Invoke(this, e);
    }

    // Event argument classes
    public class FileChangeEventArgs : EventArgs
    {
        public CharacterConfig Character { get; }
        public string ChangeType { get; }

        public FileChangeEventArgs(CharacterConfig character, string changeType)
        {
            Character = character;
            ChangeType = changeType;
        }
    }

    public class UploadScheduledEventArgs : EventArgs
    {
        public CharacterConfig Character { get; }
        public DateTime ScheduledTime { get; }

        public UploadScheduledEventArgs(CharacterConfig character, DateTime scheduledTime)
        {
            Character = character;
            ScheduledTime = scheduledTime;
        }
    }

    public class UploadEventArgs : EventArgs
    {
        public CharacterConfig Character { get; }
        public string? ResponseText { get; }

        public UploadEventArgs(CharacterConfig character, string? responseText = null)
        {
            Character = character;
            ResponseText = responseText;
        }
    }

    public class UploadErrorEventArgs : EventArgs
    {
        public CharacterConfig Character { get; }
        public string Error { get; }

        public UploadErrorEventArgs(CharacterConfig character, string error)
        {
            Character = character;
            Error = error;
        }
    }

    public class FileSizeWarningEventArgs : EventArgs
    {
        public CharacterConfig Character { get; }
        public double ThresholdMB { get; }
        public double CurrentSizeMB { get; }
        public string? DebugInfo { get; }

        public FileSizeWarningEventArgs(CharacterConfig character, double thresholdMB, double currentSizeMB, string? debugInfo = null)
        {
            Character = character;
            ThresholdMB = thresholdMB;
            CurrentSizeMB = currentSizeMB;
            DebugInfo = debugInfo;
        }
    }

    public class BackupEventArgs : EventArgs
    {
        public CharacterConfig Character { get; }
        public string BackupPath { get; }
        public double FileSizeMB { get; }
        public string Message { get; }

        public BackupEventArgs(CharacterConfig character, string backupPath, double fileSizeMB, string message)
        {
            Character = character;
            BackupPath = backupPath;
            FileSizeMB = fileSizeMB;
            Message = message;
        }
    }
}