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
- Managed SimHub actions preload Power On, Power Off, and Toggle, while each reusable preset automatically owns a managed Set Color action that forces power on.
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

Future decisions concern telemetry-driven effects, richer scene/mode support, reusable named device groups, and advanced formula/property integration. They do not block v0.2.
