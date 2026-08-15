# Testing

Automated tests are deterministic and never contact Govee or send UDP packets. Run:

```powershell
.\scripts\build.ps1
```

The suite covers settings migration/defaults, RGB/HEX validation, DPAPI encryption, cloud and segmented-command contracts, advertised-range parsing, H6046 topology, per-device preset resolution, segment command grouping, sanitized compatibility reports, device merging, local/cloud routing, stable SimHub game-code resolution, process-detection activation, profile resolution, target selection, managed-action reconciliation and stable keys, tracked toggle decisions, transition debouncing, desired-state ordering, snapshots, and Local-only restoration fallback.

Hardware tests are always opt-in. The settings panel's **Test ON + verify** and **Test OFF + verify** buttons name the selected device count, ask for confirmation, send the command, and query cloud state until it is observed. These buttons do not automatically restore the old state; use the opposite test button when finished. The research-only guarded restore script remains available at `scripts\interactive-govee-hybrid-power-test.ps1`.

The **Device compatibility test / report** can remain read-only or, after an explicit destructive-pattern warning, test each segmented index advertised by the selected model. The active test captures readable primitive state, uses red/blue identification, records visible-response confirmations, and attempts restoration in a `finally` path. Govee does not expose the current segmented pattern, so an earlier pattern/scene may be lost even when primitive restoration succeeds. Review the generated `.txt` before attaching it to GitHub.

Before a release candidate:

1. Build and run the automated suite.
2. Install the DLL with SimHub closed.
3. Start SimHub, enable the plugin, save a Developer API key, and refresh devices.
4. Select the target, enter its reserved IP, and verify ON then OFF.
5. Test each startup and clean-exit policy. Remember that crash/power-loss behavior cannot be guaranteed by an in-process plugin.
6. Confirm no secret appears in the repository, ZIP, logs, or settings UI.
7. For H6046, apply a preset with different left/right bar colors and an individual-segment color, then confirm game-stop restoration matches the last pattern commanded by this plugin.
8. Generate a compatibility report and confirm it contains no API key, device ID/name, IP/MAC address, account identifier, or raw API response.
9. In both SimHub light and dark themes, open the segmented editor and confirm the warning text is readable. Select **Never warn me again**, verify later previews skip it, then re-enable it from preset settings. Preview individual and zone colors with the picker, type/paste several valid HEX values, and type/paste/slide segmented brightness values. Every valid field must preview after the debounce without requiring another picker action. Close through Save, Cancel, and the window close control; each path must restore the captured pre-preview state.
10. Select a startup preset, restart SimHub, and verify its targets, whole colors, segmented colors, brightness, and power behavior. Repeat with **No preset — power only** and **Leave Unchanged**.
11. Export or note any configuration needed for testing, then exercise **Reset all plugin settings**. Cancel once to prove it is non-destructive, confirm once, restart SimHub, and verify the API key and all configured entities are gone while the lights were not commanded by reset itself.
12. For presets, game profiles, and custom actions, verify Add/Edit/Delete buttons and row double-click use the same themed dialogs. Change several fields and Cancel to prove the saved object is untouched; Save and confirm stable preset IDs/action keys still drive existing bindings.
13. Confirm a uniform advanced preset displays its single HEX/brightness summary, while mixed device or segment values display `Custom`. View an advanced preset in Simple mode and Cancel; its advanced data must remain. Confirm **Convert to simple preset** warns before removing it.
14. Check a segmented device's **Use** box and immediately choose Add preset. Verify the device is available in Advanced mode without first saving or naming the preset. Open it using both the button and double-click, and double-click an existing configured override to reopen it.
15. In the segmented editor, test the complete appearance for the default 10 seconds and a custom duration. Confirm all current colors and brightness values apply, the countdown is visible, Stop works, and power/color/brightness return to the captured pre-editor state after completion, Stop, window close, and a simulated command failure.
16. Double-click and use Edit on Lights On, Lights Off, and Toggle Lights. Confirm each opens the managed-action dialog, identity/type fields are locked, target selection saves, and restarting SimHub preserves those targets. Double-click a managed color action and confirm it opens the owning preset instead.
