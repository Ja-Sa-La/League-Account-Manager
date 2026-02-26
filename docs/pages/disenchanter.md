# Disenchanter

## Overview

The disenchanter page manages disenchanting workflows for champion shards and skins.

## Functions

- Display items eligible for disenchanting.
- Execute disenchant actions from the UI.
- Show expected blue and orange essence gains.

## Options

- `Disenchant selected`: disenchant the selected loot items.
- `Select all champs`: select all champion shard entries.
- `Select all skins`: select all skin shard entries.
- Blue essence sources: multi-select list of champion shards.
- Orange essence sources: multi-select list of skin shards.
- Gain labels: estimated blue/orange essence totals.

## Tutorial

1. Open the disenchanter page from the main window.
2. Select the items to disenchant (or use select-all).
3. Run `Disenchant selected` and review the totals.

## Technical details

- View: `views/DisEnchanter.xaml`
- Code-behind: `views/DisEnchanter.xaml.cs`
- Uses list selection and action buttons for operations.
