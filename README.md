# Time Never Stops

**A mod for Schedule I that keeps time moving.**  
If you run into any issues, please report them either in the modding discord or on my GitHub linked above.

## Disclaimer
- **AI Content:** Out of respect for those that wish to avoid AI content. This mod was created with the assistance of AI tools. The Icon was generated using ChatGPT v5.

## Features

- **Time Never Stops:** Removes the default time freeze at 4:00 AM. The game clock continues to advance, allowing for uninterrupted gameplay.
- **Custom Day Speed:** Supports in-game time multipliers from 0.1x to 100.0x.
- **Config File Support:** Settings are stored in 'UserData/DropDaDeuce-TimeNeverStops/TimeNeverStops.cfg' for easy manual editing.
- **Live Config Reload:** Changes made to the config file while the game is running are detected and applied automatically, without restarting.
- **Daily Summary Options:**  
  - Config option 'EnableDailySummaryAwake' lets you choose whether the daily summary appears at 7:00 AM while awake.  
  - Summary still appears after sleep if awake summaries are disabled.

## Installation

1. **Requirements:**  
   - [MelonLoader](https://github.com/LavaGang/MelonLoader/releases)  
   - Schedule I (Legit copy of the game.)

2. **Install:**  
   - Download the latest release from Thunderstore or Nexus Mods.  
   - Place 'TimeNeverStops.dll' in your game's 'Mods' folder (e.g., 'Schedule I/Mods/').

3. **Configure:**  
   - Edit the config file at 'UserData/DropDaDeuce-TimeNeverStops/TimeNeverStops.cfg'.  
   - The mod will automatically apply changes while the game is running.

## Usage

- **Normal Sleep:**  
  Sleep between 6 PM and 4 AM. Time will not freeze, and you will wake at 7:00 AM.  
  If 'EnableDailySummaryAwake' is **enabled**, the daily summary will appear at 7:00 AM whether you slept or not.  
  If **disabled**, the summary will only appear after sleeping.

- **Skipping Sleep:**  
  If awake summaries are enabled, the daily summary will still appear at 7:00 AM without sleeping.  
  If disabled, no summary will appear unless you sleep.

- **Changing Day Speed:**  
  Edit the config file to set your preferred time progression speed (0.1x-100.0x).  
  Save the file - the change applies in-game within a second.

## Compatibility

- Designed for Schedule I (IL2CPP build).  
- May conflict with other mods that patch time or sleep mechanics.

## Credits
- **Source:** [GitHub](https://github.com/DropDaDeuce/Time-Never-Stops)  
- **Deeej:** For providing a wonderful template: [GitHub](https://github.com/weedeej/S1MONO_IL2CPP_Template)