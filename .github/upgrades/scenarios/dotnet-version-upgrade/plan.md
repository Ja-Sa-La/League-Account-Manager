# .NET Version Upgrade Plan

## Overview

**Target**: Upgrade `League_Account_Manager` from `net8.0-windows7.0` to `.NET 10` (`net10.0-windows`) and align package dependencies for compatibility.
**Scope**: Single WPF project, SDK-style, with significant API compatibility fixes expected.

### Selected Strategy
**All-At-Once** — All projects upgraded simultaneously in a single operation.
**Rationale**: One project in scope with no dependency tiers, so phased dependency execution is unnecessary.

## Tasks

### 01-upgrade-project-to-net10

Perform one atomic upgrade of the project target framework and package set, then resolve resulting compile-time compatibility issues. This task includes updating the project TFM, applying package upgrades/replacements required for .NET 10 compatibility, and making source updates needed to restore a successful build.

This task also includes validating the upgraded application by running the solution build and available tests after compilation is restored.

**Done when**: The project targets `.NET 10`, package compatibility issues are resolved, the solution builds successfully, and tests run without failures.
