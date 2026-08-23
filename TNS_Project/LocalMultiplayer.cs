using System;
using System.Collections;
using MelonLoader;
using UnityEngine;

#if !IL2CPP
using FishNet;
using FishNet.Transporting;
using Multipass = FishNet.Transporting.Multipass.Multipass;
using Tugboat = FishNet.Transporting.Tugboat.Tugboat;
using ScheduleOne.DevUtilities;
using Lobby = ScheduleOne.Networking.Lobby;
using LoadManager = ScheduleOne.Persistence.LoadManager;
#endif

namespace Time_Never_Stops
{
    /// <summary>
    /// Two instances of the game on ONE machine, in one session, with no second Steam account.
    ///
    /// THIS IS MONO ONLY, AND THAT IS A DELIBERATE DECISION BY MATHEW — NOT AN OVERSIGHT AND NOT A
    /// HALF-FINISHED PORT. `CLAUDE.md` says every change lands in both arms or it is a bug; this is
    /// the sanctioned exception, and the rule there now records it. **Do not "fix" it by porting
    /// this to IL2CPP without asking.** IL2CPP multiplayer stays Steam-only; IL2CPP single-player
    /// is unaffected either way.
    ///
    /// WHY THE RESTRICTION IS TECHNICALLY REASONABLE RATHER THAN ARBITRARY: the whole feature turns
    /// on `Multipass.GetTransport&lt;Tugboat&gt;()`, a GENERIC method. Il2CppInterop reaches generic
    /// methods through a runtime `MakeGenericMethod` on the native MethodInfo, which is precisely
    /// the class of thing that compiles clean and returns null at runtime — the failure mode this
    /// repo keeps being bitten by. **It is NOT impossible on IL2CPP, and saying so would repeat the
    /// mistake `CLAUDE.md` warns about:** the interop wrapper exposes `_transports` as a plain
    /// `public unsafe List&lt;Transport&gt;`, so a Tugboat could be found by iterating that list with
    /// no generics at all. Verified 2026-08-23 against
    /// `MelonLoader\Il2CppAssemblies\Il2CppFishNet.Runtime.dll`. If it is ever wanted, that is how.
    ///
    /// NONE OF THIS IS OUR INVENTION — THE GAME SHIPS IT AND ONLY THE SWITCH IS MISSING.
    /// `ScheduleOne.Networking.LocalMultiplayerTool.Update` binds F6 to
    /// `LoadManager.LoadAsClient("localhost")`, and `LoadManager` carries the matching transport
    /// plumbing on both sides. Both halves are gated on `Application.isEditor ||
    /// Debug.isDebugBuild`, which is false in a retail build — and `Debug.isDebugBuild` is an
    /// extern property, so Harmony cannot patch it. We do not need to:
    ///
    ///   * **Client:** `LoadManager.LoadAsClient(string)` is PUBLIC, and its body branches on the
    ///     literal string `"localhost"`. That branch sets its auth outcome to Success — **Steam
    ///     authentication is skipped entirely** — switches Multipass to Tugboat, and connects to
    ///     `localhost:38465`. There is no gate on the method itself. Calling it is the whole fix.
    ///   * **Server:** the offline-server branch picks `Yak` (in-process loopback) in a retail
    ///     build and only picks Tugboat under that same dev flag. Yak cannot be reached from
    ///     another process, so this class starts Tugboat as an ADDITIONAL server transport.
    ///     Multipass supports that by design — verified in `FishNet.Runtime.dll`:
    ///     `StartConnection(true)` loops every transport and `StartConnection(true, index)` starts
    ///     exactly one.
    ///
    /// **SAME MACHINE ONLY. THIS IS NOT LAN, AND IT CANNOT BE MADE LAN FROM HERE.** `LoadAsClient`
    /// hardcodes the literal `"localhost"` twice: once to choose the branch and once as the client
    /// address. Any other string — including a real IP — takes the FishySteamworks path instead.
    ///
    /// **WHAT A LOOPBACK SESSION CANNOT TEST, AND IT MATTERS:** there is no Steam lobby, so
    /// `TimeSyncManager` and its four `__tns_*` lobby-data keys never initialise. The clock still
    /// stays in step, because that is carried by the game's own `PassMinute_Client` ObserversRpc
    /// rather than by us. What a loopback session DOES cover is everything sleep-related —
    /// `StartSleep`, `SetHostSleepDone` and the whole silent-sleep flow are FishNet RPCs and ride
    /// whatever transport is active. `Core.IsHost` is `Player.Local.IsHost`, FishNet's own
    /// server+client test, and resolves correctly on both instances.
    /// </summary>
    internal static class LocalMultiplayer
    {
        /// <summary>
        /// The port the game itself hardcodes for its offline/local server, on both sides of the
        /// connection. Changing it would break the join, because the client half is inside
        /// LoadAsClient where we cannot reach it.
        /// </summary>
        internal const ushort LoopbackPort = 38465;

