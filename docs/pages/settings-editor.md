# Settings Editor

## Overview

The settings editor page is used to modify in-game configuration values.

## Functions

- Edit settings fields across multiple tabs.
- Reset, import, export, or apply configuration changes.

## Options

- Tabs: settings categories shown in the dynamic tab control.
- `Reset`: reset settings values in the current context.
- `Export`: export settings to a file.
- `Import`: import settings from a file.
- `Apply to Client`: apply settings to the client.
- `Apply to Account`: apply settings to the selected account.
- `Lock` / `Unlock`: toggle write protection.

## Tutorial

1. Open the settings editor page from the settings page.
2. Change the values you want to update.
3. Use the action buttons to apply or save.

## Technical details

- View: `views/SettingsEditor.xaml`
- Code-behind: `views/SettingsEditor.xaml.cs`
- Uses input controls and save handlers for persistence.
