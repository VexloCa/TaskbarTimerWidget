# Changelog

All notable changes to Taskbar Timer Widget are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Recover the widget's correct physical size automatically after transient Explorer or display DPI changes, without requiring an application restart.

## [1.0.0] - 2026-07-18

### Added

- Compact taskbar-aligned countdown display with a fixed `hh:mm:ss` format.
- Seven timer presets and a custom duration dialog.
- Start, pause, resume, reset, warning pulse, and completion alert states.
- Primary and secondary monitor selection with per-monitor DPI positioning.
- Automatic recovery after Explorer, display, device, or taskbar changes.
- Light and dark taskbar theme matching.
- Optional launch at Windows sign-in and single-instance activation.
- User-level install and uninstall scripts.
- Automated Windows build, test, package, checksum, and tagged-release workflows.

[Unreleased]: https://github.com/VexloCa/TaskbarTimerWidget/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/VexloCa/TaskbarTimerWidget/releases/tag/v1.0.0
