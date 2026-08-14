# Testing

Automated tests are deterministic and never contact Govee or send UDP packets. Run:

```powershell
.\scripts\build.ps1
```

The suite covers settings migration/defaults, RGB/HEX validation, DPAPI encryption, cloud contracts, device merging, local/cloud routing, stable SimHub game-code resolution, process-detection activation, profile resolution, target selection, managed-action reconciliation and stable keys, tracked toggle decisions, transition debouncing, desired-state ordering, snapshots, and Local-only restoration fallback.

Hardware tests are always opt-in. The settings panel's **Test ON + verify** and **Test OFF + verify** buttons name the selected device count, ask for confirmation, send the command, and query cloud state until it is observed. These buttons do not automatically restore the old state; use the opposite test button when finished. The research-only guarded restore script remains available at `scripts\interactive-govee-hybrid-power-test.ps1`.

Before a release candidate:

1. Build and run the automated suite.
2. Install the DLL with SimHub closed.
3. Start SimHub, enable the plugin, save a Developer API key, and refresh devices.
4. Select the target, enter its reserved IP, and verify ON then OFF.
5. Test each startup and clean-exit policy. Remember that crash/power-loss behavior cannot be guaranteed by an in-process plugin.
6. Confirm no secret appears in the repository, ZIP, logs, or settings UI.
