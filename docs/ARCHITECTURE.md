# Architecture and Reliability Plan

## Goals and scope

Govee Controller Plugin for SimHub controls selected Govee lights without blocking SimHub's telemetry thread. It appears as Govee Controller in SimHub's menu. Version 0.2 adds transition-driven game profiles and manual SimHub actions while keeping high-frequency telemetry effects, DreamView emulation, scenes, and animation outside the current scope.

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

Settings schema v4 migrates earlier settings without changing existing devices or lifecycle behavior. The migration repairs v0.2 profiles that retained a selected preset while being stored as Power On, changing that unambiguous pre-release combination to Apply Preset. The schema provides reusable presets, a safe Leave-Unchanged default game profile, custom profiles keyed case-insensitively by SimHub game code, manual action definitions, opt-in pre-action cloud refresh, and persisted last-known power per device. Empty target lists consistently mean all globally selected devices; otherwise only listed selected device IDs receive the state.

Named SimHub actions are registered as `SimHubGovee.<immutable key>`. SimHub action callbacks are parameterless, so Set Color references a persistent preset rather than trying to receive arbitrary values at click time. Labels, presets, action types, and targets may be updated without changing the key. Deletion warns that external bindings will stop working.

Managed-action reconciliation always provides LightsOn, LightsOff, and LightsToggle plus one color action per preset. Generated color keys remain stable across preset renames and are removed with their preset. Managed color actions inherit preset targets and force power on. Toggle turns all targets off only when every tracked target is on; mixed or unknown state turns all targets on. Successful commands, cloud reads, startup policy, profiles, and restoration update persisted power tracking. Optional pre-action refresh queries cloud state when a Developer API key is saved, then gracefully falls back to tracking if refresh fails.

Command order within one desired state is brightness, RGB, then power. Power On without a preset contains only power and therefore preserves current color/brightness. Manual states remain until another manual command or lifecycle/game transition supersedes them. Clean shutdown cancels outstanding automation before applying the exit policy.

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

Published limits observed during research were 30 device-list requests/minute/account, 30 state requests/minute/device, 12 controls/second/account, and 2 controls/second/device (120/minute/device). Version 0.2 has no frame-driven lighting output and serializes commands, keeping normal use comfortably below these ceilings.

## Lifecycle state and game restoration

Before the first startup action, cloud-capable selected devices are queried and their known state is retained in memory. Startup can apply configured power or make no change. On normal exit:

- Off sends power off.
- Leave Unchanged sends nothing.
- Configured State sends the saved exit power.
- Restore Previous sends captured power, brightness, and RGB where available.

Cloud state cannot reconstruct active scenes, music modes, segmented effects, or DreamView. Restore Previous therefore restores only known primitive fields and documents that limitation. Snapshots are intentionally not persisted across processes because stale state is more dangerous than an incomplete best-effort restore.

The game coordinator captures the original state only on the first game start, retains it across direct game switches, and restores it when no game remains active. Hybrid/Cloud devices use cloud snapshots. Local-only devices use the dispatcher's last commanded state when one exists; otherwise the global configured startup power is applied as a visible best-effort fallback.

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

Hardware testing remains opt-in through confirmed UI controls or the guarded research script. Release packaging builds/tests first, includes only the plugin DLL, README, and MIT license, then emits a SHA-256 checksum. See [Testing](TESTING.md).

## Future extension points

The transport already supports brightness and whole-device RGB. The next milestone can expose these based on returned capabilities, then introduce an immutable desired-light state, per-device latest-state queues, rate limiting/coalescing, and telemetry effect arbitration. Suggested future priority is race safety, proximity, pit/limiter, RPM/shift, then idle lighting.
