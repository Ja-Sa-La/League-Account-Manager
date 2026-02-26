# Settings

## Overview

The settings page displays application settings and saves preferences.

## Functions

- Configure save file naming, update behavior, and account display preferences.
- Enable or disable account file encryption.

## Options

- `Save file name`: text box for the account CSV filename.
- `Check for updates automatically`: auto-update checkbox.
- `Display password in the accounts tab`: show/hide passwords in the accounts grid.
- `Update Ranks automatically on startup`: auto-refresh rank data.
- `Encrypt account file with a password`: enable account file encryption.
- `Save settings`: persist the selected options.

## Tutorial

1. Open the settings page from the main window.
2. Adjust the checkboxes and filename as needed.
3. Click `Save settings` to persist changes.

## Technical details

- View: `views/Settings.xaml`
- Code-behind: `views/Settings.xaml.cs`
- Uses WPF layout controls to display configuration.
