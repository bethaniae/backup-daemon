# Avalonia + Restic Backup App — Project Plan

## 1. Overview

A cross-platform desktop backup manager built with Avalonia UI, wrapping the
`restic` CLI as a subprocess. The app runs primarily from the system tray,
showing minimal status (last backup time, in-progress indicator, errors),
with a full GUI window for detailed management (repos, snapshots, schedules,
logs, restore).

**Target platforms:** Windows, Linux (primary), macOS (stretch goal)

---

## 2. Architecture

```
┌─────────────────────────────┐
│           App (Avalonia)     │
│                               │
│  ┌───────────┐  ┌──────────┐ │
│  │ TrayIcon  │  │  MainWin  │ │
│  │  (status) │  │ (details) │ │
│  └─────┬─────┘  └─────┬────┘ │
│        │              │      │
│        └──────┬───────┘      │
│               │               │
│      ┌────────▼────────┐     │
│      │  ResticService   │     │
│      │ (process wrapper)│     │
│      └────────┬────────┘     │
│               │               │
│      ┌────────▼────────┐     │
│      │  Scheduler /     │     │
│      │  BackgroundQueue │     │
│      └────────┬────────┘     │
│               │               │
│      ┌────────▼────────┐     │
│      │  restic binary   │     │
│      │  (subprocess)    │     │
│      └─────────────────┘     │
└─────────────────────────────┘
```

**Pattern:** MVVM (ReactiveUI or CommunityToolkit.Mvvm), single background
process, main window hidden by default after first run.

---

## 3. Core Components

### 3.1 ResticService (process wrapper layer)

- Wraps `restic` binary invocation via `Process` / `ProcessStartInfo`.
- All read-oriented commands use `--json` for structured parsing:
  - `snapshots --json`
  - `stats --json`
  - `check --json`
  - `backup --json` (streams progress events, one JSON object per line)
- Async, cancellable execution (`CancellationToken` wired to GUI cancel
  buttons and app shutdown).
- Centralizes environment/config:
  - `RESTIC_REPOSITORY`
  - `RESTIC_PASSWORD_FILE` (preferred over `RESTIC_PASSWORD` env var)
  - `RESTIC_CACHE_DIR`
- Typed result models (`SnapshotInfo`, `BackupProgress`, `RepoStats`, etc.)
  deserialized with `System.Text.Json`.
- Emits events/streams (e.g. `IObservable<BackupProgress>`) so both tray and
  GUI can subscribe independently.

### 3.2 Tray Icon (minimal surface)

- `TrayIcon` + `NativeMenu` defined in `App.axaml`.
- States reflected via icon swap:
  - Idle / last backup OK
  - Backup in progress (animated or distinct icon)
  - Error / repo unreachable
- Tooltip text: last successful backup timestamp, or current progress %.
- Menu items:
  - "Open Dashboard" → show/restore main window
  - "Backup Now" → trigger immediate backup job
  - "Pause Schedule" (toggle)
  - "Quit"
- Left-click (where platform supports it) → show/restore main window.

### 3.3 Main Window (full GUI)

Suggested views/tabs:

- **Dashboard** — status summary, next scheduled run, recent activity log,
  quick "Backup Now" / "Restore" actions.
- **Repositories** — add/edit/remove restic repos (local, SFTP, S3, B2,
  rclone-backed, etc.), test connection, init new repo.
- **Snapshots** — list via `snapshots --json`, filter by tag/host/path,
  browse contents (`ls --json`), restore selected snapshot or files.
- **Schedule** — define backup jobs (paths, excludes, tags, cron-like
  schedule), enable/disable per job.
- **Logs / History** — persisted run history (success/failure, duration,
  bytes added), streamed live output during active jobs.
- **Settings** — restic binary path override, cache dir, notification
  preferences, startup-with-OS toggle, close-to-tray behavior.

### 3.4 Scheduler / Background Runner

