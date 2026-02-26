# Autolobby

## Overview

The autolobby page groups tools related to automated lobby setup workflows and champion selections.

## Functions

- Provide controls for auto-accept queue, pick, ban, and chat message workflows.
- Configure picks and bans per role.

## Options

- `Enable AutoAcceptQueue`: auto-accept match found.
- `Enable AutoAcceptPick`: auto-select champion.
- `Enable AutoAcceptBan`: auto-ban champion.
- `Enable AutoAcceptMessage`: auto-send a chat message.
- Message box: message text sent when auto-message is enabled.
- Role picks: Blind/No role, Top, Jungle, Mid, Bot, Support champion selectors.
- Bans: Ban 1, Ban 2, Ban 3 champion selectors.

## Tutorial

1. Open the autolobby page from the main window.
2. Configure picks, bans, and auto-accept toggles.
3. Enable the relevant auto-actions.

## Technical details

- View: `views/Autolobby.xaml`
- Code-behind: `views/Autolobby.xaml.cs`
- Uses WPF controls to capture settings and start actions.
