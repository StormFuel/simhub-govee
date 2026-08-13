# SimHub Govee

SimHub Govee is a Windows plugin for controlling compatible Govee lights from SimHub. The first verified device is the **H6046 RGBIC TV Light Bars**, but devices are discovered from Govee capability metadata rather than hard-coded by model.

The included plugin icon is original project artwork and does not use Govee's logo or wordmark.

Version 0.1 provides device discovery and selection, manual verified power tests, configurable startup and clean-exit behavior, encrypted credential storage, and hybrid local/cloud control. Telemetry-driven effects are intentionally left for a later release.

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

Start SimHub, enable **SimHub Govee** in its Plugins settings, then:

1. Open the SimHub Govee panel and save your Developer API key.
2. Refresh devices and select the lights SimHub should control.
3. For Hybrid mode, enter each light's DHCP-reserved IPv4 address.
4. Save the choices and use the confirmed **Test ON/OFF + verify** controls.
5. Choose startup and clean-exit behavior.

The default startup state is ON. The default clean-exit action is OFF. Other exit choices are Leave Unchanged, a configured ON/OFF state, or Restore Previous. Restore Previous captures and restores known power, brightness, and RGB state; the cloud API does not expose enough current-state data to reproduce an active scene or music/DreamView mode exactly. Exit handling is best-effort on a normal SimHub shutdown and cannot run after a crash, forced termination, or power loss.

## Privacy, limits, and troubleshooting

- The API key is stored only as a current-user DPAPI ciphertext in SimHub settings.
- Device names and IDs are not deliberately logged by the plugin.
- DreamView logical devices are hidden by default.
- The cloud API is rate-limited. Avoid repeatedly refreshing or hammering verified tests.
- If local commands fail, confirm LAN Control is still enabled, the IP reservation is current, and UDP 4003 is allowed between VLANs. Cloud-only mode can confirm whether the credential/device side is healthy.
- If the key is rejected, verify it is a Developer API key—not the Desktop OpenAPI GUID.

## Release package

```powershell
.\scripts\package-release.ps1 -Version 0.1.0
```

This produces a ZIP and SHA-256 checksum under `artifacts`. Govee Desktop binaries and user credentials are never packaged.

More detail is in the [architecture plan](docs/ARCHITECTURE.md). The original probes remain as research artifacts. Vendor-provided documentation is retained locally under `resources` but excluded from source and release distribution because redistribution rights were not granted.

## License

MIT. Govee and SimHub names and trademarks belong to their respective owners. This is an independent community integration.
