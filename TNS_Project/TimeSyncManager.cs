using System;
using System.Collections;
using SteamNetworkLib;
using SteamNetworkLib.Events;
using UnityEngine;
using MelonLoader;

#if IL2CPP
using TimeManager = Il2CppScheduleOne.GameTime.TimeManager;
#else // MONO
using TimeManager = ScheduleOne.GameTime.TimeManager;
#endif

namespace Time_Never_Stops
{
    /// <summary>
    /// Manages time synchronization between host and clients using Steam lobby data.
    /// Host periodically updates lobby data with current time, and clients sync to it.
    /// </summary>
    public class TimeSyncManager : IDisposable
    {
        private SteamNetworkClient? _steamClient;
        private bool _disposed = false;
        private bool _isInitialized = false;
        private object? _syncCoroutine;
        private object? _readCoroutine;

        // Lobby data keys for time synchronization
        private const string LOBBY_DATA_KEY_CURRENT_TIME = "__tns_current_time";
        private const string LOBBY_DATA_KEY_ELAPSED_DAYS = "__tns_elapsed_days";
        private const string LOBBY_DATA_KEY_TIME_UPDATE_TIMESTAMP = "__tns_time_update_ts";
        private const string LOBBY_DATA_KEY_TIME_MULTIPLIER = "__tns_time_multiplier";

        // Synchronization settings
        private const float HOST_CHECK_INTERVAL = 5.0f; // Host checks time every 5 seconds (only syncs on hour change)
        private const float CLIENT_SYNC_INTERVAL = 10.0f; // Clients check for updates every 10 seconds
        private const float MULTIPLIER_SYNC_INTERVAL = 5.0f; // Check multiplier changes every 5 seconds
        private const float MAX_TIME_DIFF_THRESHOLD = 5.0f; // Only sync if time difference is > 5 minutes

        private int _lastSyncedCurrentTime = -1;
        private int _lastSyncedElapsedDays = -1;
        private int _lastSyncedHour = -1; // Track last synced hour (0-23)
        private float _lastSyncTimestamp = 0f;
        
        // Multiplier sync tracking
        private float _lastSyncedMultiplier = float.NaN;
        private float _lastMultiplierCheckTime = 0f;
        
        // Client-side tracking: last host time we synced to (prevents repeated syncing to same value)
        private int _lastSyncedHostTime = -1;
        private int _lastSyncedHostDays = -1;
        
        // Client-side tracking: last host multiplier we synced to (prevents repeated syncing to same value)
        private float _lastSyncedHostMultiplier = float.NaN;
        
        // Callback to get host's time multiplier (set by Core)
        private Func<float>? _getHostMultiplierCallback;
        
        // Client's original multiplier (to restore when leaving lobby)
        private float? _clientOriginalMultiplier = null;

        /// <summary>
        /// Gets whether the manager is initialized and ready.
        /// </summary>
        public bool IsInitialized => _isInitialized && !_disposed;

        /// <summary>
        /// Gets whether the local player is currently in a lobby.
        /// </summary>
        public bool IsInLobby => _steamClient?.IsInLobby ?? false;

        /// <summary>
        /// Gets whether the local player is the host.
        /// </summary>
        public bool IsHost => _steamClient?.IsHost ?? false;

        /// <summary>
        /// Sets the callback function to get the host's time multiplier.
        /// This should be set by Core to provide the current multiplier value.
        /// </summary>
        public void SetHostMultiplierCallback(Func<float> callback)
        {
            _getHostMultiplierCallback = callback;
        }

        /// <summary>
        /// Forces an immediate sync of the host's multiplier to lobby data.
        /// Called when the host changes their multiplier setting.
        /// </summary>
        public void SyncHostMultiplierNow()
        {
            if (IsHost && IsInLobby)
            {
                // Reset tracking to force sync regardless of cached value
                _lastSyncedMultiplier = float.NaN;
                SyncHostMultiplierToLobby();
            }
        }

        /// <summary>
        /// Initializes the time synchronization manager.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized || _disposed)
                return;

            try
            {
                _steamClient = new SteamNetworkClient();
                
                if (!_steamClient.Initialize())
                {
                    TNSLog.Warning("Failed to initialize SteamNetworkClient for time sync");
                    return;
                }

                // Subscribe to lobby events
                _steamClient.OnLobbyJoined += OnLobbyJoined;
                _steamClient.OnLobbyCreated += OnLobbyCreated;
                _steamClient.OnLobbyLeft += OnLobbyLeft;
                _steamClient.OnLobbyDataChanged += OnLobbyDataChanged;

                _isInitialized = true;
                TNSLog.Debug("TimeSyncManager initialized");

                // Start sync coroutines if already in a lobby
                if (_steamClient.IsInLobby)
                {
                    StartSyncCoroutines();
                }
            }
            catch (Exception ex)
            {
                TNSLog.Error($"Failed to initialize TimeSyncManager: {ex.Message}");
            }
        }

