# Product Decisions

The version 0.1 decisions are complete:

- Name: **SimHub Govee**.
- First verified hardware: H6046 RGBIC TV Light Bars, while retaining capability-based support for similar devices.
- Discovery and state: current Govee Developer API.
- Normal control: Hybrid by default—direct UDP 4003 to a reserved device IP, with cloud fallback. Cloud-only and Local-only remain selectable.
- Credential storage: the user's own Developer API key, encrypted by Windows DPAPI for the current user. No shared key or Desktop API GUID ships.
- Devices: multi-select, mirrored behavior by default, and DreamView logical entries hidden by default.
- Startup: configured ON by default or Leave Unchanged.
- Clean exit: OFF by default, with Leave Unchanged, configured ON/OFF, and Restore Previous alternatives.
- Restore Previous: restore known power, brightness, and RGB captured through cloud. Exact scene/music/DreamView restoration is not claimed.
- Version 0.1 controls: safe manual power testing. Brightness/color transport support is implemented for future UI exposure after broader-device validation.
- Telemetry effects and advanced configuration are deferred.

## Verified constraints

- The Govee Desktop DLL contract exists, but its GUID consistently returned error `1001`; it is not a product dependency.
- The H6046 did not answer public LAN discovery or state requests, even on the same subnet.
- The H6046 did accept fire-and-forget UDP 4003 power commands at its manual IP.
- A guarded test verified local OFF and restore ON through cloud state. Cloud state lagged by up to about six seconds.
- LAN commands have no acknowledgement. Explicit setup tests use cloud verification; ordinary sends do not query state after every command.
- Clean-exit behavior is best-effort. An in-process plugin cannot act after a crash, forced termination, or power loss.

Future product decisions concern telemetry effects, scene/mode support, richer per-device controls, and whether to publish SimHub actions/properties. They do not block version 0.1.
