# Architecture and Reliability Plan

## Goals and scope

Govee Controller Plugin for SimHub controls selected Govee lights without blocking SimHub's telemetry thread. It appears as Govee Controller in SimHub's menu. Version 0.3.0 adds optional per-device segmented appearances, transactional editors, timed physical preview, compatibility reporting, transition-driven game profiles, and manual SimHub actions while keeping high-frequency telemetry effects, DreamView emulation, scenes, and animation outside the current scope.

The H6046 is the reference hardware, but discovery records model device identity, SKU, capability names, transport, and IP independently. Unsupported devices can use Cloud mode; devices proven to accept local commands can use Hybrid or Local-only.

## Runtime components

```text
GoveePlugin (SimHub IDataPlugin / IWPFSettingsV2)
  ├─ SettingsView: credential, devices, tests, lifecycle policies
  ├─ GoveeController: routing, fallback, snapshot and restore
  ├─ GoveeLanClient: validated fire-and-forget UDP 4003 commands
  ├─ GoveeCloudClient: discovery, state, and control REST calls
  └─ DpapiCredentialProtector: current-user encrypted key storage

AutomationPolicy / GameTransitionDetector
  ├─ versioned presets, game profiles, targets, and actions
  ├─ debounced start/switch/stop decisions
  └─ pure deterministic policy functions

LightStateDispatcher / GameSessionCoordinator
  ├─ one desired-state path for profiles and manual actions
  ├─ latest-generation cancellation of obsolete queued work
  └─ pre-game snapshot and best-effort restoration
```

`DataUpdate` performs only game identity/process-state transition detection. A game becomes active when SimHub detects its process or reports live telemetry, whichever comes first. Profile lookup uses `SupportedGameManager.Code`, with manager/data names only as compatibility fallbacks. Network and state application run asynchronously. A two-second stop debounce ignores brief detection or telemetry gaps. Clean-exit work is bounded to five seconds so an unreachable service cannot hang SimHub indefinitely.

## Profiles, presets, and actions

Settings schema v5 migrates earlier settings without changing existing devices, preset IDs, action keys, or lifecycle behavior. The migration repairs v0.2 profiles that retained a selected preset while being stored as Power On, changing that unambiguous pre-release combination to Apply Preset. The schema provides reusable presets with optional per-device segment appearances, a safe Leave-Unchanged default game profile, custom profiles keyed case-insensitively by SimHub game code, manual action definitions, opt-in pre-action cloud refresh, and persisted last-known power per device. Empty target lists consistently mean all globally selected devices; otherwise only listed selected device IDs receive the state.

Named SimHub actions are registered as `SimHubGovee.<immutable key>`. SimHub action callbacks are parameterless, so Set Color references a persistent preset rather than trying to receive arbitrary values at click time. Labels, presets, action types, and targets may be updated without changing the key. Deletion warns that external bindings will stop working.

The settings surface follows one list-and-dialog interaction model for presets, custom game profiles, and SimHub actions. Summary grids are read-only and expose Add/Edit/Delete plus double-click editing. Dedicated XAML windows inherit the owning SimHub window's theme and edit deep draft copies; only Save replaces the corresponding settings object. Cancel therefore cannot leak partial changes. Existing IDs and immutable action keys are copied into drafts and preserved. The protected Default Game Profile uses the same profile editor, while managed actions either explain that they are automatic or route managed color edits to their owning preset.

Preset summary values are semantic rather than raw storage fields. Color reports a HEX value only when all effective segment/device colors collapse to one value; otherwise it reports `Custom`. Brightness follows the same rule. The editor keeps universal identity and application fields outside the appearance-mode tabs: name is always visible, and power/target scope share an Application behavior section. Simple mode changes the uniform fallback while preserving hidden advanced appearances. Removing advanced state requires an explicit warned conversion, preventing accidental data loss when users move between editor tabs.

Managed-action reconciliation always provides LightsOn, LightsOff, and LightsToggle plus one color action per preset. The built-in power/toggle actions preserve configured target IDs across reconciliation; their editor locks key, label, and type while allowing all/specific target selection. Generated color keys remain stable across preset renames and are removed with their preset. Managed color actions inherit preset targets, force power on, and route editing to their owning preset. Toggle turns all targets off only when every tracked target is on; mixed or unknown state turns all targets on. Successful commands, cloud reads, startup policy, profiles, and restoration update persisted power tracking. Optional pre-action refresh queries cloud state when a Developer API key is saved, then gracefully falls back to tracking if refresh fails.

