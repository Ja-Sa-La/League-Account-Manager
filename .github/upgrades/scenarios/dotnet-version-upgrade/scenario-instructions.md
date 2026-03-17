# .NET Version Upgrade

## Strategy
**Selected**: All-At-Once
**Rationale**: Single-project solution (`League_Account_Manager.csproj`) targeting modern .NET (`net8.0-windows7.0`) with no project dependency tiers; best handled as one atomic upgrade flow.

### Execution Constraints
- Apply framework and package updates in a single upgrade pass.
- Resolve compilation/API compatibility issues after the upgrade pass.
- Validate full solution build before any test validation.
- Execute tests only after build is clean.

## Preferences
- **Flow Mode**: Automatic
- **Commit Strategy**: Single Commit at End
- **Pace**: Standard

## User Preferences
- Upgrade target framework: .NET 10.0 (LTS)

## Decisions
- Use default source control setup: `master` -> `upgrade-to-NET10`; commit pending changes before initialization.

## Custom Instructions
<!-- Task-specific overrides: "For {taskId}: {instruction}" -->