        /// <summary>
        /// Processes incoming Steam messages. Should be called regularly (e.g., in Update loop).
        /// </summary>
        public void Update()
        {
            if (!_isInitialized || _disposed)
                return;

            try
            {
                _steamClient?.ProcessIncomingMessages();
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"Error processing Steam messages in TimeSyncManager: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts the synchronization coroutines for host and client.
        /// </summary>
        private void StartSyncCoroutines()
        {
            if (TimeManager.Instance == null)
            {
                TNSLog.Debug("TimeManager.Instance is null, waiting...");
                MelonCoroutines.Start(WaitForTimeManagerAndStartSync());
                return;
            }

            if (IsHost)
            {
                // Host: Periodically update lobby data with current time
                if (_syncCoroutine != null)
                    MelonCoroutines.Stop(_syncCoroutine);
                
                _syncCoroutine = MelonCoroutines.Start(HostUpdateTimeLoop());
                TNSLog.Debug("Started host time sync loop");
            }
            else
            {
                // Client: Periodically read lobby data and sync time
                if (_readCoroutine != null)
                    MelonCoroutines.Stop(_readCoroutine);
                
                _readCoroutine = MelonCoroutines.Start(ClientSyncTimeLoop());
                TNSLog.Debug("Started client time sync loop");
            }
        }

        /// <summary>
        /// Waits for TimeManager.Instance to be available before starting sync.
        /// </summary>
        private IEnumerator WaitForTimeManagerAndStartSync()
        {
            TNSLog.Debug("TimeSyncManager: waiting for TimeManager before starting sync coroutines...");
            while (TimeManager.Instance == null)
            {
                yield return null;
            }
            TNSLog.Debug("TimeSyncManager: TimeManager ready, starting sync coroutines.");
            StartSyncCoroutines();
        }

        /// <summary>
        /// Host coroutine: Periodically checks time and updates lobby data only when hour changes.
        /// Optimized to minimize expensive SetLobbyData calls.
        /// </summary>
        private IEnumerator HostUpdateTimeLoop()
        {
            var waitTime = new WaitForSecondsRealtime(HOST_CHECK_INTERVAL);

            while (IsInLobby && IsHost && TimeManager.Instance != null)
            {
                try
                {
                    var tm = TimeManager.Instance;
                    int currentTime = tm.CurrentTime;
                    int elapsedDays = tm.ElapsedDays;
                    
                    // Extract hour component (0-23) from HHMM format
                    int currentHour = (int)(currentTime / 100);

                    // Only sync when:
                    // 1. Hour has changed (hour pass occurred)
                    // 2. Days have changed
                    // 3. First sync (initial state)
                    bool hourChanged = _lastSyncedHour != currentHour;
                    bool daysChanged = _lastSyncedElapsedDays != elapsedDays;
                    bool isFirstSync = _lastSyncedCurrentTime == -1;
                    
                    bool shouldUpdate = isFirstSync || hourChanged || daysChanged;

                    if (shouldUpdate)
                    {
                        // Cache string conversions to avoid repeated allocations
                        string currentTimeStr = currentTime.ToString();
                        string elapsedDaysStr = elapsedDays.ToString();
                        string timestampStr = Time.realtimeSinceStartup.ToString("F2");
                        
                        _steamClient?.SetLobbyData(LOBBY_DATA_KEY_CURRENT_TIME, currentTimeStr);
                        _steamClient?.SetLobbyData(LOBBY_DATA_KEY_ELAPSED_DAYS, elapsedDaysStr);
                        _steamClient?.SetLobbyData(LOBBY_DATA_KEY_TIME_UPDATE_TIMESTAMP, timestampStr);

                        _lastSyncedCurrentTime = currentTime;
                        _lastSyncedElapsedDays = elapsedDays;
                        _lastSyncedHour = currentHour;
                        _lastSyncTimestamp = Time.realtimeSinceStartup;

                        TNSLog.Debug($"Host updated lobby time: {currentTime} (Hour: {currentHour}), Days: {elapsedDays}");
                    }
                }
                catch (Exception ex)
                {
                    TNSLog.Warning($"Error in host time update loop: {ex.Message}");
                }

                // Check multiplier periodically (only sync when changed)
                float currentTimeRealtime = Time.realtimeSinceStartup;
                if (currentTimeRealtime - _lastMultiplierCheckTime >= MULTIPLIER_SYNC_INTERVAL)
                {
                    _lastMultiplierCheckTime = currentTimeRealtime;
                    try
                    {
                        SyncHostMultiplierToLobby();
                    }
                    catch (Exception ex)
                    {
                        TNSLog.Warning($"Error syncing host multiplier: {ex.Message}");
                    }
                }

                yield return waitTime;
            }
        }

        /// <summary>
        /// Syncs the host's time multiplier to lobby data.
        /// Only syncs when the multiplier value actually changes.
        /// </summary>
        private void SyncHostMultiplierToLobby()
        {
            if (!IsHost || _steamClient == null || TimeManager.Instance == null)
                return;

            try
            {
                float hostMultiplier = _getHostMultiplierCallback?.Invoke() ?? TimeManager.Instance.TimeSpeedMultiplier;
                
                // Only sync if multiplier changed (with small epsilon for floating point comparison)
                if (float.IsNaN(_lastSyncedMultiplier) || Mathf.Abs(_lastSyncedMultiplier - hostMultiplier) > 0.0001f)
                {
                    string multiplierStr = hostMultiplier.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    _steamClient.SetLobbyData(LOBBY_DATA_KEY_TIME_MULTIPLIER, multiplierStr);
                    _lastSyncedMultiplier = hostMultiplier;
                    TNSLog.Debug($"Host synced multiplier to lobby: {hostMultiplier:0.########}x");
                }
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"Error syncing host multiplier to lobby: {ex.Message}");
            }
        }

        /// <summary>
        /// Client: Syncs local time multiplier to match host's multiplier from lobby data.
        /// Only syncs when the multiplier value actually changes.
        /// </summary>
        private void SyncToHostMultiplier()
        {
            if (IsHost || _steamClient == null || TimeManager.Instance == null)
                return;

            try
            {
                string? multiplierStr = _steamClient.GetLobbyData(LOBBY_DATA_KEY_TIME_MULTIPLIER);
                
                if (!string.IsNullOrEmpty(multiplierStr) &&
                    float.TryParse(multiplierStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hostMultiplier) &&
                    float.IsFinite(hostMultiplier) && hostMultiplier > 0)
                {
                    // Only sync if multiplier value has changed
                    bool multiplierChanged = float.IsNaN(_lastSyncedHostMultiplier) || 
                                           Mathf.Abs(_lastSyncedHostMultiplier - hostMultiplier) > 0.0001f;
                    
                    if (!multiplierChanged)
                        return; // Already synced to this value, skip
                    
                    var tm = TimeManager.Instance;
                    
                    // Save original multiplier if not already saved
                    if (_clientOriginalMultiplier == null)
                    {
                        _clientOriginalMultiplier = tm.TimeSpeedMultiplier;
                    }

                    // Apply host's multiplier directly — SetTimeSpeedMultiplier() has a
                    // server-only guard in the new assembly, so we bypass it via TMAccess.
                    TMAccess.SetTimeSpeedMultiplierDirect(tm, hostMultiplier);
                    _lastSyncedHostMultiplier = hostMultiplier;
                    
                    TNSLog.Msg($"Synced time multiplier to host: {hostMultiplier:0.########}x (was {_clientOriginalMultiplier.Value:0.########}x)");
                    TNSLog.Debug($"[Config Sync] Client received and applied host time multiplier config: {hostMultiplier:0.########}x");
                }
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"Error syncing to host multiplier: {ex.Message}");
            }
        }