Command order within one desired state is whole-device brightness, whole-device RGB, grouped segmented RGB/brightness, then power. Power On without a preset contains only power and therefore preserves current color/brightness. Manual states remain until another manual command or lifecycle/game transition supersedes them. Clean shutdown cancels outstanding automation before applying the exit policy.

## Transport policy

- **Hybrid:** if a device has a manual/reserved IPv4 address, send power/brightness/RGB locally. On a local socket/validation failure, use cloud control when fallback is enabled. Explicit power tests also poll cloud state every two seconds, up to four attempts.
- **Cloud:** send control through the Govee Developer API. No local IP is required.
- **Local-only:** send only UDP 4003. This can operate without an API key after a device is manually persisted, but state verification and true previous-state restoration are unavailable.

Successful UDP send means the datagram was handed to the network stack, not that the light applied it. Routine commands avoid cloud verification to preserve responsiveness and respect API quotas. The settings panel clearly labels verified tests.

## Cloud contract

The adapter uses `https://openapi.api.govee.com/router/api/v1`:

- `GET /user/devices`
- `POST /device/state`
- `POST /device/control`
- header `Govee-API-Key`

Responses are parsed defensively and non-200 HTTP/API codes become sanitized exceptions. HTTP 401 and 429 receive actionable messages. Device identity and SKU are required for state/control. Current known state fields are online, power, brightness, RGB integer, and color temperature.

Published limits observed during research were 30 device-list requests/minute/account, 30 state requests/minute/device, 12 controls/second/account, and 2 controls/second/device (120/minute/device). Version 0.3.0 has no frame-driven lighting output and serializes commands, keeping normal use comfortably below these ceilings.

## Segmented devices and community compatibility reports

The next segmented-lighting milestone keeps a uniform whole-device color as the default and adds optional per-device overrides. Cloud capability metadata determines whether segmented RGB/brightness controls may be offered, but it is treated as a claim rather than a verified physical topology. H6046 testing demonstrated why: the API advertised indices 0–14 while only 0–9 produced visible changes. The verified reference layout is left bar 0–4 and right bar 5–9. Other models must use their own discovered and tested layouts rather than inheriting the H6046 map.

Segmented commands use the Govee cloud API even for Hybrid devices because the documented LAN path supports whole-device color/brightness only. Uniform commands retain the fast LAN path. Preset resolution produces per-device desired states with an inherited uniform appearance plus optional device/segment assignments. Commands group segments that share a color, remain below the per-device cloud rate limit, and are superseded when a newer game/profile/action wins. Existing preset IDs and registered SimHub action keys remain stable during schema migration.

The segmented preset editor uses live physical preview so mounting direction never has to be inferred from numeric indices. Opening it requires cloud access and a successful primitive-state snapshot. The unqueryable-state warning is enabled by default, offers a persistent **Never warn me again** choice, can be re-enabled from preset settings, and inherits SimHub's foreground/background theme. Visual-picker and zone changes preview immediately. Valid typed/pasted HEX and segmented-brightness values use per-field debounce timers, with Enter as an immediate explicit preview; brightness textboxes and 0–100 sliders synchronize without rewriting partial invalid input. Preview commands are serialized to respect rate limits. A timed complete-appearance test groups the current draft's colors and brightness values, powers the target on, counts down for a persisted 1–120 seconds (default 10), and supports cancellation; its `finally` path restores the captured state. Closing by Save, Cancel, or the window control stops pending debounce timers, waits for in-flight previews, and restores the primitive snapshot plus the last segmented pattern known to this plugin. Externally created patterns remain unrestorable and the settings UI continues to state that limitation even when the modal warning is suppressed.

The Advanced preset device list is refreshed from committed main-screen device selections before every Add/Edit operation. Both available and already-configured device rows support double-click as a shortcut to the same configuration command; the explicit button remains for discoverability and keyboard use. New presets may enter segmented configuration before receiving a name so the resulting appearance can supply the normal suggested name. Double-click is not assigned to the editable main Devices grid because editing selection, IP, and transport cells must remain unambiguous.

The settings UI includes an opt-in **Device Compatibility Test** for segmented-capable lights. It performs read-only capability discovery first, explains that Govee may not report current segment state, captures the queryable primitive state, and requires explicit confirmation before changing hardware. Identification proceeds in bounded stages with a visible observation prompt, cancellation, and a finally-path restore. Restore verification polls eventual cloud state and retries a captured brightness command when needed. The UI never describes an accepted cloud command as visual verification.

