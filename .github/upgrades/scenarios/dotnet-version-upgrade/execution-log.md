
## [2026-03-17 17.42] 01-upgrade-project-to-net10

Retargeted `League_Account_Manager.csproj` to `net10.0-windows7.0` and updated package references for compatibility (`FlaUI.UIA3` to 4.0.0 and `Newtonsoft.Json` to 13.0.4). During validation, downgrading `WPF-UI` caused XAML API breakage, so it was reverted to 4.2.0 to preserve current UI control usage. Final solution build is successful. No test projects were discovered, so no tests were executed.