        /// <summary>
        /// Client coroutine: Periodically reads lobby data and syncs local time to host time.
        /// Optimized to check less frequently and batch operations.
        /// Only syncs when host's lobby data has changed (new hour).
        /// </summary>
        private IEnumerator ClientSyncTimeLoop()
        {
            var waitTime = new WaitForSecondsRealtime(CLIENT_SYNC_INTERVAL);

            while (IsInLobby && !IsHost && TimeManager.Instance != null)
            {
                try
                {
                    var tm = TimeManager.Instance;

                    // Read time data from lobby (only when needed)
                    string? currentTimeStr = _steamClient?.GetLobbyData(LOBBY_DATA_KEY_CURRENT_TIME);
                    string? elapsedDaysStr = _steamClient?.GetLobbyData(LOBBY_DATA_KEY_ELAPSED_DAYS);

                    if (!string.IsNullOrEmpty(currentTimeStr) && 
                        !string.IsNullOrEmpty(elapsedDaysStr) &&
                        int.TryParse(currentTimeStr, out int hostCurrentTime) &&
                        int.TryParse(elapsedDaysStr, out int hostElapsedDays))
                    {
                        // Only sync if host's lobby data has changed (new hour update from host)
                        bool hostDataChanged = (_lastSyncedHostTime != hostCurrentTime) || 
                                             (_lastSyncedHostDays != hostElapsedDays);
                        bool isFirstSync = _lastSyncedHostTime == -1;
                        
                        if (hostDataChanged)
                        {
                            int localCurrentTime = tm.CurrentTime;
                            int localElapsedDays = tm.ElapsedDays;

                            // Convert HHMM to total minutes before diffing — raw HHMM subtraction
                            // gives wrong results across hour boundaries (e.g. 059 vs 100 = 41, not 1).
                            int localMinutes = (localCurrentTime / 100) * 60 + (localCurrentTime % 100);
                            int hostMinutes  = (hostCurrentTime  / 100) * 60 + (hostCurrentTime  % 100);
                            int timeDiff  = Mathf.Abs(localMinutes - hostMinutes);
                            int daysDiff  = Mathf.Abs(localElapsedDays - hostElapsedDays);

                            // Only sync if there's a significant difference, days don't match, or first sync
                            bool needsSync = isFirstSync || (timeDiff > MAX_TIME_DIFF_THRESHOLD) || (daysDiff > 0);

                            if (needsSync)
                            {
                                // Sync time to host
                                TMAccess.SetCurrentTime(tm, hostCurrentTime);
                                
                                if (daysDiff > 0 || isFirstSync)
                                {
                                    TMAccess.SetElapsedDays(tm, hostElapsedDays);
                                }

                                // Update daily minute total
                                TMAccess.SetDailyMinSum(tm, TimeManager.GetMinSumFrom24HourTime(hostCurrentTime));

                                // Update tracking to prevent repeated syncing to same value
                                _lastSyncedHostTime = hostCurrentTime;
                                _lastSyncedHostDays = hostElapsedDays;

                                TNSLog.Debug($"Client synced time to host: {localCurrentTime} -> {hostCurrentTime}, Days: {localElapsedDays} -> {hostElapsedDays}");
                            }
                            else
                            {
                                // Host data changed but difference is within threshold — just update tracking
                                TNSLog.Debug($"Client skipped sync: timeDiff={timeDiff}min <= {MAX_TIME_DIFF_THRESHOLD}, daysDiff={daysDiff}. (local={localCurrentTime:D4}, host={hostCurrentTime:D4})");
                                _lastSyncedHostTime = hostCurrentTime;
                                _lastSyncedHostDays = hostElapsedDays;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TNSLog.Warning($"Error in client time sync loop: {ex.Message}");
                }

                yield return waitTime;
            }
        }

        /// <summary>
        /// Called when lobby data changes.
        /// </summary>
        private void OnLobbyDataChanged(object? sender, LobbyDataChangedEventArgs e)
        {
            if (_disposed || !IsInitialized)
                return;

            // If client and time-related data changed, sync immediately
            if (!IsHost && TimeManager.Instance != null)
            {
                if (e.Key == LOBBY_DATA_KEY_CURRENT_TIME || 
                    e.Key == LOBBY_DATA_KEY_ELAPSED_DAYS ||
                    e.Key == LOBBY_DATA_KEY_TIME_UPDATE_TIMESTAMP)
                {
                    try
                    {
                        var tm = TimeManager.Instance;
                        string? currentTimeStr = _steamClient?.GetLobbyData(LOBBY_DATA_KEY_CURRENT_TIME);
                        string? elapsedDaysStr = _steamClient?.GetLobbyData(LOBBY_DATA_KEY_ELAPSED_DAYS);

                        if (!string.IsNullOrEmpty(currentTimeStr) && 
                            !string.IsNullOrEmpty(elapsedDaysStr) &&
                            int.TryParse(currentTimeStr, out int hostCurrentTime) &&
                            int.TryParse(elapsedDaysStr, out int hostElapsedDays))
                        {
                            // Only sync if host's lobby data has changed (new hour update from host)
                            bool hostDataChanged = (_lastSyncedHostTime != hostCurrentTime) || 
                                                 (_lastSyncedHostDays != hostElapsedDays);
                            
                            if (hostDataChanged)
                            {
                                TMAccess.SetCurrentTime(tm, hostCurrentTime);
                                TMAccess.SetElapsedDays(tm, hostElapsedDays);
                                TMAccess.SetDailyMinSum(tm, TimeManager.GetMinSumFrom24HourTime(hostCurrentTime));

                                // Update tracking to prevent repeated syncing to same value
                                _lastSyncedHostTime = hostCurrentTime;
                                _lastSyncedHostDays = hostElapsedDays;

                                TNSLog.Debug($"Client synced time immediately on lobby data change: {hostCurrentTime}, Days: {hostElapsedDays}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TNSLog.Warning($"Error syncing time on lobby data change: {ex.Message}");
                    }
                }
                else if (e.Key == LOBBY_DATA_KEY_TIME_MULTIPLIER)
                {
                    // Client: Sync multiplier when host changes it
                    SyncToHostMultiplier();
                }
            }
        }

        /// <summary>
        /// Called when a lobby is joined.
        /// </summary>
        private void OnLobbyJoined(object? sender, LobbyJoinedEventArgs e)
        {
            TNSLog.Debug($"Lobby joined. IsHost={IsHost}, TM ready={TimeManager.Instance != null}.");

            if (!IsHost && TimeManager.Instance != null)
            {
                // Client: Sync to host's multiplier
                string? existingMultiplier = _steamClient?.GetLobbyData(LOBBY_DATA_KEY_TIME_MULTIPLIER);
                TNSLog.Debug($"Client joined lobby. Host multiplier in lobby data: '{existingMultiplier}'.");
                SyncToHostMultiplier();
            }
            else if (IsHost)
            {
                // Host: Set multiplier in lobby data
                TNSLog.Debug($"Host joined/created lobby. Broadcasting multiplier.");
                SyncHostMultiplierToLobby();
            }

            StartSyncCoroutines();
        }

        /// <summary>
        /// Called when a lobby is created.
        /// </summary>
        private void OnLobbyCreated(object? sender, LobbyCreatedEventArgs e)
        {
            TNSLog.Debug($"Lobby created. IsHost={IsHost}.");

            // Host: Set multiplier in lobby data
            if (IsHost)
            {
                SyncHostMultiplierToLobby();
            }

            StartSyncCoroutines();
        }

        /// <summary>
        /// Called when the lobby is left.
        /// </summary>
        private void OnLobbyLeft(object? sender, LobbyLeftEventArgs e)
        {
            // Only log if we were actually in a lobby (sender == null means called from Dispose cleanup)
            if (sender != null)
                TNSLog.Debug("Lobby left, stopping time sync");
            
            if (_syncCoroutine != null)
            {
                MelonCoroutines.Stop(_syncCoroutine);
                _syncCoroutine = null;
            }

            if (_readCoroutine != null)
            {
                MelonCoroutines.Stop(_readCoroutine);
                _readCoroutine = null;
            }

            // Restore client's original multiplier if it was overridden
            if (_clientOriginalMultiplier.HasValue && TimeManager.Instance != null)
            {
                try
                {
                    TMAccess.SetTimeSpeedMultiplierDirect(TimeManager.Instance, _clientOriginalMultiplier.Value);
                    TNSLog.Msg($"Restored original time multiplier: {_clientOriginalMultiplier.Value:0.########}x");
                }
                catch (Exception ex)
                {
                    TNSLog.Warning($"Error restoring original multiplier: {ex.Message}");
                }
                _clientOriginalMultiplier = null;
            }

            _lastSyncedCurrentTime = -1;
            _lastSyncedElapsedDays = -1;
            _lastSyncedHour = -1;
            _lastSyncTimestamp = 0f;
            _lastSyncedMultiplier = float.NaN;
            _lastMultiplierCheckTime = 0f;
            _lastSyncedHostTime = -1;
            _lastSyncedHostDays = -1;
            _lastSyncedHostMultiplier = float.NaN;
        }

        /// <summary>
        /// Disposes the time synchronization manager and cleans up resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                OnLobbyLeft(null, null!); // Stop coroutines

                if (_steamClient != null)
                {
                    _steamClient.OnLobbyJoined -= OnLobbyJoined;
                    _steamClient.OnLobbyCreated -= OnLobbyCreated;
                    _steamClient.OnLobbyLeft -= OnLobbyLeft;
                    _steamClient.OnLobbyDataChanged -= OnLobbyDataChanged;
                    _steamClient.Dispose();
                    _steamClient = null;
                }
            }
            catch (Exception ex)
            {
                TNSLog.Warning($"Error disposing TimeSyncManager: {ex.Message}");
            }

            _disposed = true;
            _isInitialized = false;
        }
    }
}