- In-process scheduler (e.g. simple timer-based or a lightweight cron
  library) rather than relying on OS task schedulers initially — keeps
  cross-platform behavior consistent.
- Optional stretch: generate OS-native scheduled tasks (systemd timer /
  Task Scheduler) for reliability when app isn't running.
- Job queue ensures only one restic operation runs at a time per repo
  (restic locks repos, but queuing avoids noisy failures).

### 3.5 Persistence

- App config (repos, jobs, schedules, preferences) stored as local JSON or
  SQLite (`Microsoft.Data.Sqlite` or `LiteDB`) under platform-appropriate
  app-data directory.
- Never store repo passwords in plaintext app config — use OS credential
  store where available:
  - Windows: DPAPI / Credential Manager
  - Linux: Secret Service API (libsecret) via a wrapper, fallback to
    `RESTIC_PASSWORD_FILE` with restricted file permissions
  - macOS: Keychain

---

## 4. Milestones

### Phase 1 — Foundation
- [ ] Avalonia project scaffold (MVVM, DI container e.g. `Microsoft.Extensions.DependencyInjection`)
- [ ] `ResticService` with basic commands: `version`, `init`, `snapshots --json`
- [ ] Verify restic binary detection/bundling strategy (bundle vs. require system install)

### Phase 2 — Tray + Window shell
- [ ] TrayIcon with static icon + menu (Open/Quit)
- [ ] Main window show/hide, close-to-tray behavior
- [ ] Basic Dashboard view showing last snapshot info

### Phase 3 — Backup execution
- [ ] "Backup Now" wired to `restic backup --json`, live progress parsing
- [ ] Tray icon state changes during backup (in-progress/error/idle)
- [ ] Run history persisted and shown in Logs view

### Phase 4 — Repository & snapshot management
- [ ] Add/edit repositories, secure credential storage
- [ ] Snapshot browsing and restore flow (`restic restore` / `dump`)
- [ ] Repo check/prune actions with confirmation dialogs

### Phase 5 — Scheduling
- [ ] In-app scheduler with per-job cron-like config
- [ ] Pause/resume schedule from tray menu
- [ ] Notifications on failure (OS-native notification if available)

### Phase 6 — Polish / cross-platform hardening
- [ ] Linux tray behavior testing across DEs (GNOME/KDE/XFCE)
- [ ] Startup-on-login integration per OS
- [ ] Packaging: MSIX/installer (Windows), AppImage/deb (Linux), optionally .app (macOS)
- [ ] Auto-update mechanism (optional)

---

## 5. Key Risks / Open Questions

| Risk | Notes |
|---|---|
| Linux tray inconsistency | GNOME needs AppIndicator extension; test early on target distros |
| Restic binary distribution | Bundle a pinned version vs. require user-installed binary + PATH detection |
| Long-running process during OS sleep/shutdown | Handle graceful cancellation, avoid corrupting repo locks |
| Credential storage cross-platform parity | libsecret wrapper on Linux needs testing; consider `RESTIC_PASSWORD_FILE` fallback |
| Concurrent repo access | Enforce single-job-per-repo queuing to avoid lock conflicts |

---

## 6. Suggested Tech Stack

- **UI:** Avalonia UI 11.x, Fluent theme
- **MVVM:** CommunityToolkit.Mvvm (simpler) or ReactiveUI (better for
  reactive progress streams)
- **JSON:** `System.Text.Json`
- **Persistence:** SQLite via `Microsoft.Data.Sqlite` or LiteDB
- **DI:** `Microsoft.Extensions.DependencyInjection`
- **Process management:** `System.Diagnostics.Process`, async line-reading
  for streamed JSON

---

## 7. Next Steps

1. Confirm target platforms and whether restic binary will be bundled or
   required as a system dependency.
2. Scaffold the Avalonia project with tray icon shell (Phase 1–2) as a
   proof of concept before investing in scheduling/persistence.
3. Decide on credential storage approach per platform early, since it
   affects the repository-add UX.
