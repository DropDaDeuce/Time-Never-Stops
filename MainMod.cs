using System.Collections;
using MelonLoader;
using Time_Never_Stops.Helpers;
using UnityEngine;
#if MONO
using FishNet;
#else
using Il2CppFishNet;
#endif

[assembly: MelonInfo(
    typeof(Time_Never_Stops.Time_Never_Stops),
    Time_Never_Stops.BuildInfo.Name,
    Time_Never_Stops.BuildInfo.Version,
    Time_Never_Stops.BuildInfo.Author
)]
[assembly: MelonColor(1, 255, 0, 0)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Time_Never_Stops
{
    public static class BuildInfo
    {
        public const string Name = "Time Never Stops";
        public const string Description = "Removes the 4am time stop. Adds config for time speed.";
        public const string Author = "DropDaDeuce";
        public const string Version = "1.0.0";
    }

    public class Time_Never_Stops : MelonMod
    {
        private static MelonLogger.Instance Logger;

        public override void OnInitializeMelon()
        {
            Logger = LoggerInstance;
            Logger.Msg("Time Never Stops initialized");
            //Logger.Debug("This will only show in debug mode");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Logger.Debug($"Scene loaded: {sceneName}");
            if (sceneName == "Main")
            {
                Logger.Debug("Main scene loaded, waiting for player");
                MelonCoroutines.Start(Utils.WaitForPlayer(DoStuff()));

                Logger.Debug("Main scene loaded, waiting for network");
                MelonCoroutines.Start(Utils.WaitForNetwork(DoNetworkStuff()));
            }
        }

        private IEnumerator DoStuff()
        {
            Logger.Msg("Player ready, doing stuff...");
            yield return new WaitForSeconds(2f);
            Logger.Msg("Did some stuff!");
        }

        private IEnumerator DoNetworkStuff()
        {
            Logger.Msg("Network ready, doing network stuff...");
            var nm = InstanceFinder.NetworkManager;
            if (nm.IsServer && nm.IsClient)
                Logger.Debug("Host");
            else if (!nm.IsServer && !nm.IsClient)
                Logger.Debug("Singleplayer");
            else if (nm.IsClient && !nm.IsServer)
                Logger.Debug("Client-only");
            else if (nm.IsServer && !nm.IsClient)
                Logger.Debug("Server-only");
            yield return new WaitForSeconds(2f);
            Logger.Msg("Did some network stuff!");
        }
    }
}
