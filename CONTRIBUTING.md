# Contributing

Thank you for helping improve Taskbar Timer Widget.

## Before you start

- Search existing issues before opening a new one.
- Use the bug report form for reproducible defects and the feature request form for product ideas.
- Keep changes focused. Large UI or architecture changes should be discussed in an issue first.

## Development setup

You need Windows 10 or 11, PowerShell 5.1 or later, and the .NET Framework 4.8 Developer Pack.

Build the application and run the deterministic logic tests:

```powershell
.\build.ps1
```

Run the live Explorer/taskbar smoke test when changing window ownership, positioning, DPI, or taskbar-recovery behavior:

```powershell
.\build.ps1 -RunSmokeTest
```

Create the same release package produced by CI:

```powershell
.\build.ps1 -Package
```

## Pull requests

- Explain the user-visible behavior and the reason for the change.
- Add or update tests for deterministic logic.
- Confirm that `build.ps1` succeeds without warnings.
- Update `README.md` or `CHANGELOG.md` when behavior changes.
- Do not commit files generated under `build/` or `artifacts/`.

By contributing, you agree that your contribution is licensed under the MIT License.
