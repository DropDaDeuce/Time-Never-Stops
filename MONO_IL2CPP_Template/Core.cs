using MelonLoader;
using UnityEngine;
using MelonLoader.Utils;
using System.Collections;

// Conditional compilation example for IL2CPP and MONO
// #if <Build config> is used to check the build configuration
#if IL2CPP
using Il2CppScheduleOne.NPCs; // IL2Cpp using directive
#elif MONO
using ScheduleOne.NPCs; // Mono using directive
#else
// Other build configs
#endif

[assembly: MelonInfo(typeof(MONO_IL2CPP_Template.Core), "Time Never Stops", "1.0.0", "DropDaDeuce", null)] // Change this
[assembly: MelonGame("TVGS", "Schedule I")]

namespace MONO_IL2CPP_Template
{
    public class Core : MelonMod
    {
        private MelonPreferences_Category cfgCategory;
        private MelonPreferences_Entry<float> cfgDaySpeed;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Time Never Stops Initialized.");
        }

        private IEnumerator SetDaySpeedLoop()
        {
            // Wait for TimeManager to exist
            while (Il2CppScheduleOne.GameTime.TimeManager.Instance == null)
                yield return null;
            var timeManager = Il2CppScheduleOne.GameTime.TimeManager.Instance;

            // Clamp and set the multiplier
            float multiplier = Mathf.Clamp(cfgDaySpeed.Value, 0.1f, 2.0f);
            timeManager.TimeProgressionMultiplier = multiplier;
            MelonLogger.Msg($"Day speed set to {multiplier:0.##}x ({multiplier * 100f:0.#}% of normal speed)");
        }
    }
}