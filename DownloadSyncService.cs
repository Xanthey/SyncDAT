using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SyncDAT
{
    // ─── Event argument types ────────────────────────────────────────────────

    public class SyncStartedEventArgs : EventArgs
    {
        public SyncTarget Target { get; }
        public SyncStartedEventArgs(SyncTarget target) => Target = target;
    }

    public class SyncCompletedEventArgs : EventArgs
    {
        public SyncTarget Target { get; }
        public string OutputPath { get; }
        public SyncCompletedEventArgs(SyncTarget target, string outputPath)
        {
            Target = target;
            OutputPath = outputPath;
        }
    }

    public class SyncErrorEventArgs : EventArgs
    {
        public SyncTarget Target { get; }
        public string Error { get; }
        public SyncErrorEventArgs(SyncTarget target, string error)
        {
            Target = target;
            Error = error;
        }
    }

    /// <summary>
    /// Handles all download-direction sync operations (dashboard -> WoW client).
    ///
    /// Each SyncTarget carries its own OutputDirectory and OutputFileName, so this
    /// service needs no changes when new addon targets are added to AppConfig.
    /// </summary>
    public class DownloadSyncService : IDisposable
    {
        private readonly AppConfig _config;
        private static readonly HttpClient _httpClient;
        private Timer? _autoSyncTimer;
        private bool _disposed = false;

        public event EventHandler<SyncStartedEventArgs>? SyncStarted;
        public event EventHandler<SyncCompletedEventArgs>? SyncCompleted;
        public event EventHandler<SyncErrorEventArgs>? SyncError;
        public event EventHandler? AutoSyncCycleStarted;

        static DownloadSyncService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public DownloadSyncService(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Sync a single target. Used for manual button clicks.</summary>
        public async Task SyncTargetAsync(SyncTarget target)
        {
            await PerformSync(target);
        }

        /// <summary>Sync all enabled targets. Used for "Sync All" and auto-sync.</summary>
        public async Task SyncAllEnabledAsync()
        {
            foreach (var target in _config.SyncTargets)
            {
                if (target.Enabled)
                {
                    await PerformSync(target);
                }
            }
        }

        /// <summary>Start/restart the automatic sync timer based on current config.</summary>
        public void ConfigureAutoSync()
        {
            _autoSyncTimer?.Dispose();
            _autoSyncTimer = null;

            if (_config.EnableAutoSync && _config.AutoSyncIntervalMinutes > 0)
            {
                int intervalMs = _config.AutoSyncIntervalMinutes * 60 * 1000;
                _autoSyncTimer = new Timer(async _ =>
                {
                    AutoSyncCycleStarted?.Invoke(this, EventArgs.Empty);
                    await SyncAllEnabledAsync();
                }, null, intervalMs, intervalMs);
            }
        }

        /// <summary>Stop the automatic sync timer.</summary>
        public void StopAutoSync()
        {
            _autoSyncTimer?.Dispose();
            _autoSyncTimer = null;
        }

        // ─── Core download logic ──────────────────────────────────────────────

        private async Task PerformSync(SyncTarget target)
        {
            // Validate API key
            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                OnSyncError(target, "No API key configured. Set your API key in Configuration.");
                return;
            }

            // Validate per-target output directory
            if (string.IsNullOrWhiteSpace(target.OutputDirectory))
            {
                OnSyncError(target, $"No output directory configured for {target.Name}. Set it in the Sync tab.");
                return;
            }

            if (!Directory.Exists(target.OutputDirectory))
            {
                // Attempt to create the directory — addon folders may not exist yet on a
                // fresh install before the user has launched WoW at least once.
                try
                {
                    Directory.CreateDirectory(target.OutputDirectory);
                }
                catch
                {
                    OnSyncError(target, $"Output directory does not exist and could not be created: {target.OutputDirectory}");
                    return;
                }
            }

            OnSyncStarted(target);

            try
            {
                string baseUrl = _config.ApiEndpoint.TrimEnd('/');
                string url = $"{baseUrl}/{target.EndpointPath}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-API-Key", _config.ApiKey);

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request);
                }
                catch (TaskCanceledException)
                {
                    OnSyncError(target, "Request timed out. Is the dashboard reachable?");
                    return;
                }
                catch (HttpRequestException ex)
                {
                    OnSyncError(target, $"Network error: {ex.Message}");
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    OnSyncError(target, $"HTTP {(int)response.StatusCode}: {body.Trim()}");
                    return;
                }

                string luaContent = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(luaContent))
                {
                    OnSyncError(target, "Dashboard returned empty content. No data to sync.");
                    return;
                }

                string outputPath = target.FullOutputPath;

                // Write atomically: temp file -> rename
                string tempPath = outputPath + ".tmp";
                await File.WriteAllTextAsync(tempPath, luaContent, System.Text.Encoding.UTF8);
                File.Move(tempPath, outputPath, overwrite: true);

                _config.UpdateLastSync(target, DateTime.Now);
                OnSyncCompleted(target, outputPath);
            }
            catch (Exception ex)
            {
                string error = $"Sync failed: {ex.Message}";
                _config.UpdateLastSync(target, DateTime.Now, error);
                OnSyncError(target, error);
            }
        }

        // ─── Event helpers ────────────────────────────────────────────────────

        private void OnSyncStarted(SyncTarget target) =>
            SyncStarted?.Invoke(this, new SyncStartedEventArgs(target));

        private void OnSyncCompleted(SyncTarget target, string outputPath) =>
            SyncCompleted?.Invoke(this, new SyncCompletedEventArgs(target, outputPath));

        private void OnSyncError(SyncTarget target, string error) =>
            SyncError?.Invoke(this, new SyncErrorEventArgs(target, error));

        // ─── IDisposable ──────────────────────────────────────────────────────

        public void Dispose()
        {
            if (!_disposed)
            {
                _autoSyncTimer?.Dispose();
                _disposed = true;
            }
        }
    }
}