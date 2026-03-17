# 01-upgrade-project-to-net10: Upgrade Project to .NET 10

Perform one atomic upgrade of the project target framework and package set, then resolve resulting compile-time compatibility issues. This task includes updating the project TFM, applying package upgrades/replacements required for .NET 10 compatibility, and making source updates needed to restore a successful build.

This task also includes validating the upgraded application by running the solution build and available tests after compilation is restored.

**Done when**: The project targets `.NET 10`, package compatibility issues are resolved, the solution builds successfully, and tests run without failures.

## Research Findings

- Project is SDK-style WPF with target framework currently set to `net8.0-windows7.0`.
- All package references are defined directly in `League_Account_Manager.csproj` (no CPM).
- Analyzer flagged compatibility risk for:
  - `FlaUI.UIA3` -> supported version for net10: `4.0.0`
  - `WPF-UI` -> supported version for net10: `2.0.3`
  - `Newtonsoft.Json` -> recommended `13.0.4`
  - `System.Drawing.Common` -> supported preview line `11.0.0-preview.2.26159.112` (defer unless required by build/runtime)
  - `ini-parser` already supported at `2.5.2`

## Execution Plan

1. Update project TFM from `net8.0-windows7.0` to `net10.0-windows`.
2. Apply package version updates identified as required/recommended by compatibility tooling.
3. Run restore/build to identify compiler or package conflicts.
4. Fix source-level issues if they are introduced by package/framework changes.
5. Run solution build and tests, then capture outcomes in `progress-detail.md`.
