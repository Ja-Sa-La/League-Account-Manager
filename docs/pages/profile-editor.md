# Profile Editor

## Overview

The profile editor page is used to view and update profile-related information and chat presence.

## Functions

- Update chat presence and status message.
- Change profile icon and background.
- Set a rank display using queue, rank, and division.

## Options

- Chat controls: `Disable Chat`, `Enable Chat`.
- Presence controls: `Show Offline`, `Show Away`, `Show Mobile`, `Show Online`.
- Status message: text box and `Set Chat Statusmessage`.
- Icon: `set Icon` with icon autosuggest and preview.
- Background: `set Background` with skin autosuggest and preview.
- Rank: queue, rank, and division selectors with `set Rank`.

## Tutorial

1. Open the profile editor page from the main window.
2. Update the fields you want to change.
3. Use the corresponding action buttons to apply changes.

## Technical details

- View: `views/ProfileEditor.xaml`
- Code-behind: `views/ProfileEditor.xaml.cs`
- Uses data bindings and event handlers for updates.
