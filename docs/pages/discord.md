# Discord

## Overview

The Discord page controls the League and Valorant rich presence activities.

## Functions

- Enable or disable both activities.
- Choose which match details and account statistics are included.
- Preview the League and Valorant layouts.

## Options

- `Show League and Valorant on Discord`: publish activities while either game is open.
- `Queue, map, and selected champion or agent`: include the current lobby or match details.
- `Round score`: include the Valorant ally-enemy round score, such as 0-1, or the live League team score.
- `K/D`: include the signed-in League player's live kills and deaths while a match is in progress. Valorant does not expose this value.
- `Valorant party join button`: create the Valorant party link automatically and show it as a Discord button. This is off by default. Discord allows two buttons, so the join button replaces the GitHub button. The choice is saved immediately.
- `Riot ID`, `Account level`, `Region`, `Rank`, `Currencies`, and `Collection counts`: optional account statistics, off by default.

The current situation is always shown first: client open, lobby, queue, champion or agent select, or in game. A Valorant lobby also shows its queue and party size, such as Swiftplay and 1/5. League shows the same party size when its lobby reports the members. K/D is available for League while the local live game is running; Valorant does not expose live kills and deaths, so that value stays hidden there. When the party join option is enabled, the Valorant party link is created automatically and shown as a Join party button. A live match can temporarily block the League client and Valorant party requests. The last activity stays visible, while League score and K/D continue from the local live game. League and Valorant can appear at the same time. During Valorant agent select and in game, the selected agent's uploaded portrait is the large icon and the Valorant artwork is the small icon. Discord must be running, and the activities remain visible only while League Account Manager stays open. Passwords and party codes are never written to logs.

## Technical details

- View: `views/DiscordPresence.xaml`
- Code-behind: `views/DiscordPresence.xaml.cs`
- Publisher: `Misc/DiscordPresenceService.cs`
