# Add Accounts

## Overview

The add accounts page is used to create new account entries, either individually or in bulk.

## Functions

- Capture account details in form fields.
- Submit new entries for storage in the app.
- Paste multiple entries for bulk import.

## Options

- Single account: `Username` and `Password` fields with an `Add account` button.
- Bulk import: multi-line input for `username:password` entries and an `Add all` button.

## Tutorial

1. Open the add accounts page from the main window.
2. Fill in the required fields.
3. Use `Add account` for a single entry or `Add all` for bulk import.

## Technical details

- View: `views/AddAccounts.xaml`
- Code-behind: `views/AddAccounts.xaml.cs`
- Implements form-style inputs with validation in the code-behind.
