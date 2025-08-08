using MelonLoader;
using UnityEngine;
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

[assembly: MelonInfo(typeof(Time_Never_Stops.Core), "Time Never Stops", "1.0.0", "DropDaDeuce", null)] // Change this
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Time_Never_Stops
{
    public class Core : MelonMod
    {
        private MelonPreferences_Category cfgCategory;
        private MelonPreferences_Entry<float> cfgDaySpeed;

        public override void OnInitializeMelon()
        {
            cfgCategory = MelonPreferences.CreateCategory("TimeNeverStops", "Time Never Stops Settings");
            cfgDaySpeed = cfgCategory.CreateEntry<float>(
                "DaySpeedMultiplier",
                1.0f,
                "Day Speed Multiplier",
                "Sets the in-game time speed multiplier. 1.0 = normal speed, 0.5 = half speed, 2.0 = double speed.\nMinimum 0.1, Maximum 3.0. Default = 1.0 (Vanilla Game Speed)",
                false, false
            );
            MelonCoroutines.Start(SetDaySpeedLoop());
            LoggerInstance.Msg("Time Never Stops Initialized.");
        }

        private IEnumerator SetDaySpeedLoop()
        {
            // Wait for TimeManager to exist
            while (Il2CppScheduleOne.GameTime.TimeManager.Instance == null)
                yield return null;
            var timeManager = Il2CppScheduleOne.GameTime.TimeManager.Instance;

            // Clamp and set the multiplier
            float multiplier = Mathf.Clamp(cfgDaySpeed.Value, 0.1f, 5.0f);
            timeManager.TimeProgressionMultiplier = multiplier;
            LoggerInstance.Msg($"Day speed set to {multiplier:0.##}x ({multiplier * 100f:0.#}% of normal speed)");
        }
    }
}