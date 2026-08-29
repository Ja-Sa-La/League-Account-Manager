# Main Window

## Overview

The main window is the shell of the app and provides navigation to all other pages and tools.

## Functions

- Entry point for the UI and primary navigation surface.
- Hosts the current view, notifications, update prompts, and status indicators.
- Provides the navigation menu and settings access.

## Options

- Navigation items: League accounts, Valorant accounts, Add accounts, Champion select, Auto Champ select, Buy champions, Report tool, LCU traffic, Misc, Chat, Loot, and Ingame settings.
- Footer: Settings, version label, credits, and Discord support link.
- Title bar: window controls and app title.

## Tutorial

1. Launch the app to open the main window.
2. Use the navigation menu to open the page you need. Cached pages preserve long-running workflows while you navigate.
3. Use the Settings item in the footer for app preferences.

## Technical details

- View: `MainWindow.xaml`
- Code-behind: `MainWindow.xaml.cs`
- The main window typically hosts other views inside its layout.
- Update checks run at startup and periodically when automatic updates are enabled.
