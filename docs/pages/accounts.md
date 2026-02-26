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

### Login token generation and usage

1. Select the account you want to use.
2. Click `Generate token` to create a login token for that account.
3. Click `Login With token` to sign in using the generated token.

#### Notes

- The generated token is copied to your clipboard.
- The app formats the token as a clickable link (for example, `https://redirect.leagueaccountmanager.xyz/login?token=...`).
- Share the link (for example, in Discord) so another user can click it and sign in with League Account Manager.
- Tokens are valid for about one minute.

## Technical details

- View: `views/Accounts.xaml`
- Code-behind: `views/Accounts.xaml.cs`
- Uses a WPF `DataGrid` with columns for username, password, Riot ID, level, server, currency, ranks, champions, skins, loot, and notes.
