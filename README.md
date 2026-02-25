# League Account Manager

A WPF utility for managing League of Legends accounts from one place. It uses the League Client API (LCU) only and does not perform exploits.

## Features

- Store accounts locally (CSV) (optionally you can encrypt the data with AES-GCM) and log in with a click
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

Allows you to store your league of legends smurf accounts in a one place n login to them with a click of a button.
Was made to test some stuff in lcu and as a coding excersise. Does not do client exploits or anything not allowed by riot at the moment


### Report Manager
<img src="https://github.com/user-attachments/assets/50584ce8-41bb-4eb7-8dd1-985d4d7d6955" width="100%" />

- Accounts stored locally in a cvs file
- Save rank/level/champions/skins on your account
- search accounts by region/loot/champions/skins
- Allows you to quickly buy champions with a simple ui
- Display player stats in champion select, DOES NOT WORK ON SOLOQ only in queue types allowed by riot
- Remove all log files create by league of legends
- Manage your friendlist
- Better after game reporting ui
- All done in lcu no autoclicker scripts!
- Change your riot id easily
- Customise your chat profile or disable it
- Manage your loot
- automatically accept queue pops if you are away



![image](https://github.com/Ja-Sa-La/League-Account-Manager/assets/133235384/5f87db91-54ba-48f5-a03f-d91751b79551)
![image](https://github.com/Ja-Sa-La/League-Account-Manager/assets/133235384/ca4deff9-d291-4bc7-8b18-49eb28cb955e)
![image](https://github.com/Ja-Sa-La/League-Account-Manager/assets/133235384/cd199dc7-2f59-4cf6-aa08-0a5d2624dd12)
![image](https://github.com/Ja-Sa-La/League-Account-Manager/assets/133235384/e51377d1-7c8c-48c8-b51e-5b1e7bf3bc54)
![image](https://github.com/Ja-Sa-La/League-Account-Manager/assets/133235384/1c28ac75-9474-4365-9f1a-bd3e8e697c0d)
![image](https://github.com/Ja-Sa-La/League-Account-Manager/assets/133235384/73d84fd7-9627-49f4-bac5-bc883248d41f)
![image](https://github.com/Ja-Sa-La/League-Account-Manager/assets/133235384/a8b0d2b0-3246-416a-bbb5-8ba266a40d14)
![image](https://github.com/Ja-Sa-La/League-Account-Manager/assets/133235384/3db5e79f-621d-41bb-8e16-75462f59f482)

### Change Riot ID
<img src="https://github.com/user-attachments/assets/357fdd3b-59d7-46f1-88fa-c4369778df0b" width="600" />


## UI Pages / Tools

- Dashboard / Home
- Accounts list and search
- Champion Buyer
- Disenchanter / Loot Manager
- Misc Tools (log cleanup, queue helpers, loot value, etc.)
- Profile Editor (icon/status/background)
- Change Riot ID
- Friend Manager (Display inactive ones and bulk remove)
- Report Tool
- Settings

## Requirements

- Windows
- .NET 8 Desktop Runtime (for the packaged app) or SDK (for building)

## Install & Run (binary)

## Installation

## Build from Source

```powershell
git clone https://github.com/Ja-Sa-La/League-Account-Manager.git
cd League-Account-Manager
dotnet restore
dotnet build League_Account_Manager/League_Account_Manager.csproj -c Release
dotnet run --project League_Account_Manager/League_Account_Manager.csproj
```