After a test, the plugin can save a human-readable, machine-parseable `.txt` report with a report-schema version. The tester is instructed to review the saved file before attaching it. It may contain plugin version, SKU, normalized capability metadata, advertised and effective segment indices, sanitized test outcomes, tester observations, and primitive state-read results. It must never contain the API key, encrypted key, device ID, device name, IP/MAC address, raw API response, account identifier, or unrelated SimHub logs. No stable device fingerprint is generated.

The plugin saves the sanitized report through a standard file dialog and can open the GitHub compatibility issue form afterward. It does not request a GitHub token or upload automatically. The tester reviews and attaches the file. The repository supplies `.github/ISSUE_TEMPLATE/device-compatibility.yml` with structured model fields, a required sanitized `.txt` upload, observations, result, and a privacy-confirmation checkbox. A single community report is accepted as evidence but not automatically promoted to a trusted built-in topology: conflicting results remain device-specific, while a model default requires reproducible evidence (normally two independent matching reports or maintainer hardware verification). Every accepted mapping becomes a deterministic topology/parser regression fixture.

## Lifecycle state and game restoration

Before the first startup action, cloud-capable selected devices are queried and their known state is retained in memory. Configured startup can apply a selected reusable preset—with its targets, primitive fields, and per-device segmented appearance—or fall back to the original power-only setting when no preset is selected. Leave Unchanged sends no startup command. On normal exit:

- Off sends power off.
- Leave Unchanged sends nothing.
- Configured State sends the saved exit power.
- Restore Previous sends captured power, brightness, and RGB where available.

Cloud state cannot reconstruct active scenes, music modes, segmented effects, or DreamView. Restore Previous therefore restores only known primitive fields and documents that limitation. Snapshots are intentionally not persisted across processes because stale state is more dangerous than an incomplete best-effort restore.

The game coordinator captures the original state only on the first game start, retains it across direct game switches, and restores it when no game remains active. Hybrid/Cloud devices use cloud snapshots. Local-only devices use the dispatcher's last commanded state when one exists; otherwise the global configured startup power is applied as a visible best-effort fallback.

The Status and recovery section exposes a destructive settings reset for corrupted or unusable configuration. After explicit confirmation it replaces the persisted module settings with a normalized first-run object, including removal of the encrypted credential, device/IP data, presets, profiles, action definitions, lifecycle choices, and preferences. It sends no light command. The UI requires a SimHub restart afterward because dispatcher snapshots and previously registered action callbacks may still exist in memory for the current process.

## Security and privacy

- API keys are encrypted with `ProtectedData`/DPAPI `CurrentUser` plus application entropy.
- The settings panel never displays a saved key.
- No credential, shared default, or Govee binary is included in builds.
- Normal plugin logs contain only sanitized errors, not response payloads or device identifiers.
- Vendor documentation and probes are research artifacts and are not runtime dependencies.

## Failure handling

- Network work is asynchronous and never runs in `DataUpdate`.
- Commands are serialized to avoid device races.
- Hybrid fallback is optional and explicit.
- HTTP requests have a 12-second timeout; plugin shutdown has a tighter global bound.
- Cloud authentication, rate limiting, invalid IP, and missing configuration produce user-facing status instead of crashing SimHub.
- Settings are normalized on load/save, and devices returned by refresh are merged with saved selection, IP, and transport by cloud device ID.

## Verification and releases

The console test project has no external package dependency. It verifies settings, validation, DPAPI round trips, cloud JSON contracts, header use, device merging, transport routing/fallback, and startup policy using fake clients. Automated tests never access real hardware or the internet.

Hardware testing remains opt-in through confirmed UI controls or the guarded research script. Community reports use the same sanitization, confirmation, and restoration rules before they can be attached to the repository's device-compatibility issue form. Release packaging builds/tests first, includes only the plugin DLL, README, and MIT license, then emits a SHA-256 checksum. See [Testing](TESTING.md).

## Future extension points

The transport and desired-state pipeline now support whole-device and verified segmented RGB/brightness. Future work can add reusable device groups, richer capability-specific scenes, rate-aware transitions, and optional telemetry effect arbitration. Suggested effect priority remains race safety, proximity, pit/limiter, RPM/shift, then idle lighting; ambient screen matching remains outside this plugin's purpose.
