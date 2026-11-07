# Discord Presence

## Overview

The Discord Presence page is in the main navigation, after Ingame settings. It controls the League and Valorant rich presence activities. The footer item named Join Discord for Support opens the support server and is separate from this page.

## Functions

- Enable or disable both activities.
- Choose which match details and account statistics are included.
- Preview the League and Valorant layouts.

## Options

- `Show League and Valorant on Discord`: publish activities while either game is open.
- `Queue, map, and selected champion or agent`: include the current lobby or match details.
- `Round score`: include the Valorant ally-enemy round score, such as 0-1, or the live League team score.
- `K/D`: include the signed-in League player's live kills and deaths while a match is in progress. Valorant does not expose this value.
- `Join party button`: create the Valorant party link automatically and show it as a Discord button. This is off by default.
- `Tracker button`: show a Tracker.gg button for the signed-in account when its Riot ID is known. This is on by default.
- `GitHub button`: show a button that opens this project's GitHub page. This is on by default. Discord allows two buttons, so Join party is shown first, then Tracker, then GitHub. The same activity is sent to every Discord client running on this PC.
- `Team`: show which side the signed-in player is on during a live match, such as Blue or Red. This is on by default.
- `Lobby size`: show the party size before the match starts, such as 1/5. This is on by default.
- `Players in game`: show how many players are in the live match, such as 10 players. This is on by default and does not include the lobby size.
- `Show beside the official game activity`: Discord keeps music visible beside a game because they use different activity types, but it shows only one Playing activity. This option uses the second slot, so this presence stays visible while the official League or Valorant activity is also on. Discord labels it Watching instead of Playing. This is on by default.
- `Riot ID`, `Account level`, `Region`, `Rank`, `Currencies`, and `Collection counts`: optional account statistics, off by default.

The current situation is always shown first: client open, lobby, queue, champion or agent select, or in game. A Valorant lobby also shows its queue, and the lobby size option shows the party size, such as Swiftplay and 1/5. League shows the same party size when its lobby reports the members. During a live match the team option shows Blue or Red and the players in game option shows the players in that match. K/D is available for League while the local live game is running; Valorant does not expose live kills and deaths, so that value stays hidden there. When the party join option is enabled, the Valorant party link is created automatically and shown as a Join party button. A live match can temporarily block the League client and Valorant party requests. The last activity stays visible, while League score and K/D continue from the local live game. League and Valorant can appear at the same time. During Valorant agent select and in game, the selected agent's uploaded portrait is the large icon and the Valorant artwork is the small icon. Discord must be running, and the activities remain visible only while League Account Manager stays open. Passwords and party codes are never written to logs.

## Technical details

The page groups the choices into Presence, Match, Account, and Buttons. The preview on the right uses the selected options and shows the same two buttons Discord will display.

- View: `views/DiscordPresence.xaml`
- Code-behind: `views/DiscordPresence.xaml.cs`
- Publisher: `Misc/DiscordPresenceService.cs`
