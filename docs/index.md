This site documents the app, its features, and the WPF pages in the UI.

## Features

- Store accounts locally (CSV) (optionally you can encrypt the data with AES-GCM) and log in with a click
- Share accounts without ever having to give your password to anyone
- Save rank, level, champions, skins, loot, and notes per account
- Search accounts by region, loot value, champions, and skins
- Champion buyer (quick purchase flow)
- Loot manager and disenchanter
- Queue auto-accept (configurable)
- Player stats in champ select (queues allowed by Riot only)
- Profile editor (icon, banner, status, background)
- Riot ID changer
- Friend management (bulk remove) and log cleanup
- Report tool with improved post-game UI
- Misc tools: log remover, loot value checker, queue helpers, etc.
- Stealth login (launch/login without showing yourself online)

## Screenshots

### Dashboard
<img src="https://github.com/user-attachments/assets/887ab552-7969-4eed-92d8-0537e7634a7e" width="100%" />

### Add Accounts
<img src="https://github.com/user-attachments/assets/94bb1a3f-4a85-4b20-8865-670b8f5841a5" width="100%" />

### Champion Select
<img src="https://github.com/user-attachments/assets/c08c7d11-c83d-4e8f-99d7-925a77bf7382" width="100%" />

### Auto Champion Select
<img src="https://github.com/user-attachments/assets/811edfbf-7416-4f84-8832-c02bc475642f" width="100%" />

### Champion Buyer
<img src="https://github.com/user-attachments/assets/b0ebfad5-9201-4c9d-a269-18406e18b959" width="100%" />

### Report Manager
<img src="https://github.com/user-attachments/assets/50584ce8-41bb-4eb7-8dd1-985d4d7d6955" width="100%" />

### Misc Tools
<img src="https://github.com/user-attachments/assets/59061b09-a74a-4e21-ac21-cf97dce94d98" width="100%" />

### Profile Editor
<img src="https://github.com/user-attachments/assets/fdde3f73-743c-4e11-8a6f-1ffb139c1627" width="100%" />

### Disenchanter
<img src="https://github.com/user-attachments/assets/0c016f75-086b-4542-9c2f-efd0d2f0cdaa" width="100%" />

### Settings Manager
<img src="https://github.com/user-attachments/assets/2903b900-9881-43f3-a21e-f37db8920b4e" width="100%" />

### Change Riot ID
<img src="https://github.com/user-attachments/assets/357fdd3b-59d7-46f1-88fa-c4369778df0b" width="600" />

## Pages

- [Main Window](pages/main-window.md)
- [Accounts](pages/accounts.md)
- [Add Accounts](pages/add-accounts.md)
- [Autolobby](pages/autolobby.md)
- [Champion Buyer](pages/champion-buyer.md)
- [Champion Select](pages/champion-select.md)
- [Disenchanter](pages/disenchanter.md)
- [Misc Tools](pages/misc-tools.md)
- [Profile Editor](pages/profile-editor.md)
- [Report Tool](pages/report-tool.md)
- [Settings](pages/settings.md)
- [Settings Editor](pages/settings-editor.md)
- [Change Name](pages/change-name.md)
- [Display Data With Search](pages/display-data-with-search.md)
- [Missing Info](pages/missing-info.md)
- [Note Display](pages/note-display.md)
- [Password Prompt](pages/password-prompt.md)
- [Progress Window](pages/progress-window.md)
- [Remove Friends Confirmation](pages/remove-friends-confirmation.md)

## Requirements

- Windows
- .NET 8 Desktop Runtime (for the packaged app) or SDK (for building)

## Install & Run (binary)

1. [Download](https://github.com/Ja-Sa-La/League-Account-Manager/releases) the latest release build
2. Ensure .NET 8 Desktop Runtime is installed
3. Run `League Account Manager.exe`
4. If League permissions block some operations, start the app as Administrator

## Build from Source

```powershell
git clone https://github.com/Ja-Sa-La/League-Account-Manager.git
cd League-Account-Manager
dotnet restore
dotnet build League_Account_Manager/League_Account_Manager.csproj -c Release
dotnet run --project League_Account_Manager/League_Account_Manager.csproj
```

## Privacy & Safety

- All account data stays on your machine (local CSV)
- Uses only LCU endpoints; no automation via autoclickers

## Troubleshooting

- Run as Administrator if file access or League permissions fail
- Ensure the League Client is running when using LCU-dependent features

## Contributing

PRs and issues are welcome. Please keep changes within Riot’s ToS and LCU guidelines.

## Technical details

- UI framework: WPF on .NET 8.
- Each page uses a XAML view and a code-behind file (for example, `views/Accounts.xaml` and `views/Accounts.xaml.cs`).
- Windows and dialogs live under `Windows` and are opened by the main workflow as needed.

## Tutorials

For each page, open the relevant view from the main window, follow the steps listed on that page, and return here for navigation.
