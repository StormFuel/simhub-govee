# Govee Controller Plugin for SimHub

<p align="center">
  <img src="design/simhub-govee.png" alt="Govee Controller Plugin for SimHub logo" width="180">
</p>

Govee Controller Plugin for SimHub is a Windows plugin for controlling compatible Govee room lights from SimHub. It appears as **Govee Controller** in SimHub's menu. The first verified device is the **H6046 RGBIC TV Light Bars**, but devices are discovered from Govee capability metadata rather than hard-coded by model.

> **This is not an ambient-lighting or screen-color-matching plugin.** SimHub already has a separate plugin for Govee ambient lighting. Govee Controller is intended for lights that are not supported by that ambient-lighting integration, as well as users who want direct control over the lighting in their room.

The goal is to put your room lights into a chosen default state when SimHub starts, automatically apply different settings when specific games start, restore their previous state when gameplay ends, and expose lighting actions to SimHub. Those actions can be assigned to button boxes, Dash Studio dashboard buttons, or other SimHub controls for manual power and color changes.

The included plugin icon is original project artwork and does not use Govee's logo or wordmark.

Version 0.3.0 adds optional per-device and per-segment preset appearances plus a guarded community compatibility test/report workflow. It retains reusable presets, default and per-game profiles, targeted lights, pre-game restoration, managed SimHub actions, encrypted credentials, verified power tests, and hybrid local/cloud control.

## Screenshots

### Setup and device selection

![Developer API setup, device discovery, and local IP configuration](screenshots/version%200.3.0/Screenshot%202026-08-15%20134914.png)

### Reusable light presets

![Reusable Simple and Advanced light preset list](screenshots/version%200.3.0/Screenshot%202026-08-15%20134925.png)

The preset name and application behavior remain visible regardless of appearance mode:

![Simple preset appearance and universal application behavior](screenshots/version%200.3.0/Screenshot%202026-08-15%20134944.png)

Advanced mode associates segmented appearances with verified physical devices:

![Advanced preset editor with available device and configured override](screenshots/version%200.3.0/Screenshot%202026-08-15%20134938.png)

### Live segmented appearance editor

The safety warning explains the restoration limits before physical preview begins:

![Live segmented preview warning and optional suppression](screenshots/version%200.3.0/Screenshot%202026-08-15%20134953.png)

The segmented editor provides per-section colors and brightness plus a timed complete-appearance test:

![Segmented appearance editor with per-segment controls and timed test](screenshots/version%200.3.0/Screenshot%202026-08-15%20135000.png)

### Per-game profiles

![Default and custom SimHub game profiles](screenshots/version%200.3.0/Screenshot%202026-08-15%20135014.png)

Profiles use SimHub's registered game list and the same target-light workflow:

![Game profile editor with game, behavior, preset, and target selection](screenshots/version%200.3.0/Screenshot%202026-08-15%20135023.png)

### Lifecycle behavior

![Startup preset, clean-exit policy, and cloud fallback settings](screenshots/version%200.3.0/Screenshot%202026-08-15%20135113.png)

Screenshots from the previous 0.2.0 interface remain archived under [`screenshots/version 0.2.0`](screenshots/version%200.2.0/).

## Architecture

```text
SimHub lifecycle / settings panel
              |
              v
       policy + controller
        /             \
UDP 4003 commands   Govee Developer API
(fast local path)   (discovery, state, fallback)
```

For a Hybrid device, routine power/brightness/color commands go directly to its reserved IPv4 address. Local UDP commands are fire-and-forget, so the explicit test buttons confirm their result using the eventually consistent cloud state endpoint. If a local send fails, the plugin can automatically use cloud control. Cloud-only and Local-only modes are also available per device.

The plugin never depends on Govee Desktop or its DLL. The supplied Desktop API GUID repeatedly failed with Govee error `1001`; it is not compiled into the plugin. The user supplies their own **Govee Developer API key**, which is encrypted with Windows DPAPI for the current user and never redisplayed.

## Requirements

- Windows and SimHub with .NET Framework 4.8
- A Govee Developer API key
- For fast local control: LAN Control enabled in the Govee Home mobile app
- A DHCP reservation/manual IPv4 address for each local target
- Network routing that permits the PC to send UDP 4003 to the light

The PC and light may be on different subnets. Multicast discovery is not required because cloud discovery supplies device identity and the user supplies the reserved local IP. Incoming UDP 4002 and its firewall rule are only needed by the research LAN discovery probe, not by the released plugin.

## Build and test

SimHub is expected at `C:\Program Files (x86)\SimHub`. Override that at build time with `SIMHUB_INSTALL_PATH` if necessary.

