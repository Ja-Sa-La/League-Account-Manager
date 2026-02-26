# Accounts

## Overview

The accounts page focuses on viewing and working with account entries, including client actions and data pulls.

## Functions

- Display account entries in a list with account metadata.
- Trigger client actions like login, stealth login, and client launch.
- Pull account data and manage stored entries.

## Options

- `Kill client`: close the League client.
- `Open League`: launch the League client.
- `PullData`: fetch account-related data and update the grid.
- `Delete`: remove the selected account entry.
- `Login`: log in with the selected account.
- `Stealth Login`: log in without showing online status.
- `Second client`: launch a secondary client session.
- `Remove duplicates`: remove duplicate entries from the account list.
- `Name Change`: open the Riot ID change flow.
- `Generate token`: generate a login token.
- `Login With token`: log in using a generated token.
- Filter box: search by champion, skin, loot, or server.

## Tutorial

1. Open the accounts page from the main window.
2. Review the list of accounts and use the filter to narrow results.
3. Select an account and use the action buttons to manage it.

## Technical details

- View: `views/Accounts.xaml`
- Code-behind: `views/Accounts.xaml.cs`
- Uses a WPF `DataGrid` with columns for username, password, Riot ID, level, server, currency, ranks, champions, skins, loot, and notes.
