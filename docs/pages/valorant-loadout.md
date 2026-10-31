# Valorant Loadout

## Overview

Shows the weapon skins equipped on the Valorant account currently signed in to the Riot Client, and lets each weapon be switched to another skin that account owns.

## Functions

- Reads the live player loadout from Valorant.
- Shows the equipped weapons as a Valorant collection grid, grouped into sidearms, SMGs, shotguns, rifles, snipers, machine guns, and melee.
- Clicking a weapon opens its owned skins. Choosing one only changes the grid. Apply changes equips the skins shown.
- Shows every playable agent, plus Random, in a three-column grid on the left. Random is drawn as the dice card because the agent list has no random portrait.
- Clicking an agent shows that agent's saved skins in the grid. The live Valorant loadout is not changed.
- Named sets belong to the signed-in account. Apply only equips skins and chromas that account owns.
- Chromas the account does not own are not shown. The base chroma of an owned skin is still shown.
- Equipping a skin uses the highest upgrade level that account owns, not the fully upgraded level.
- An agent with no saved loadout uses the standard skin on every weapon until one is saved.
- Applies a saved agent loadout automatically when that agent is selected. Before that change, the current loadout is stored as the account's Default set. When the match loads, Default is equipped again so the account loadout is not left on the agent set. Named sets are not applied automatically.

## Options

- Refresh reloads the signed-in account, owned skins, and equipped loadout.
- Apply changes equips the skins currently shown. Choosing a skin does not change Valorant until this is pressed.
- Save for agent stores the skins currently shown for the agent chosen on the left. If none is chosen, it uses the agent selected in the live lobby.
- Reset agent clears the saved loadout for the agent chosen on the left. That agent uses the standard skins again. It does not change the live loadout.
- Save as set stores the skins currently shown under a name. It does not equip them.
- Update replaces the selected set with the skins currently shown. It does not equip them.
- Remove deletes the selected set.
- Apply equips the set selected under Sets. Choosing the set only shows it.
- Apply saved loadout in agent select turns the automatic equip on or off. It only applies agent loadouts, then restores the Default set when the match loads.

## Tutorial

1. Sign in to Valorant through the Riot Client.
2. Open Valorant loadout and choose Refresh.
3. Pick another owned skin for any weapon, then choose Apply changes to equip the skins shown.
4. Choose an agent on the left to view its saved skins, then choose Save for agent to store the skins shown. Reset agent clears that saved loadout.
5. Choose Save as set to keep the skins shown as a named set. Select the set to view it, then choose Apply to equip it. Update stores the skins now shown into that set. Remove deletes it.
6. Leave Apply saved loadout in agent select enabled. The next time that agent is selected, those skins are equipped. When the match loads, the loadout from before agent select is equipped again.

## Technical details

- View: `views/ValorantLoadout.xaml`
- Code-behind: `views/ValorantLoadout.xaml.cs`
- Service: `Misc/ValorantLoadoutService.cs`
- Automatic apply: `Misc/ValorantLoadoutAutoApplyService.cs`
- Saved agent loadouts and named sets are stored per account in `%AppData%\League Account Manager\valorant-loadouts.json`.
- Live data uses the Valorant player-loadout (`personalization/v3`), entitlements, and pre-game endpoints with the same Riot Client tokens as the Valorant accounts page. Names and images come from valorant-api.com.
- The player-data shard comes from `pp.c` in the `/entitlements/v1/token` access token. The Riot Client region locale is the League region and is not used.
