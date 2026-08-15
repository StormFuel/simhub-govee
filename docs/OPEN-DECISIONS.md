# Product Decisions

## Version 0.2 decisions

- The device table explains that an IP is optional for Cloud mode but strongly recommended with a DHCP reservation for Hybrid/local control.
- Reusable presets contain color, optional brightness, automatic power-on, and optional target lights.
- A Default Game Profile handles games without a custom profile and initially leaves lights unchanged.
- Custom profiles use SimHub's detected game code and may target all selected or specific lights.
- Power On without a preset preserves the current color and brightness.
- The original pre-game state is retained across direct game switches and restored after the final game stops.
- Hybrid/Cloud devices restore captured cloud state. Local-only devices restore the last plugin-commanded state, or the configured global default if no known state exists.
- The UI explains why Local-only restoration is best effort and why active scenes, music modes, segmented effects, and DreamView cannot be restored exactly.
- Managed SimHub actions preload Power On, Power Off, and Toggle. Their immutable identity/behavior is locked, but their target selection is configurable and survives reconciliation. Each reusable preset automatically owns a managed Set Color action that forces power on and routes editing to the preset.
- Managed color actions inherit preset targets; custom actions may target all selected lights or specific lights.
- Registered action keys are immutable. Preset renames update their managed action labels without changing keys, and preset deletion warns before removing the paired action.
- Toggle normally uses persisted plugin-tracked power. An off-by-default setting can refresh cloud state before every action when an API key is saved; refresh failure falls back to tracking.
- Manual actions persist until another manual action or a game/lifecycle transition changes the lights.

## Existing transport decisions

- Primary verified hardware is H6046, with capability-based flexibility for similar devices.
- Hybrid control uses cloud discovery/state and direct UDP 4003 commands to a reserved IP, with optional cloud fallback.
- The user's Developer API key is encrypted with Windows DPAPI for the current user.
- DreamView logical devices are hidden by default.
- The Govee Desktop DLL/GUID path is not a runtime dependency after its repeatable authentication failure.

## Version 0.3.0 segmented-lighting decisions

- Uniform whole-device color remains the simple default; per-device and per-segment appearances are opt-in advanced settings.
- Segmented color and brightness require the Developer cloud API. The UI explains that cloud accepts the commands but does not reliably return or visually verify current segment state.
- Capability metadata gates the feature but does not establish the physical topology. H6046 is verified as left bar 0–4 and right bar 5–9 even though cloud metadata advertises 0–14.
- Device topology is stored per physical device and may start from a model suggestion. Users can identify/calibrate sections when their hardware differs.
- Existing preset IDs and SimHub action keys survive migration to per-device appearances.
- The plugin tracks its own last segmented state for best-effort restoration; externally changed segment patterns cannot be reconstructed reliably.
- The segmented preset editor previews changes on the physical device to avoid mounting-orientation assumptions. The restoration warning is on by default, can be permanently suppressed from the dialog, and can be re-enabled from preset settings. All editor exits attempt to restore the captured primitive state plus the plugin's last known segmented pattern.
- The segmented editor has a complete-appearance test with a persisted 10-second default (configurable from 1–120 seconds), Stop support, and unconditional restoration. Available and configured device rows support double-click configuration; the editable main Devices grid intentionally does not overload double-click.
- Configured startup may apply a reusable preset (including targets and segmented appearances); no selected startup preset preserves the legacy power-only behavior, and Leave Unchanged overrides both.
- A confirmed module-wide reset returns persisted configuration and encrypted credentials to first-run defaults without commanding the lights; the user is instructed to restart SimHub afterward.
- Presets, game profiles, and actions use uniform read-only lists with Add/Edit/Delete and themed transactional dialogs. Cancel discards draft changes; Save preserves stable IDs and immutable action keys.
- Preset Color/Brightness summaries report `Custom` only when effective device/segment values differ. Viewing Simple mode preserves advanced overrides; removing them requires an explicit warned conversion.
- A guarded Device Compatibility Test generates a previewable sanitized `.txt` report for a GitHub issue. It never includes credentials, device IDs/names, network addresses, raw responses, or stable fingerprints.
- GitHub submission remains user-controlled: save the report, open its folder, copy an issue summary, and open the repository issue form. The plugin does not authenticate to GitHub or upload automatically.
- One report is evidence, not an automatic model default. Built-in mappings normally require two independent matching reports or maintainer hardware verification, followed by a regression fixture.

Future decisions concern telemetry-driven effects, richer scene/mode support, reusable named device groups, and advanced formula/property integration. They do not block v0.3.0.
