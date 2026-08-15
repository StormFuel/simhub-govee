using SimHub.Govee;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static int _assertions;
    private static async Task<int> Main()
    {
        try
        {
            SettingsTests(); AutomationPolicyTests(); ActionRegistrationTests(); ManagedActionTests(); GameCatalogTests(); GameRuntimeIdentityTests(); TransitionTests(); CredentialTests(); EmbeddedAssetTests(); LanValidationTests(); CompatibilityReportTests(); await CloudParsingTests(); await CloudErrorTests(); await ControllerTests(); await DispatcherTests(); await GameSessionTests();
            Console.WriteLine("PASS: " + _assertions + " assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }
    private static void SettingsTests()
    {
        var s = new PluginSettings { Devices = null }; SettingsValidator.Normalize(s); Equal(0, s.Devices.Count, "normalizes null devices");
        True(SettingsValidator.IsValidRgbHex("#00AfF1"), "accepts RGB hex"); True(!SettingsValidator.IsValidRgbHex("00AfF1"), "requires #"); True(!SettingsValidator.IsValidRgbHex("#GG0000"), "rejects invalid hex");
        var d = new DeviceSettings { Name = "Bars", Sku = "H6046" }; Equal("Bars (H6046)", d.DisplayName, "display name");
        True(new DeviceSettings { Sku = "DreamViewScenic" }.IsLogical, "logical device detection");
        Equal(ExitPolicy.Off, new PluginSettings().ExitPolicy, "safe exit default"); True(new PluginSettings().CloudFallback, "fallback default"); True(new PluginSettings().ShowLivePreviewWarning, "live preview warning defaults on"); Equal(10, new PluginSettings().SegmentTestDurationSeconds, "segment test defaults to ten seconds");
        Equal(5, s.SchemaVersion, "old settings migrate to v5"); True(s.DefaultGameProfile != null, "default profile migrated"); Equal(ProfileBehavior.LeaveUnchanged, s.DefaultGameProfile.Behavior, "default profile is safe");
        Equal("#AABBCC", SettingsValidator.NormalizeHex("#aabbcc"), "hex normalized"); Equal(0xAABBCC, SettingsValidator.HexToRgb("#AABBCC"), "hex converted"); Equal("RaceRed_-1", SettingsValidator.NormalizeActionKey(" Race Red!_ -1"), "action key sanitized");
        var legacy = new PluginSettings { SchemaVersion = 2 };
        var legacyPreset = new LightPreset { Id = "legacy-red", Name = "Red", HexColor = "#FF0000" }; legacy.Presets.Add(legacyPreset);
        legacy.GameProfiles.Add(new GameProfile { GameCode = "AssettoCorsa", Behavior = ProfileBehavior.PowerOn, PresetId = legacyPreset.Id });
        legacy.DefaultGameProfile.Behavior = ProfileBehavior.PowerOn; legacy.DefaultGameProfile.PresetId = legacyPreset.Id;
        SettingsValidator.Normalize(legacy);
        Equal(ProfileBehavior.ApplyPreset, legacy.GameProfiles[0].Behavior, "v2 game profile with selected preset migrates to Apply Preset");
        Equal(ProfileBehavior.ApplyPreset, legacy.DefaultGameProfile.Behavior, "v2 default profile with selected preset migrates to Apply Preset");
        legacy.EncryptedApiKey = null; legacy.RefreshStateBeforeAction = true; SettingsValidator.Normalize(legacy); True(!legacy.RefreshStateBeforeAction, "automatic state refresh is disabled without a saved key");
        legacy.StartupPresetId = "missing"; SettingsValidator.Normalize(legacy); Equal(null, legacy.StartupPresetId, "missing startup preset is cleared safely");
        legacy.SegmentTestDurationSeconds = 0; SettingsValidator.Normalize(legacy); Equal(1, legacy.SegmentTestDurationSeconds, "segment test duration has safe minimum"); legacy.SegmentTestDurationSeconds = 999; SettingsValidator.Normalize(legacy); Equal(120, legacy.SegmentTestDurationSeconds, "segment test duration has safe maximum");
        var mapped = new DeviceSettings { Sku = "h6046" }; SegmentTopologyCatalog.ApplyKnownMapping(mapped); True(mapped.SegmentTopology.HasUsableMapping, "H6046 receives verified mapping"); True(mapped.SegmentTopology.SupportsSegmentedBrightness, "H6046 enables advertised segmented brightness"); Equal("Left bar", mapped.SegmentTopology.Zones[0].Name, "H6046 left zone named"); Equal(9, mapped.SegmentTopology.VerifiedSegmentIndices.Last(), "H6046 effective range ends at 9");
        var capabilityMapped = new PluginSettings { Devices = new List<DeviceSettings> { new DeviceSettings { Sku = "OTHER", Capabilities = new List<string> { "devices.capabilities.segment_color_setting/segmentedBrightness" } } } }; SettingsValidator.Normalize(capabilityMapped); True(capabilityMapped.Devices[0].SegmentTopology.SupportsSegmentedBrightness, "saved capability metadata restores segmented brightness support");
        var named = Enumerable.Range(0, 10).Select(i => new SegmentColorAssignment { SegmentIndices = new List<int> { i }, HexColor = i < 5 ? "#FF0000" : "#0000FF" }).ToList(); Equal("Left Red - Right Blue", PresetNameSuggester.ForSegmentedAppearance(mapped.SegmentTopology, named), "zone colors produce memorable preset name");
        named[1].HexColor = "#00FF00"; Equal("Left Mixed - Right Blue", PresetNameSuggester.ForSegmentedAppearance(mapped.SegmentTopology, named), "mixed zone identified in suggested name"); Equal("#123456", PresetNameSuggester.FriendlyColor("#123456"), "uncommon color keeps precise hex in suggested name");
        var summary = new LightPreset { Id = "summary", HexColor = "#FF0000", Brightness = 50 }; Equal("#FF0000", summary.ColorSummary, "simple preset lists uniform color"); Equal("50", summary.BrightnessSummary, "simple preset lists uniform brightness");
        summary.DeviceAppearances.Add(new DevicePresetAppearance { TargetId = "id", UseSegmentedColor = true, SegmentColors = new List<SegmentColorAssignment> { new SegmentColorAssignment { HexColor = "#0000FF", Brightness = 20, SegmentIndices = new List<int> { 0 } }, new SegmentColorAssignment { HexColor = "#00FF00", Brightness = 80, SegmentIndices = new List<int> { 1 } } } }); Equal("Custom", summary.ColorSummary, "multi-color segmented preset lists Custom"); Equal("Custom", summary.BrightnessSummary, "multi-brightness segmented preset lists Custom");
        summary.DeviceAppearances[0].SegmentColors[1].HexColor = "#0000FF"; Equal("#0000FF", summary.ColorSummary, "single effective segmented color is listed directly");
        var draft = SettingsDrafts.Preset(summary); draft.Name = "Changed"; draft.DeviceAppearances[0].SegmentColors[0].HexColor = "#FFFFFF"; Equal("summary", draft.Id, "preset draft preserves stable ID"); True(summary.Name != draft.Name && summary.DeviceAppearances[0].SegmentColors[0].HexColor != draft.DeviceAppearances[0].SegmentColors[0].HexColor, "preset draft is deep and transactional");
        var profileDraftSource = new GameProfile { Id = "profile-stable", GameCode = "AC", TargetDeviceIds = new List<string> { "id" } }; var profileDraft = SettingsDrafts.Profile(profileDraftSource); profileDraft.TargetDeviceIds.Clear(); Equal("profile-stable", profileDraft.Id, "profile draft preserves stable ID"); Equal(1, profileDraftSource.TargetDeviceIds.Count, "profile draft target edits are transactional");
        var actionDraftSource = new ManualActionDefinition { Id = "action-id", ActionKey = "Permanent", TargetDeviceIds = new List<string> { "id" } }; var actionDraft = SettingsDrafts.Action(actionDraftSource); actionDraft.DisplayName = "Changed"; actionDraft.TargetDeviceIds.Clear(); Equal("Permanent", actionDraft.ActionKey, "action draft preserves immutable key"); Equal(1, actionDraftSource.TargetDeviceIds.Count, "action draft target edits are transactional");
    }
    private static void AutomationPolicyTests()
    {
        var s = new PluginSettings(); var d1 = Device(); var d2 = new DeviceSettings { DeviceId = "id2", Selected = true }; d1.Selected = true; s.Devices.Add(d1); s.Devices.Add(d2);
        var preset = new LightPreset { Id = "red", HexColor = "#FF0011", Brightness = 42, TurnOn = true }; s.Presets.Add(preset);
        var profile = new GameProfile { GameCode = "AssettoCorsa", Behavior = ProfileBehavior.ApplyPreset, PresetId = "red" }; s.GameProfiles.Add(profile);
        Equal(profile, AutomationPolicy.ResolveProfile(s, "assettocorsa"), "custom profile case insensitive"); Equal(s.DefaultGameProfile, AutomationPolicy.ResolveProfile(s, "unknown"), "default profile fallback");
        var state = AutomationPolicy.StateForProfile(s, profile); Equal(true, state.PowerOn, "preset turns on"); Equal(42, state.Brightness, "preset brightness"); Equal(0xFF0011, state.Rgb, "preset color");
        profile.Behavior = ProfileBehavior.PowerOn; state = AutomationPolicy.StateForProfile(s, profile); Equal(true, state.PowerOn, "power profile on"); True(!state.Rgb.HasValue && !state.Brightness.HasValue, "power on preserves color and brightness");
        preset.TurnOn = false; state = AutomationPolicy.StateForPreset(s, preset.Id, "managed", true); Equal(true, state.PowerOn, "managed color action always turns lights on"); preset.TurnOn = true;
        preset.DeviceAppearances.Add(new DevicePresetAppearance { TargetId = d1.TargetId, UseSegmentedColor = true, Brightness = 55, SegmentColors = new List<SegmentColorAssignment> { new SegmentColorAssignment { HexColor = "#0000FF", SegmentIndices = new List<int> { 0, 1 } } } });
        state = AutomationPolicy.StateForPreset(s, preset.Id, "segmented"); var perDevice = state.ForDevice(d1); Equal(55, perDevice.Brightness, "device brightness override applied"); Equal(2, perDevice.SegmentColors[0].SegmentIndices.Count, "device segment override applied"); Equal(0, state.ForDevice(d2).SegmentColors.Count, "other device remains uniform");
        s.StartupPolicy = StartupPolicy.ConfiguredState; s.StartupPresetId = preset.Id; state = AutomationPolicy.StateForStartup(s); Equal(0xFF0011, state.Rgb, "startup preset supplies color"); Equal(2, AutomationPolicy.ResolveStartupTargets(s).Count, "startup preset with no target override uses all selected lights");
        preset.TargetDeviceIds = new List<string> { d1.TargetId }; Equal(1, AutomationPolicy.ResolveStartupTargets(s).Count, "startup preset target selection honored"); s.StartupPolicy = StartupPolicy.LeaveUnchanged; Equal(null, AutomationPolicy.StateForStartup(s), "leave unchanged suppresses startup preset");
        Equal(2, AutomationPolicy.ResolveTargets(s, null).Count, "empty target means all selected"); Equal("id2", AutomationPolicy.ResolveTargets(s, new[] { "id2" })[0].DeviceId, "specific target selected");
        var local = new DeviceSettings { DeviceId = "", IpAddress = "192.0.2.9", Selected = true }; s.Devices.Add(local); Equal("ip:192.0.2.9", local.TargetId, "local-only target identity"); Equal(local, AutomationPolicy.ResolveTargets(s, new[] { "ip:192.0.2.9" })[0], "local-only target resolved");
        d1.LastKnownPower = true; d2.LastKnownPower = true; Equal(false, AutomationPolicy.ToggleShouldTurnOn(new[] { d1, d2 }), "toggle turns all-off when every target is on");
        d2.LastKnownPower = false; Equal(true, AutomationPolicy.ToggleShouldTurnOn(new[] { d1, d2 }), "toggle turns all-on for mixed state");
        d2.LastKnownPower = null; Equal(true, AutomationPolicy.ToggleShouldTurnOn(new[] { d1, d2 }), "toggle treats unknown state as needing on");
    }
    private static void TransitionTests()
    {
        var detector = new GameTransitionDetector(TimeSpan.FromSeconds(2)); var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Equal(GameTransitionType.Started, detector.Observe(true, "GameA", t).Type, "game start detected"); Equal(GameTransitionType.None, detector.Observe(true, "GameA", t.AddSeconds(1)).Type, "steady game ignored");
        Equal(GameTransitionType.Switched, detector.Observe(true, "GameB", t.AddSeconds(2)).Type, "game switch detected"); Equal(GameTransitionType.None, detector.Observe(false, null, t.AddSeconds(3)).Type, "stop begins debounce"); Equal(GameTransitionType.None, detector.Observe(true, "GameB", t.AddSeconds(4)).Type, "brief telemetry loss ignored");
        detector.Observe(false, null, t.AddSeconds(5)); Equal(GameTransitionType.None, detector.Observe(false, null, t.AddSeconds(6)).Type, "stop still debounced"); Equal(GameTransitionType.Stopped, detector.Observe(false, null, t.AddSeconds(7)).Type, "game stop detected");
    }
    private static void GameRuntimeIdentityTests()
    {
        Equal("AssettoCorsa", GameRuntimeIdentity.Resolve("AssettoCorsa", "Assetto Corsa", "Assetto Corsa"), "stable Assetto Corsa code preferred");
        Equal("FH6", GameRuntimeIdentity.Resolve(" FH6 ", "Forza Horizon 6", "Forza Horizon 6"), "stable FH6 code preferred and trimmed");
        Equal("ManagerName", GameRuntimeIdentity.Resolve(null, " ManagerName ", "DataName"), "manager name fallback");
        Equal("DataName", GameRuntimeIdentity.Resolve(null, null, " DataName "), "data name fallback");
        True(GameRuntimeIdentity.IsDetected(false, true), "process detection activates a profile before telemetry");
        True(GameRuntimeIdentity.IsDetected(true, false), "live telemetry activates a profile");
        True(!GameRuntimeIdentity.IsDetected(false, false), "inactive game remains inactive");
    }
    private static void ActionRegistrationTests()
    {
        var actions = new[] { new ManualActionDefinition { ActionKey = "RaceRed" }, new ManualActionDefinition { ActionKey = "racered" }, new ManualActionDefinition { ActionKey = "" }, null, new ManualActionDefinition { ActionKey = "PowerOff" } };
        var plan = ActionRegistrationPlanner.Build(actions); Equal(2, plan.Count, "duplicate/invalid actions excluded"); Equal("SimHubGovee.RaceRed", plan[0].RegisteredName, "stable action prefix"); Equal("SimHubGovee.PowerOff", plan[1].RegisteredName, "second stable action");
    }
    private static void ManagedActionTests()
    {
        var settings = new PluginSettings(); var preset = new LightPreset { Id = "red-id", Name = "Race Red", HexColor = "#FF0000" }; settings.Presets.Add(preset);
        ManagedActionPlanner.Reconcile(settings);
        Equal(4, settings.ManualActions.Count, "three default actions and one preset action generated");
        True(settings.ManualActions.Any(a => a.RegisteredName == "SimHubGovee.LightsOn" && a.Type == ManualActionType.PowerOn && a.IsManaged), "managed LightsOn generated");
        True(settings.ManualActions.Any(a => a.RegisteredName == "SimHubGovee.LightsOff" && a.Type == ManualActionType.PowerOff && a.IsManaged), "managed LightsOff generated");
        True(settings.ManualActions.Any(a => a.RegisteredName == "SimHubGovee.LightsToggle" && a.Type == ManualActionType.TogglePower && a.IsManaged), "managed LightsToggle generated");
        var managedOn = settings.ManualActions.Single(a => a.ActionKey == "LightsOn"); managedOn.TargetDeviceIds = new List<string> { "target-one" }; ManagedActionPlanner.Reconcile(settings); Equal("target-one", settings.ManualActions.Single(a => a.ActionKey == "LightsOn").TargetDeviceIds.Single(), "managed power action preserves configured targets");
        var color = settings.ManualActions.Single(a => a.PresetId == preset.Id); Equal("SimHubGovee.Color_RaceRed", color.RegisteredName, "managed color key generated"); string stableKey = color.ActionKey;
        preset.Name = "Renamed Red"; ManagedActionPlanner.Reconcile(settings); color = settings.ManualActions.Single(a => a.PresetId == preset.Id); Equal(stableKey, color.ActionKey, "preset rename preserves action key"); Equal("Color: Renamed Red", color.DisplayName, "preset rename updates action label");
        settings.Presets.Clear(); ManagedActionPlanner.Reconcile(settings); True(!settings.ManualActions.Any(a => a.Type == ManualActionType.SetColor && a.IsManaged), "deleting preset removes generated action");
        settings = new PluginSettings(); settings.ManualActions.Add(new ManualActionDefinition { ActionKey = "Color_Blue" }); settings.Presets.Add(new LightPreset { Id = "blue-id", Name = "Blue" }); ManagedActionPlanner.Reconcile(settings);
        True(settings.ManualActions.Any(a => a.ActionKey == "Color_Blue_2" && a.IsManaged), "generated color key avoids custom action collision");
    }
    private static void GameCatalogTests()
    {
        var games = new[] { new GameCatalogItem { Code = "AC", Name = "Assetto Corsa" }, new GameCatalogItem { Code = "FH6", Name = "Forza Horizon 6", Hidden = true }, new GameCatalogItem { Code = "ac", Name = "Duplicate" }, null };
        var visible = SimHubGameCatalog.Filter(games); Equal(1, visible.Count, "hidden and duplicate games filtered"); Equal("Assetto Corsa (AC)", visible[0].DisplayName, "friendly game label includes stable code");
        var included = SimHubGameCatalog.Filter(games, new[] { "fh6" }); Equal(2, included.Count, "configured hidden game retained"); Equal("FH6", included.Single(x => x.Code == "FH6").Code, "stable game code retained");
    }
    private static void CredentialTests()
    {
        var p = new DpapiCredentialProtector(); string encrypted = p.Protect("not-a-real-key");
        True(encrypted != "not-a-real-key", "credential encrypted"); Equal("not-a-real-key", p.Unprotect(encrypted), "DPAPI round trip");
    }
    private static void EmbeddedAssetTests()
    {
        using (var stream = typeof(DeviceSettings).Assembly.GetManifestResourceStream("SimHub.Govee.Assets.SimHubGoveeIcon.png"))
        {
            True(stream != null, "plugin icon embedded");
            True(stream != null && stream.Length > 1000, "plugin icon is nonempty");
        }
    }
    private static void LanValidationTests()
    {
        var lan = new GoveeLanClient();
        Throws<ArgumentException>(() => lan.SendPower("not-an-ip", true), "invalid IP rejected");
        Throws<ArgumentOutOfRangeException>(() => lan.SendBrightness("192.0.2.1", 101), "brightness range enforced");
        Throws<ArgumentOutOfRangeException>(() => lan.SendColor("192.0.2.1", -1, 0, 0), "color range enforced");
    }
    private static void CompatibilityReportTests()
    {
        var device = new DeviceSettings { DeviceId = "secret-device", Name = "Private Room", IpAddress = "192.0.2.93", Sku = "H6046", Capabilities = new List<string> { "devices.capabilities.segment_color_setting/segmentedColorRgb" } }; SegmentTopologyCatalog.ApplyKnownMapping(device);
        string report = CompatibilityReportBuilder.Build(device, new DeviceState { PowerOn = true, Brightness = 50, Rgb = 1 }, "right bar changed");
        True(report.Contains("Model/SKU: H6046"), "report contains model"); True(report.Contains("right bar changed"), "report contains tester observation");
        True(!report.Contains("secret-device") && !report.Contains("Private Room") && !report.Contains("192.0.2.93"), "report excludes device identifiers");
    }
    private static async Task CloudParsingTests()
    {
        var handler = new FakeHttpHandler(); using (var http = new HttpClient(handler)) using (var cloud = new GoveeCloudClient(http))
        {
            handler.Response = "{\"code\":200,\"data\":[{\"sku\":\"H6046\",\"device\":\"AA:BB\",\"deviceName\":\"Bars\",\"capabilities\":[{\"type\":\"devices.capabilities.on_off\",\"instance\":\"powerSwitch\"},{\"type\":\"devices.capabilities.segment_color_setting\",\"instance\":\"segmentedColorRgb\",\"parameters\":{\"fields\":[{\"fieldName\":\"segment\",\"elementRange\":{\"min\":0,\"max\":14}}]}}]}]}";
            var devices = await cloud.GetDevicesAsync("test", CancellationToken.None); Equal(1, devices.Count, "cloud device count"); Equal("Bars", devices[0].Name, "cloud name"); Equal("AA:BB", devices[0].DeviceId, "cloud id"); Equal(2, devices[0].Capabilities.Count, "cloud capabilities"); Equal(15, devices[0].SegmentTopology.AdvertisedSegmentIndices.Count, "advertised segment range parsed");
            handler.Response = "{\"code\":200,\"payload\":{\"capabilities\":[{\"instance\":\"online\",\"state\":{\"value\":true}},{\"instance\":\"powerSwitch\",\"state\":{\"value\":1}},{\"instance\":\"brightness\",\"state\":{\"value\":72}},{\"instance\":\"colorRgb\",\"state\":{\"value\":16711680}}]}}";
            var state = await cloud.GetStateAsync("test", devices[0], CancellationToken.None); True(state.Online, "online parsed"); Equal(true, state.PowerOn, "power parsed"); Equal(72, state.Brightness, "brightness parsed"); Equal(16711680, state.Rgb, "RGB parsed");
            handler.Response = "{\"code\":200,\"message\":\"ok\"}"; await cloud.SetPowerAsync("test", devices[0], false, CancellationToken.None);
            True(handler.LastBody.Contains("powerSwitch") && handler.LastBody.Contains("\"value\":0"), "power request shape"); True(handler.SawKey, "API key header");
            await cloud.SetSegmentColorAsync("test", devices[0], new[] { 0, 1, 2 }, 255, 0, 8, CancellationToken.None); True(handler.LastBody.Contains("segmentedColorRgb") && handler.LastBody.Contains("\"segment\":[0,1,2]") && handler.LastBody.Contains("\"rgb\":16711688"), "segmented color request shape");
            await cloud.SetSegmentBrightnessAsync("test", devices[0], new[] { 0, 1 }, 47, CancellationToken.None); True(handler.LastBody.Contains("segmentedBrightness") && handler.LastBody.Contains("\"brightness\":47"), "segmented brightness request shape");
        }
    }
    private static async Task CloudErrorTests()
    {
        var handler = new FakeHttpHandler { Status = HttpStatusCode.Unauthorized, Response = "{\"code\":401,\"message\":\"secret server detail\"}" };
        using (var cloud = new GoveeCloudClient(new HttpClient(handler)))
        {
            try { await cloud.GetDevicesAsync("bad", CancellationToken.None); throw new Exception("expected unauthorized exception"); }
            catch (GoveeCloudException ex) { True(ex.Message.Contains("rejected"), "unauthorized is actionable"); Equal(HttpStatusCode.Unauthorized, ex.StatusCode, "HTTP status retained"); }
        }
        handler = new FakeHttpHandler { Status = (HttpStatusCode)429, Response = "{\"code\":429}" };
        using (var cloud = new GoveeCloudClient(new HttpClient(handler)))
        {
            try { await cloud.GetDevicesAsync("key", CancellationToken.None); throw new Exception("expected rate exception"); }
            catch (GoveeCloudException ex) { True(ex.Message.Contains("rate limit"), "rate limit is actionable"); }
        }
    }
    private static async Task ControllerTests()
    {
        var cloud = new FakeCloud(); var lan = new FakeLan(); var c = new GoveeController(lan, cloud, () => "key");
        var d = Device(); var result = await c.SetPowerAsync(d, true, false, true, CancellationToken.None); True(result.Success, "local succeeds"); Equal(1, lan.PowerCalls, "local called"); Equal(0, cloud.PowerCalls, "cloud unused");
        Equal(true, d.LastKnownPower, "successful power command updates tracked state");
        lan.Throw = true; result = await c.SetPowerAsync(d, false, false, true, CancellationToken.None); True(result.Success && result.UsedCloudFallback, "fallback succeeds"); Equal(1, cloud.PowerCalls, "cloud fallback called");
        d.Transport = TransportMode.LocalOnly; result = await c.SetPowerAsync(d, false, false, false, CancellationToken.None); True(!result.Success, "local-only failure returned");
        var settings = new PluginSettings { HideLogicalDevices = true, Devices = new List<DeviceSettings> { new DeviceSettings { DeviceId = "id", Selected = true, IpAddress = "1.2.3.4", Transport = TransportMode.Hybrid } } };
        cloud.Devices = new List<DeviceSettings> { new DeviceSettings { DeviceId = "id", Sku = "H6046" }, new DeviceSettings { DeviceId = "logical", Sku = "DreamViewScenic" } };
        var found = await c.DiscoverAsync(settings, CancellationToken.None); Equal(1, found.Count, "logical hidden"); True(found[0].Selected, "selection merged"); Equal("1.2.3.4", found[0].IpAddress, "IP merged"); True(found[0].SegmentTopology.HasUsableMapping, "known H6046 topology applied during discovery");
        found[0].Transport = TransportMode.LocalOnly; result = await c.SetSegmentColorAsync(found[0], new[] { 0 }, 1, 2, 3, CancellationToken.None); True(!result.Success && result.Message.Contains("cloud"), "segmented command rejects Local Only mode");
        lan.Throw = false; found[0].Transport = TransportMode.Hybrid; settings.StartupPolicy = StartupPolicy.LeaveUnchanged; int before = lan.PowerCalls;
        await c.ApplyStartupAsync(settings, CancellationToken.None); Equal(before, lan.PowerCalls, "leave unchanged sends no startup command");
        settings.StartupPolicy = StartupPolicy.ConfiguredState; settings.StartupPowerOn = true; await c.ApplyStartupAsync(settings, CancellationToken.None); Equal(before + 1, lan.PowerCalls, "configured startup command");
        cloud.State = new DeviceState { Online = true, PowerOn = false, Brightness = 30, Rgb = 0x112233 }; lan.Commands.Clear();
        await c.CaptureInitialStatesAsync(found, CancellationToken.None); settings.ExitPolicy = ExitPolicy.RestorePrevious; await c.ApplyExitAsync(settings, CancellationToken.None);
        Equal("brightness,color,power:False", string.Join(",", lan.Commands), "restore applies power last");
        Equal(false, found[0].LastKnownPower, "cloud state and restore update tracked power");
    }
    private static async Task DispatcherTests()
    {
        var cloud = new FakeCloud(); var lan = new FakeLan(); var controller = new GoveeController(lan, cloud, () => "key"); var dispatcher = new LightStateDispatcher(controller); var d = Device();
        var result = await dispatcher.ApplyAsync(new[] { d }, new DesiredLightState { Brightness = 20, Rgb = 0x010203, PowerOn = true }, true, CancellationToken.None);
        True(result.Success, "desired state applied"); Equal("brightness,color,power:True", string.Join(",", lan.Commands), "desired state ordering");
        var known = dispatcher.GetLastKnown(d); Equal(20, known.Brightness, "last commanded brightness"); Equal(0x010203, known.Rgb, "last commanded color"); Equal(true, known.PowerOn, "last commanded power");
        SegmentTopologyCatalog.ApplyKnownMapping(d); d.SegmentTopology.SupportsSegmentedBrightness = true; cloud.SegmentCalls.Clear();
        var segmented = new DesiredLightState { Rgb = 0xFF0000, SegmentColors = new List<SegmentColorAssignment> { new SegmentColorAssignment { HexColor = "#0000FF", Brightness = 35, SegmentIndices = new List<int> { 0, 1 } }, new SegmentColorAssignment { HexColor = "#0000FF", Brightness = 35, SegmentIndices = new List<int> { 2 } }, new SegmentColorAssignment { HexColor = "#00FF00", SegmentIndices = new List<int> { 5 } } } };
        result = await dispatcher.ApplyAsync(new[] { d }, segmented, true, CancellationToken.None); True(result.Success, "segmented state applied"); Equal(2, cloud.SegmentCalls.Count, "matching segment colors grouped into two cloud commands"); True(cloud.SegmentCalls[0].Contains("0,1,2"), "same-color segments combined");
        Equal(1, cloud.SegmentBrightnessCalls.Count, "matching segment brightness grouped into one cloud command"); known = dispatcher.GetLastKnown(d); Equal(3, known.SegmentColors.Count, "last commanded segmented pattern tracked");
    }
    private static async Task GameSessionTests()
    {
        var cloud = new FakeCloud { State = new DeviceState { PowerOn = false, Brightness = 10, Rgb = 0x101010 } }; var lan = new FakeLan(); var controller = new GoveeController(lan, cloud, () => "key"); var dispatcher = new LightStateDispatcher(controller); var coordinator = new GameSessionCoordinator(controller, dispatcher);
        var d = Device(); d.Selected = true; var settings = new PluginSettings { Devices = new List<DeviceSettings> { d }, DefaultGameProfile = new GameProfile { Id = "default", Behavior = ProfileBehavior.PowerOn } };
        await coordinator.HandleAsync(new GameTransition(GameTransitionType.Started, null, "Unknown"), settings, CancellationToken.None); Equal("power:True", string.Join(",", lan.Commands), "default game profile applied");
        lan.Commands.Clear(); await coordinator.HandleAsync(new GameTransition(GameTransitionType.Stopped, "Unknown", null), settings, CancellationToken.None); Equal("brightness,color,power:False", string.Join(",", lan.Commands), "pre-game cloud state restored");
        d.Transport = TransportMode.LocalOnly; d.DeviceId = ""; d.IpAddress = "192.0.2.2"; settings.DefaultGameProfile.Behavior = ProfileBehavior.PowerOn; lan.Commands.Clear();
        await coordinator.HandleAsync(new GameTransition(GameTransitionType.Started, null, "Unknown"), settings, CancellationToken.None); await coordinator.HandleAsync(new GameTransition(GameTransitionType.Stopped, "Unknown", null), settings, CancellationToken.None);
        Equal("power:True,power:True", string.Join(",", lan.Commands), "local-only falls back to global configured default");
        settings.StartupPolicy = StartupPolicy.LeaveUnchanged; settings.StartupPowerOn = false; settings.DefaultGameProfile.Behavior = ProfileBehavior.LeaveUnchanged;
        var freshLan = new FakeLan(); var freshController = new GoveeController(freshLan, cloud, () => "key"); var freshCoordinator = new GameSessionCoordinator(freshController, new LightStateDispatcher(freshController));
        await freshCoordinator.HandleAsync(new GameTransition(GameTransitionType.Started, null, "Unknown"), settings, CancellationToken.None); await freshCoordinator.HandleAsync(new GameTransition(GameTransitionType.Stopped, "Unknown", null), settings, CancellationToken.None);
        Equal("power:False", string.Join(",", freshLan.Commands), "local-only without known state uses configured global default even when startup leaves unchanged");
    }
    private static DeviceSettings Device() => new DeviceSettings { DeviceId = "id", Sku = "H6046", IpAddress = "192.0.2.1", Transport = TransportMode.Hybrid };
    private static void True(bool value, string name) { _assertions++; if (!value) throw new Exception(name); }
    private static void Equal(object expected, object actual, string name) { _assertions++; if (!object.Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual); }
    private static void Throws<T>(Action action, string name) where T : Exception { _assertions++; try { action(); } catch (T) { return; } throw new Exception(name); }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        public string Response, LastBody; public bool SawKey; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SawKey = request.Headers.Contains("Govee-API-Key"); LastBody = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
        }
    }
    private sealed class FakeLan : ILanClient
    {
        public bool Throw; public int PowerCalls; public List<string> Commands = new List<string>();
        public void SendPower(string ip, bool on) { PowerCalls++; Commands.Add("power:" + on); if (Throw) throw new Exception("UDP failed"); }
        public void SendBrightness(string ip, int value) { Commands.Add("brightness"); if (Throw) throw new Exception("UDP failed"); }
        public void SendColor(string ip, int r, int g, int b) { Commands.Add("color"); if (Throw) throw new Exception("UDP failed"); }
    }
    private sealed class FakeCloud : ICloudClient
    {
        public int PowerCalls; public IList<DeviceSettings> Devices = new List<DeviceSettings>(); public DeviceState State = new DeviceState { Online = true, PowerOn = true }; public List<string> SegmentCalls = new List<string>(); public List<string> SegmentBrightnessCalls = new List<string>();
        public Task<IList<DeviceSettings>> GetDevicesAsync(string key, CancellationToken token) => Task.FromResult(Devices);
        public Task<DeviceState> GetStateAsync(string key, DeviceSettings d, CancellationToken token) => Task.FromResult(State);
        public Task SetPowerAsync(string key, DeviceSettings d, bool on, CancellationToken token) { PowerCalls++; return Task.CompletedTask; }
        public Task SetBrightnessAsync(string key, DeviceSettings d, int value, CancellationToken token) => Task.CompletedTask;
        public Task SetColorAsync(string key, DeviceSettings d, int r, int g, int b, CancellationToken token) => Task.CompletedTask;
        public Task SetSegmentColorAsync(string key, DeviceSettings d, IList<int> segments, int r, int g, int b, CancellationToken token) { SegmentCalls.Add(string.Join(",", segments) + ":" + r + "," + g + "," + b); return Task.CompletedTask; }
        public Task SetSegmentBrightnessAsync(string key, DeviceSettings d, IList<int> segments, int brightness, CancellationToken token) { SegmentBrightnessCalls.Add(string.Join(",", segments) + ":" + brightness); return Task.CompletedTask; }
    }
}
