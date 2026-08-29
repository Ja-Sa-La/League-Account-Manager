# Settings

## Overview

The settings page displays application settings and saves preferences.

## Functions

- Configure save file naming, update behavior, release channel, and account display preferences.
- Enable or disable account file encryption.

## Options

- `Save file name`: text box for the account CSV filename.
- `Check for updates automatically`: auto-update checkbox.
- `Release channel`: choose Stable (tested releases only) or Beta (early access; may contain bugs).
- `Display password in the accounts tab`: show/hide passwords in the accounts grid.
- `Update Ranks automatically on startup`: auto-refresh rank data.
- `Encrypt account file with a password`: enable account file encryption.
- `Save settings`: persist the selected options.

## Tutorial

1. Open the settings page from the main window.
2. Adjust the filename, update behavior, release channel, and account display options as needed.
3. Click `Save settings` to persist changes.

Automatic update checks run at startup and periodically while the application is open. Updates are downloaded only when a newer release is available for the selected channel.

## Technical details

- View: `views/Settings.xaml`
- Code-behind: `views/Settings.xaml.cs`
- Uses WPF layout controls to display configuration.
