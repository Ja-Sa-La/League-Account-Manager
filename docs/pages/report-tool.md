# Report Tool

## Overview

The report tool page focuses on loading reportable players and submitting reports.

## Functions

- Load reportable players from the current match.
- Submit reports for selected players.

## Options

- `Get reportable players`: load the list of players to report.
- `Select all`: toggle selection of all rows.
- `Report selected`: submit reports for checked entries.
- Data grid columns: Select, Username, Reported, SummonerID, GameID, puuID.

## Tutorial

1. Open the report tool page from the main window.
2. Use `Get reportable players` to populate the list.
3. Select entries and click `Report selected`.

## Technical details

- View: `views/ReportTool.xaml`
- Code-behind: `views/ReportTool.xaml.cs`
- Uses form inputs with event handlers for submission.
