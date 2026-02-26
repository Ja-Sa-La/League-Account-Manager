# Champion Select

## Overview

The champion select page assists with scouting during champion select.

## Functions

- Enter player names and open external stats pages.
- Pull ranks and stats for the team.

## Options

- `Pull info`: fetch ranks and recent stats for the listed players.
- `Open multi.op.gg`: open a multi-search page for the team.
- `DODGE`: trigger the dodge workflow.
- `Open PoroProfessor`: open the team in PoroProfessor.
- Player entries: `Player 1` through `Player 5` name fields.
- For each player: `Open op.gg` and `Open League of Graphs` shortcuts.
- Ranks & stats panel: peak rank, current rank, winrate/KDA (last 40 games).

## Tutorial

1. Open the champion select page from the main window.
2. Fill in player names and use the open buttons for stats sites.
3. Use `Pull info` to load ranks and stats.

## Technical details

- View: `views/ChampionSelect.xaml`
- Code-behind: `views/ChampionSelect.xaml.cs`
- Uses selection controls and event handlers for the workflow.