        /// <summary>
        /// Matches the key the game's own <c>LocalMultiplayerTool</c> uses. Safe to reuse: both
        /// vanilla F6 handlers are behind the dev-build gate and are inert in a retail build.
        /// </summary>
        internal const KeyCode JoinKey = KeyCode.F6;

        internal static bool Enabled => Core.cfgLocalMultiplayer?.Value == true;

#if !IL2CPP
        private static bool _serverOpened;
        private static bool _syncNoticeLogged;
        private static object _openRoutine;

        /// <summary>Called on every scene load so a session change cannot leave stale state.</summary>
        internal static void Reset()
        {
            if (_openRoutine != null)
            {
                try { MelonCoroutines.Stop(_openRoutine); } catch { }
                _openRoutine = null;
            }
            _serverOpened = false;
            _syncNoticeLogged = false;
        }

        /// <summary>Called when the gameplay scene loads. Opens the loopback port if we end up hosting.</summary>
        internal static void OnGameSceneLoaded()
        {
            if (!Enabled) return;
            _openRoutine = MelonCoroutines.Start(OpenLoopbackServerWhenReady());
        }

        /// <summary>
        /// Polled from Core.OnUpdate. First line is a config read, so this costs one bool test per
        /// frame when the feature is off — and the config hot-reloads, so it must be re-read rather
        /// than latched at startup.
        /// </summary>
        internal static void Tick()
        {
            if (!Enabled) return;
            if (!Input.GetKeyDown(JoinKey)) return;
            TryJoinLocalHost();
        }

        private static IEnumerator OpenLoopbackServerWhenReady()
        {
            // The game brings its own server up inside a load coroutine, so there is nothing to
            // attach to until it has. Bounded rather than a bare WaitUntil: a load that never
            // finishes must not leave a coroutine spinning for the rest of the process.
            const float maxWait = 180f;
            float start = Time.realtimeSinceStartup;
            while (!InstanceFinder.IsServer && Time.realtimeSinceStartup - start < maxWait)
                yield return new WaitForSecondsRealtime(0.5f);

            if (!InstanceFinder.IsServer)
            {
                // Entirely normal: this is what the JOINING instance looks like.
                TNSLog.Debug("Local multiplayer: this instance is not the server, so there is no loopback port to open.");
                _openRoutine = null;
                yield break;
            }

            // A real Steam lobby is already on FishySteamworks and needs no help from us. Starting
            // a second server transport underneath a live multiplayer session would be meddling.
            if (Singleton<Lobby>.InstanceExists && Singleton<Lobby>.Instance != null && Singleton<Lobby>.Instance.IsInLobby)
            {
                TNSLog.Msg("Local multiplayer: skipped — this session is in a Steam lobby, which already has real networking.");
                _openRoutine = null;
                yield break;
            }

            OpenLoopbackServer();
            _openRoutine = null;
        }

