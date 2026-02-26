# Champion Buyer

## Overview

The champion buyer page focuses on purchasing champions through the app workflow.

## Functions

- Provide a list or selection of champions.
- Trigger purchase actions from the UI.
- Show a log of purchase results.

## Options

- `Buy selected champions`: purchase all selected champions.
- Available champions list: multi-select list of champions with price details.
- Buy log: scrollable history of purchase actions.

## Tutorial

1. Open the champion buyer page from the main window.
2. Select the champions to purchase.
3. Click `Buy selected champions` and review the buy log.

## Technical details

- View: `views/ChampionBuyer.xaml`
- Code-behind: `views/ChampionBuyer.xaml.cs`
- UI actions map to code-behind handlers for purchase operations.