```powershell
.\scripts\build.ps1
```

Automated tests use fake cloud/LAN transports and do not change real lights. See [Testing](docs/TESTING.md) for the hardware release checklist.

## Install

Close SimHub, then copy `SimHub.Govee.dll` to the SimHub installation directory, normally `C:\Program Files (x86)\SimHub`. An elevated PowerShell can install a local release build with:

```powershell
.\scripts\install-development.ps1
```

Start SimHub, enable **Govee Controller Plugin for SimHub** in its Plugins settings, then:

1. Open **Govee Controller** and save your Developer API key.
2. Refresh devices and select the lights SimHub should control.
3. For Hybrid mode, enter each light's DHCP-reserved IPv4 address.
4. Save the choices and use the confirmed **Test ON/OFF + verify** controls.
5. Choose startup and clean-exit behavior.

## Presets and per-game behavior

The main page presents presets as a read-only summary list with consistent **Add**, **Edit**, and **Delete** commands; double-click also edits. Each editor works on a draft, so Cancel cannot partially mutate the saved preset. Universal fields remain visible outside the appearance tabs: the preset name is above them, while power behavior and named target-light selection are grouped under **Application behavior** below them. Simple mode provides the visual picker/HEX color, swatches, and optional brightness. Advanced mode preserves that uniform fallback and adds per-device segmented appearances with live preview. Switching back to view Simple mode does not silently discard advanced data; removing it requires the warned **Convert to simple preset** command.

The summary Color column displays the actual HEX value only when the effective preset is one solid color. It displays **Custom** when devices or segments use different colors. Brightness follows the same rule. Preset IDs and automatically generated SimHub action keys remain stable across edits and renames.

The Default Game Profile applies to every game without a custom profile and initially uses **Leave Unchanged**, making upgrades non-disruptive. Default and custom profiles use dedicated draft editors with behavior, preset, enabled state, and target lights. Custom games are chosen from SimHub's registered game catalog, with current-game detection as a shortcut. A profile may leave lights unchanged, turn them on while preserving color/brightness, turn them off, or apply a preset.

The preset's single color remains the default for every target. **Per-device / segmented colors** is an optional advanced editor for devices with a verified physical mapping. Devices checked **Use** can be opened with **Configure selected device** or by double-clicking the available/configured device row; a new preset can be configured before it is named. The editor provides a visual picker beside every segment, retains optional HEX entry for precision, can apply one color to a named zone such as an entire H6046 bar, and supports optional brightness per segment through synchronized 0–100 textboxes and sliders. Picker and zone changes preview immediately; valid typed or pasted HEX/brightness values preview after a short debounce, while Enter previews immediately. **Test complete appearance** applies the entire draft for a configurable 1–120 seconds (10 by default), offers Stop, and then restores the captured pre-editor state. This avoids assumptions about mounting direction or API numbering without interfering with normal editing or paste. Closing the editor restores the captured pre-preview state whether changes were saved or cancelled. H6046 uses the hardware-verified map 0–4 for the left bar and 5–9 for the right bar; Govee advertises 0–14 for this model, but 10–14 did not produce a visible result in testing.

For memorable names, use physical position followed by color: `Left Red - Right Blue`, `Top White - Bottom Cyan`, or `Left Mixed - Right Green`. When a segmented override is saved and the preset name is still blank or generic, the plugin suggests that form automatically. Standard swatch colors receive friendly names; uncommon colors retain their `#RRGGBB` value. A name the user already entered is never overwritten.

Segmented color and brightness always use the Govee Developer cloud API, including for Hybrid devices. Save the API key in Step 1 and do not use Local Only mode for a segmented target. Before live preview, the plugin displays a warning explaining that Govee accepts segment commands but does not return the current segmented pattern. The warning includes **Never warn me again**; it can be restored later with **Show the safety warning before live segmented preview** in the preset settings. The plugin restores readable power, brightness, and whole-light RGB plus a segmented pattern previously commanded by this plugin. A pattern changed by another app, scene, music mode, or DreamView cannot be reconstructed exactly.

When SimHub detects the first game process in a session, the matching profile applies immediately without waiting for live telemetry. The plugin uses SimHub's stable game code (such as `AssettoCorsa` or `FH6`) rather than its display name. Hybrid/Cloud devices have their known power, brightness, and RGB captured through cloud state. Switching games applies the new profile without losing that original snapshot. When gameplay ends, the original known state is restored. Local-only devices cannot report physical state; the plugin restores its last commanded state when available, otherwise it applies the global default as a documented best effort. Scenes, music modes, segmented effects, and DreamView cannot be recreated exactly.

## SimHub and Dash Studio actions

