# Backup Manager

A cross-platform desktop backup manager that wraps the [restic](https://restic.net/) CLI.
It lives in your system tray, runs backups on a schedule, and shows native
notifications when they finish — with a full GUI for managing repositories,
snapshots, and jobs.

**Platforms:** Windows (x64) and Linux (x64). Built with Avalonia UI on .NET 10.

## Features

- **Tray-first operation** — persistent tray icon with a native menu
  (open dashboard, sync now, pause/resume schedule, quit). Closing the window
  returns to the tray; the tooltip shows the currently running job.
- **Scheduled backups** — in-app scheduler that runs each enabled job once per
  day at its configured time, with per-job excludes and tags.
- **Manual sync** — trigger a backup immediately from the tray or dashboard.
- **Repository management** — add restic repositories (any location restic
  supports), initialize new ones non-destructively, and run integrity checks.
- **Snapshot browsing & restore** — list snapshots, browse their contents, and
  restore a snapshot to a local folder.
- **Run history** — every run (success/failure, duration, bytes added) is
  logged in the Logs view.
- **Notifications** — native Windows toasts (`Microsoft.Toolkit.Uwp.Notifications`)
  or Linux `notify-send`, with distinct messages for manual vs. scheduled and
  success vs. failure runs.
- **Auto-start** — optionally start with the OS (registry Run key on Windows,
  `~/.config/autostart/*.desktop` on Linux) and launch hidden to the tray.
- **Crash-safe startup** — unhandled exceptions are written to a crash log
  instead of vanishing silently.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build.
- The `restic` binary available on `PATH`, or a custom path set in
  **Settings** (the app shows the detected restic version there).

## Building & running

```sh
# Run from source
dotnet run

# Publish (Linux, self-contained)
dotnet publish BackupManager.csproj -c Release -f net10.0 -r linux-x64 --self-contained -o ./publish

# Publish (Windows, self-contained — needed for toast notifications)
dotnet publish BackupManager.csproj -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained -o ./publish
```

On Linux the app prefers the X11/Skia backend when `DISPLAY` is set; set
`BM_BACKEND=x11` to force it. Where no tray is available (e.g. some Wayland
compositors), the main window becomes the primary surface.

### Windows installer

`packaging/installer.nsi` builds an NSIS installer from the publish output:

```sh
makensis -DPUBLISH_DIR="$PWD/publish" packaging/installer.nsi
```

## Releases

Pushing a tag (`v*`) triggers the GitHub Actions workflow in
[.github/workflows/release.yml](.github/workflows/release.yml), which builds
self-contained binaries for `win-x64` and `linux-x64`, produces a Windows
setup EXE and a Linux tarball, and attaches them to a GitHub release with
auto-generated notes.

## Configuration & data locations

All state lives under the platform-local app data directory:

| File | Purpose |
|---|---|
| `BackupManager/config.json` | Repositories, jobs, and settings |
| `BackupManager/crash.log` | Unhandled exceptions |
| `BackupManager/trace.log` | Avalonia/UI trace output |
| `BackupManager/notify.log` | Notification diagnostics (Windows) |
| `BackupManager/restic-cache/` | restic cache directory (if not overridden) |

- **Windows:** `%LOCALAPPDATA%\BackupManager\`
- **Linux:** `~/.local/share/BackupManager/`

## Architecture

MVVM with `CommunityToolkit.Mvvm`, wired together with
`Microsoft.Extensions.DependencyInjection`:

```
App.axaml.cs          DI setup, tray icon, window lifecycle, scheduler start
Program.cs            Entry point, crash logging, backend selection
Views/                MainWindow + Dashboard/Repositories/Snapshots/Schedule/Logs/Settings
ViewModels/           One view-model per view, plus shared AppState
Services/
  ResticService       restic subprocess wrapper (--json parsing, env config)
  SchedulerService    60s tick scheduler, one run per job per day, run queue
  JsonConfigStore     JSON persistence for AppConfig
  NotificationService Windows toasts / Linux notify-send
Models/               AppConfig, BackupJob, RepositoryConfig, SnapshotInfo, BackupProgress
```

`ResticService` centralizes restic invocation and passes credentials via the
`RESTIC_REPOSITORY` / `RESTIC_PASSWORD` environment variables (never command
line arguments). Backups stream `--json` progress events, which drive live
progress and error reporting.

## Security notes

- Repository passwords are currently stored in plaintext in `config.json` —
  protect that file accordingly. (OS credential stores are a planned
  improvement; see [PLAN.md](PLAN.md).)
- Restic itself provides at-rest encryption; the config only holds the
  passphrase to unlock it.

## Known limitations

- The scheduler uses an in-app timer: jobs only run while the app is running.
- One backup per job at a time; concurrent runs of the same job are skipped.
- macOS is a stretch goal and not yet supported.