        private static void OpenLoopbackServer()
        {
            try
            {
                var mp = InstanceFinder.TransportManager?.GetTransport<Multipass>();
                if (mp == null)
                {
                    TNSLog.Warning("Local multiplayer: no Multipass transport on this build — cannot open the loopback port.");
                    return;
                }

                var tug = mp.GetTransport<Tugboat>();
                if (tug == null)
                {
                    // THE ONE THING THAT COULD NOT BE PROVEN WITHOUT RUNNING THE GAME. The game's
                    // own code calls GetTransport<Tugboat>() in two places, so it should be in the
                    // prefab's transport list — but "should" is not "is", and if it is absent the
                    // whole feature is impossible rather than broken. Say so plainly.
                    TNSLog.Error("Local multiplayer: Tugboat is not in this build's Multipass transport list, " +
                                 "so a second instance cannot connect. Nothing else about the mod is affected.");
                    return;
                }

                if (tug.GetConnectionState(server: true) == LocalConnectionState.Started)
                {
                    TNSLog.Debug("Local multiplayer: the loopback port is already open.");
                    _serverOpened = true;
                    return;
                }

                tug.SetPort(LoopbackPort);
                if (!tug.StartConnection(server: true))
                {
                    TNSLog.Error($"Local multiplayer: Tugboat refused to listen on port {LoopbackPort}. " +
                                 "Something else may already hold it.");
                    return;
                }

                _serverOpened = true;
                TNSLog.Msg($"Local multiplayer: listening on 127.0.0.1:{LoopbackPort}. " +
                           $"Press {JoinKey} in the OTHER instance to join this session.");
                LogSyncNoticeOnce();
            }
            catch (Exception ex)
            {
                TNSLog.Error($"Local multiplayer: could not open the loopback port; the session is unaffected: {ex}");
            }
        }

        private static void TryJoinLocalHost()
        {
            try
            {
                var lm = Singleton<LoadManager>.InstanceExists ? Singleton<LoadManager>.Instance : null;
                if (lm == null)
                {
                    TNSLog.Warning("Local multiplayer: no LoadManager yet — wait for the main menu and try again.");
                    return;
                }

                if (lm.IsLoading)
                {
                    TNSLog.Debug("Local multiplayer: already loading; ignoring the join key.");
                    return;
                }

                if (InstanceFinder.IsServer)
                {
                    // Guard against the obvious mistake, which is pressing the key in the wrong
                    // window. LoadAsClient would exit this session to the menu first and take the
                    // host down with it, stranding the other instance.
                    TNSLog.Warning($"Local multiplayer: this instance IS the host — press {JoinKey} in the other one. Ignoring.");
                    return;
                }

                TNSLog.Msg($"Local multiplayer: joining 127.0.0.1:{LoopbackPort} as a client...");
                LogSyncNoticeOnce();

                // "localhost" is a magic string inside LoadAsClient, not a hostname we chose: it is
                // what selects the Tugboat branch and skips Steam authentication. See the class note.
                lm.LoadAsClient("localhost");
            }
            catch (Exception ex)
            {
                TNSLog.Error($"Local multiplayer: join failed; the session is unaffected: {ex}");
            }
        }

        /// <summary>
        /// Says out loud what a loopback session cannot do, once per session, at Msg level.
        ///
        /// Worth the log line: the first thing anyone will do with this is watch the clocks and
        /// conclude the sync is broken. It is not — it is absent, because there is no Steam lobby
        /// for TimeSyncManager to write its four __tns_* keys into.
        /// </summary>
        private static void LogSyncNoticeOnce()
        {
            if (_syncNoticeLogged) return;
            _syncNoticeLogged = true;
            TNSLog.Msg("Local multiplayer: there is no Steam lobby here, so TimeSyncManager's lobby-data sync " +
                       "is INACTIVE. The clock still tracks the host through the game's own PassMinute_Client RPC. " +
                       "Keep DaySpeedMultiplier identical in both instances — the host cannot push it without a lobby.");
        }
#else
        // IL2CPP: deliberately absent. See the class note — multiplayer on this arm is Steam-only.
        internal static void Reset() { }
        internal static void OnGameSceneLoaded() { }
        internal static void Tick() { }
#endif
    }
}