Actions use the same read-only list and Add/Edit/Delete dialog workflow. Create custom actions with one of these types:

- Power On
- Power Off
- Toggle Power
- Set Color, using a saved preset

Each custom action targets all selected lights or specific lights. It appears in SimHub under a stable name such as `SimHubGovee.RaceRed`, where it can be assigned in Controls and Events or triggered by a Dash Studio button. The permanent action key is editable only during creation and becomes read-only afterward, so existing bindings survive later changes to its label, color preset, or targets. Deleting an action warns that its existing mappings will stop working. Double-clicking a managed color action opens its associated preset rather than exposing duplicate color settings.

`SimHubGovee.LightsOn`, `SimHubGovee.LightsOff`, and `SimHubGovee.LightsToggle` are managed defaults. Double-clicking or editing one opens its configuration dialog: its permanent identity and behavior stay locked, but it may target all selected lights or chosen lights. Every preset receives a managed `Color_<name>` action that always turns on the target and applies its color. Double-clicking that color action opens its owning preset. Renaming the preset updates the label without changing the immutable registered key; deleting the preset deletes its generated action after warning. Managed color actions inherit the preset's target selection.

Toggle uses the per-light power state tracked across startup, profiles, restores, and successful commands. If another application may change the lights, enable **Refresh power state from Govee Cloud before every SimHub action**. It is off by default, uses the Developer API key saved in Step 1, and adds a cloud request before each action. If refresh fails, the action continues using tracked state.

The internal DLL name, settings identity, and `SimHubGovee.*` registered action prefix intentionally remain unchanged for upgrade compatibility. This preserves existing settings and button bindings while the user-facing product name changes.

A manual action remains in effect until another manual action or a game start, switch, stop, or SimHub shutdown policy changes the desired state.

The default startup state is ON. Startup can instead apply any saved light preset, including its color, brightness, segmented per-device appearance, power behavior, and target-light selection. **No preset — power only** retains the original ON/OFF behavior, while **Leave Unchanged** sends no startup command. The default clean-exit action is OFF. Other exit choices are Leave Unchanged, a configured ON/OFF state, or Restore Previous. Restore Previous captures and restores known power, brightness, and RGB state; the cloud API does not expose enough current-state data to reproduce an active scene or music/DreamView mode exactly. Exit handling is best-effort on a normal SimHub shutdown and cannot run after a crash, forced termination, or power loss.

## Privacy, limits, and troubleshooting

- The API key is stored only as a current-user DPAPI ciphertext in SimHub settings.
- Device names and IDs are not deliberately logged by the plugin.
- DreamView logical devices are hidden by default.
- The cloud API is rate-limited. Avoid repeatedly refreshing or hammering verified tests.
- If local commands fail, confirm LAN Control is still enabled, the IP reservation is current, and UDP 4003 is allowed between VLANs. Cloud-only mode can confirm whether the credential/device side is healthy.
- If the key is rejected, verify it is a Developer API key—not the Desktop OpenAPI GUID.
- **Reset all plugin settings** under Status and recovery returns the module to first-run defaults if its configuration becomes unusable. After a destructive confirmation it removes the encrypted key, devices/IPs, presets, profiles, action configuration, lifecycle choices, and preferences without immediately changing the lights. Restart SimHub afterward to clear in-memory state.

## Testing another Govee model

Select one device row and choose **Device compatibility test / report**. A read-only report is always available. If the device advertises segmented RGB and has cloud access, the optional active test first warns that an existing segmented effect may be lost, captures the readable whole-light state, sets each advertised segment to a contrasting color in turn, and asks the tester whether a physical section changed. Readable power, brightness, and whole-light RGB are restored afterward in a `finally` path.

The saved `.txt` report includes the SKU, normalized capability names, advertised/verified indices, command-test results, and the tester's physical observations. It intentionally excludes the API key, encrypted credential, device ID/name, IP or MAC address, account identifiers, and raw API responses. Review the file before attaching it to the repository's **Device compatibility test** issue form. A model mapping is not promoted from one unconfirmed report; normally it needs two matching independent reports or maintainer hardware verification.

## Release package

```powershell
.\scripts\package-release.ps1 -Version 0.3.0
```

This produces a ZIP and SHA-256 checksum under `artifacts`. Govee Desktop binaries and user credentials are never packaged.

More detail is in the [architecture plan](docs/ARCHITECTURE.md). The original probes remain as research artifacts. Vendor-provided documentation is retained locally under `resources` but excluded from source and release distribution because redistribution rights were not granted.

## License

MIT. Govee and SimHub names and trademarks belong to their respective owners. This is an independent community integration.
