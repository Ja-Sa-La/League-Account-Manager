# Settings

## Overview

The settings page displays application settings and saves preferences.

## Functions

- Configure save file naming, update behavior, release channel, and account display preferences.
- Enable or disable account file encryption.
- Sign in to, create, and sign out of an optional cloud account.
- Upload or download encrypted account and application settings data.

## Options

- `Save file name`: text box for the account CSV filename.
- `Check for updates automatically`: auto-update checkbox.
- `Release channel`: choose Stable (tested releases only) or Beta (early access; may contain bugs).
- `Display password in the accounts tab`: show/hide passwords in the accounts grid.
- `Update Ranks automatically on startup`: auto-refresh rank data.
- `Encrypt account file with a password`: enable account file encryption.
- `Save settings`: persist the selected options.

## Cloud synchronization

Cloud synchronization is managed from the Settings page and is separate from local account-file encryption.

1. Select `Sign in / Create account` and complete the cloud authentication flow.
2. Use the account sync controls to upload or download the local account file.
3. Use the settings sync controls to upload or download application settings.
4. Select `Sign out` to remove the saved cloud session from this Windows user.

Account and settings payloads are encrypted locally with AES-256-GCM before upload. Separate keys are derived for the two data types. The app can automatically upload account changes while a valid cloud session is available; it does not prompt for cloud credentials when no valid session exists.

The saved session is protected with Windows DPAPI and is restored for the current Windows user when the app starts. If the session cannot be restored or is no longer valid, sign in again from Settings.

## Tutorial

1. Open the settings page from the main window.
2. Adjust the filename, update behavior, release channel, and account display options as needed.
3. Click `Save settings` to persist changes.

Automatic update checks run at startup and periodically while the application is open. Updates are downloaded only when a newer release is available for the selected channel.

## Technical details

- View: `views/Settings.xaml`
- Code-behind: `views/Settings.xaml.cs`
- Uses WPF layout controls to display configuration.
