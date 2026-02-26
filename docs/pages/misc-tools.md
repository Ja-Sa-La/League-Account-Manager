# Misc Tools

## Overview

The misc tools page gathers smaller utilities in one location.

## Functions

- Expose quick tools and helper actions.
- Provide access to utilities not shown elsewhere.
- Show tool output in the log area.

## Options

- `Nuke logs`: remove League and Riot log files.
- `Nuke friends`: bulk remove friends.
- `Get Friends`: load the current friends list.
- `Uninstall league`: start the League uninstall flow.
- `Disable Riot client autolaunch`: disable startup auto-launch.
- `Get riot hwid`: display the Riot hardware ID.
- `Restart LeagueClient UX`: restart the client UX process.
- Output log: view status messages in the scrollable text area.

## Tutorial

1. Open the misc tools page from the main window.
2. Choose the tool you want to run.
3. Review results in the output log.

## Technical details

- View: `views/MiscTools.xaml`
- Code-behind: `views/MiscTools.xaml.cs`
- Contains multiple action handlers wired to UI controls.
