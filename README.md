# Baseline

Baseline is a WPF desktop application for capturing a known-good Windows configuration and reproducing it on another machine in a selective, rollback-capable way.

## What It Is

- Profile-based Windows environment capture
- Compare current machine state against a saved profile
- Apply selected differences with per-item results
- Record rollback data before apply and restore app-managed changes later

## What It Is Not

- Full system imaging
- Full registry export or import
- Driver migration
- Arbitrary BCD editor
- System restore replacement
- Credential or profile cloning tool

## Supported v1 Categories

- Services
- Boot Behavior Settings
- Registry Tweaks
- Policies
- Network
- Startup Environment
- Scheduled Tasks
- Power Configuration

## Workflow

1. Capture a profile from the source machine.
2. Save or load a profile file.
3. Compare the profile against the current machine.
4. Select mismatches or individual items to apply.
5. Use rollback history to restore app-managed changes from a prior apply session.

## Project Structure

- `BaseLine/Core`: strongly typed domain and workflow models
- `BaseLine/Infrastructure`: dialogs, command execution, registry access, storage, composition root
- `BaseLine/Services`: category handlers, workflow orchestration, machine and network discovery
- `BaseLine/ViewModels`: shell and workflow page view models
- `BaseLine/Views`: main window and page views
- `BaseLine/Resources`: theme dictionaries and data templates

## Profile and Rollback Storage

- Profiles are JSON files intended for operator review and reuse.
- Rollback records are stored under the local app data path for the current user.
- Rollback is scoped to settings changed by Baseline during apply sessions.

## Current v1 Limitations

- Startup folder entries are captured for review but are not recreated during apply.
- Scheduled tasks support enable and disable flow; full task recreation is intentionally out of scope for v1.
- Network adapter apply is adapter-aware, but when the captured adapter is missing it falls back to the first active adapter with a warning in compare.
- Some curated registry and policy entries only appear when the source machine already has those values defined.
- Administrative privileges are still required by Windows for many apply and rollback operations.

## Build

```powershell
dotnet build BaseLine.slnx
```
