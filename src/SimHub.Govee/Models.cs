using System;
using System.Collections.Generic;
using System.Linq;

namespace SimHub.Govee
{
    public enum TransportMode { Hybrid, Cloud, LocalOnly }
    public enum StartupPolicy { ConfiguredState, LeaveUnchanged }
    public enum ExitPolicy { Off, LeaveUnchanged, ConfiguredState, RestorePrevious }
    public enum ProfileBehavior { LeaveUnchanged, PowerOn, PowerOff, ApplyPreset }
    public enum ManualActionType { PowerOn, PowerOff, SetColor, TogglePower }

    public sealed class PluginSettings
    {
        public int SchemaVersion { get; set; } = 5;
        public string EncryptedApiKey { get; set; }
        public List<DeviceSettings> Devices { get; set; } = new List<DeviceSettings>();
        public StartupPolicy StartupPolicy { get; set; } = StartupPolicy.ConfiguredState;
        public ExitPolicy ExitPolicy { get; set; } = ExitPolicy.Off;
        public bool StartupPowerOn { get; set; } = true;
        public string StartupPresetId { get; set; }
        public bool ExitPowerOn { get; set; }
        public bool CloudFallback { get; set; } = true;
        public bool HideLogicalDevices { get; set; } = true;
        public bool RefreshStateBeforeAction { get; set; }
        public bool ShowLivePreviewWarning { get; set; } = true;
        public int SegmentTestDurationSeconds { get; set; } = 10;
        public List<LightPreset> Presets { get; set; } = new List<LightPreset>();
        public GameProfile DefaultGameProfile { get; set; } = GameProfile.CreateDefault();
        public List<GameProfile> GameProfiles { get; set; } = new List<GameProfile>();
        public List<ManualActionDefinition> ManualActions { get; set; } = new List<ManualActionDefinition>();
    }

    public sealed class LightPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New preset";
        public string HexColor { get; set; } = "#FFFFFF";
        public int? Brightness { get; set; }
        public bool TurnOn { get; set; } = true;
        public List<string> TargetDeviceIds { get; set; } = new List<string>();
        public List<DevicePresetAppearance> DeviceAppearances { get; set; } = new List<DevicePresetAppearance>();
        public string TargetSummary => TargetDeviceIds == null || TargetDeviceIds.Count == 0 ? "All selected lights" : TargetDeviceIds.Count + " light(s)";
        public string ColorSummary => EffectiveColorSummary();
        public string BrightnessSummary => EffectiveBrightnessSummary();
        public string AppearanceSummary => DeviceAppearances != null && DeviceAppearances.Any(x => x != null && (x.UseSegmentedColor || SettingsValidator.IsValidRgbHex(x.HexColor) || x.Brightness.HasValue)) ? "Advanced" : "Simple";

        private string EffectiveColorSummary()
        {
            string uniform = SettingsValidator.NormalizeHex(HexColor);
            var appearances = (DeviceAppearances ?? new List<DevicePresetAppearance>()).Where(x => x != null).ToList(); var colors = new List<string>();
            if ((TargetDeviceIds == null || TargetDeviceIds.Count == 0) && appearances.Count == 0) colors.Add(uniform);
            else foreach (string target in TargetDeviceIds)
            {
                var appearance = appearances.FirstOrDefault(x => string.Equals(x.TargetId, target, StringComparison.OrdinalIgnoreCase)); AddAppearanceColors(colors, appearance, uniform);
            }
            if (TargetDeviceIds == null || TargetDeviceIds.Count == 0) foreach (var appearance in appearances) AddAppearanceColors(colors, appearance, uniform);
            var distinct = colors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); return distinct.Count == 1 ? distinct[0] : "Custom";
        }

        private string EffectiveBrightnessSummary()
        {
            var appearances = (DeviceAppearances ?? new List<DevicePresetAppearance>()).Where(x => x != null).ToList(); var values = new List<int?>();
            if ((TargetDeviceIds == null || TargetDeviceIds.Count == 0) && appearances.Count == 0) values.Add(Brightness);
            else foreach (string target in TargetDeviceIds)
            {
                var appearance = appearances.FirstOrDefault(x => string.Equals(x.TargetId, target, StringComparison.OrdinalIgnoreCase)); AddAppearanceBrightness(values, appearance, Brightness);
            }
            if (TargetDeviceIds == null || TargetDeviceIds.Count == 0) foreach (var appearance in appearances) AddAppearanceBrightness(values, appearance, Brightness);
            var distinct = values.Distinct().ToList(); return distinct.Count == 1 ? distinct[0].HasValue ? distinct[0].Value.ToString() : "—" : "Custom";
        }
        private static void AddAppearanceColors(ICollection<string> colors, DevicePresetAppearance appearance, string fallback)
        {
            if (appearance == null) { colors.Add(fallback); return; }
            if (appearance.UseSegmentedColor && appearance.SegmentColors != null && appearance.SegmentColors.Count > 0) foreach (var assignment in appearance.SegmentColors.Where(x => x != null)) colors.Add(SettingsValidator.NormalizeHex(assignment.HexColor));
            else colors.Add(SettingsValidator.IsValidRgbHex(appearance.HexColor) ? SettingsValidator.NormalizeHex(appearance.HexColor) : fallback);
        }
        private static void AddAppearanceBrightness(ICollection<int?> values, DevicePresetAppearance appearance, int? fallback)
        {
            if (appearance == null) { values.Add(fallback); return; }
            if (appearance.UseSegmentedColor && appearance.SegmentColors != null && appearance.SegmentColors.Any(x => x != null && x.Brightness.HasValue)) foreach (var assignment in appearance.SegmentColors.Where(x => x != null)) values.Add(assignment.Brightness ?? appearance.Brightness ?? fallback);
            else values.Add(appearance.Brightness ?? fallback);
        }
    }

    public sealed class DevicePresetAppearance
    {
        public string TargetId { get; set; }
        public string HexColor { get; set; }
        public int? Brightness { get; set; }
        public bool UseSegmentedColor { get; set; }
        public List<SegmentColorAssignment> SegmentColors { get; set; } = new List<SegmentColorAssignment>();
    }

    public sealed class SegmentColorAssignment
    {
        public string Name { get; set; }
        public List<int> SegmentIndices { get; set; } = new List<int>();
        public string HexColor { get; set; } = "#FFFFFF";
        public int? Brightness { get; set; }
        public string SegmentSummary => SegmentIndices == null ? string.Empty : string.Join(", ", SegmentIndices);
    }

    public sealed class SegmentZone
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<int> SegmentIndices { get; set; } = new List<int>();
        public string SegmentSummary => SegmentIndices == null ? string.Empty : string.Join(", ", SegmentIndices);
    }

    public sealed class SegmentTopology
    {
        public List<int> AdvertisedSegmentIndices { get; set; } = new List<int>();
        public List<int> VerifiedSegmentIndices { get; set; } = new List<int>();
        public List<SegmentZone> Zones { get; set; } = new List<SegmentZone>();
        public string Source { get; set; }
        public bool SupportsSegmentedColor { get; set; }
        public bool SupportsSegmentedBrightness { get; set; }
        public bool HasUsableMapping => VerifiedSegmentIndices != null && VerifiedSegmentIndices.Count > 0 && Zones != null && Zones.Count > 0;
    }

    public static class SegmentTopologyCatalog
    {
        public static void ApplyKnownMapping(DeviceSettings device)
        {
            if (device == null || !string.Equals(device.Sku, "H6046", StringComparison.OrdinalIgnoreCase)) return;
            if (device.SegmentTopology == null) device.SegmentTopology = new SegmentTopology();
            device.SegmentTopology.SupportsSegmentedColor = true;
            device.SegmentTopology.SupportsSegmentedBrightness = true;
            if (device.SegmentTopology.HasUsableMapping) return;
            device.SegmentTopology.VerifiedSegmentIndices = Enumerable.Range(0, 10).ToList();
            device.SegmentTopology.Zones = new List<SegmentZone>
            {
                new SegmentZone { Id = "left-bar", Name = "Left bar", SegmentIndices = Enumerable.Range(0, 5).ToList() },
                new SegmentZone { Id = "right-bar", Name = "Right bar", SegmentIndices = Enumerable.Range(5, 5).ToList() }
            };
            device.SegmentTopology.Source = "Built-in H6046 mapping (hardware verified)";
        }
    }

    public static class PresetNameSuggester
    {
        private static readonly IDictionary<string, string> KnownColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["#FFFFFF"] = "White", ["#000000"] = "Black", ["#FF0000"] = "Red", ["#FF8000"] = "Orange",
            ["#FFFF00"] = "Yellow", ["#00FF00"] = "Green", ["#00BFFF"] = "Cyan", ["#0000FF"] = "Blue",
            ["#8000FF"] = "Violet", ["#FF00FF"] = "Magenta"
        };

        public static string ForSegmentedAppearance(SegmentTopology topology, IEnumerable<SegmentColorAssignment> assignments)
        {
            if (topology == null) return "Segmented preset";
            var colors = new Dictionary<int, string>();
            foreach (var assignment in assignments ?? Enumerable.Empty<SegmentColorAssignment>())
                foreach (int index in assignment.SegmentIndices ?? new List<int>()) colors[index] = SettingsValidator.NormalizeHex(assignment.HexColor);
            var parts = new List<string>();
            foreach (var zone in topology.Zones ?? new List<SegmentZone>())
            {
                var zoneColors = (zone.SegmentIndices ?? new List<int>()).Where(colors.ContainsKey).Select(x => colors[x]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (zoneColors.Count == 0) continue;
                string color = zoneColors.Count == 1 ? FriendlyColor(zoneColors[0]) : "Mixed";
                parts.Add(ShortZoneName(zone.Name) + " " + color);
            }
            if (parts.Count > 0) return string.Join(" - ", parts);
            var all = colors.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return all.Count == 1 ? "Segmented " + FriendlyColor(all[0]) : "Segmented Mixed";
        }

        public static string FriendlyColor(string hex)
        {
            string normalized = SettingsValidator.NormalizeHex(hex), name;
            return KnownColors.TryGetValue(normalized, out name) ? name : normalized;
        }

        private static string ShortZoneName(string value)
        {
            string name = string.IsNullOrWhiteSpace(value) ? "Zone" : value.Trim();
            foreach (string suffix in new[] { " bar", " zone", " section" }) if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - suffix.Length);
            return name;
        }
    }

    public static class SettingsDrafts
    {
        public static LightPreset Preset(LightPreset value)
        {
            value = value ?? new LightPreset { Name = string.Empty };
            return new LightPreset { Id = value.Id, Name = value.Name, HexColor = value.HexColor, Brightness = value.Brightness, TurnOn = value.TurnOn, TargetDeviceIds = (value.TargetDeviceIds ?? new List<string>()).ToList(), DeviceAppearances = (value.DeviceAppearances ?? new List<DevicePresetAppearance>()).Select(Appearance).ToList() };
        }
        public static DevicePresetAppearance Appearance(DevicePresetAppearance value) => new DevicePresetAppearance { TargetId = value.TargetId, HexColor = value.HexColor, Brightness = value.Brightness, UseSegmentedColor = value.UseSegmentedColor, SegmentColors = DesiredLightState.CloneAssignments(value.SegmentColors) };
        public static GameProfile Profile(GameProfile value)
        {
            value = value ?? new GameProfile();
            return new GameProfile { Id = value.Id, GameCode = value.GameCode, DisplayName = value.DisplayName, Enabled = value.Enabled, Behavior = value.Behavior, PresetId = value.PresetId, PresetName = value.PresetName, TargetDeviceIds = (value.TargetDeviceIds ?? new List<string>()).ToList() };
        }
        public static ManualActionDefinition Action(ManualActionDefinition value)
        {
            value = value ?? new ManualActionDefinition();
            return new ManualActionDefinition { Id = value.Id, ActionKey = value.ActionKey, DisplayName = value.DisplayName, Type = value.Type, PresetId = value.PresetId, PresetName = value.PresetName, TargetDeviceIds = (value.TargetDeviceIds ?? new List<string>()).ToList(), IsManaged = value.IsManaged };
        }
    }

    public sealed class GameProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string GameCode { get; set; }
        public string DisplayName { get; set; } = "New game profile";
        public bool Enabled { get; set; } = true;
        public ProfileBehavior Behavior { get; set; } = ProfileBehavior.LeaveUnchanged;
        public string PresetId { get; set; }
        public string PresetName { get; set; }
        public List<string> TargetDeviceIds { get; set; } = new List<string>();
        public string TargetSummary => TargetDeviceIds == null || TargetDeviceIds.Count == 0 ? "All selected lights" : TargetDeviceIds.Count + " light(s)";
        public static GameProfile CreateDefault() => new GameProfile { Id = "default", DisplayName = "Default Game Profile", Enabled = true, Behavior = ProfileBehavior.LeaveUnchanged };
    }

    public sealed class ManualActionDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ActionKey { get; set; }
        public string DisplayName { get; set; }
        public ManualActionType Type { get; set; }
        public string PresetId { get; set; }
        public string PresetName { get; set; }
        public List<string> TargetDeviceIds { get; set; } = new List<string>();
        public bool IsManaged { get; set; }
        public string RegisteredName => "SimHubGovee." + ActionKey;
        public string TargetSummary => TargetDeviceIds == null || TargetDeviceIds.Count == 0 ? "All selected lights" : TargetDeviceIds.Count + " light(s)";
    }

    public sealed class DesiredLightState
    {
        public bool? PowerOn { get; set; }
        public int? Brightness { get; set; }
        public int? Rgb { get; set; }
        public List<SegmentColorAssignment> SegmentColors { get; set; } = new List<SegmentColorAssignment>();
        public List<DevicePresetAppearance> DeviceAppearances { get; set; } = new List<DevicePresetAppearance>();
        public string Source { get; set; }

        public DesiredLightState ForDevice(DeviceSettings device)
        {
            var result = new DesiredLightState { PowerOn = PowerOn, Brightness = Brightness, Rgb = Rgb, SegmentColors = CloneAssignments(SegmentColors), Source = Source };
            var appearance = (DeviceAppearances ?? new List<DevicePresetAppearance>()).FirstOrDefault(x => x != null && string.Equals(x.TargetId, device.TargetId, StringComparison.OrdinalIgnoreCase));
            if (appearance == null) return result;
            if (appearance.Brightness.HasValue) result.Brightness = appearance.Brightness;
            if (SettingsValidator.IsValidRgbHex(appearance.HexColor)) result.Rgb = SettingsValidator.HexToRgb(appearance.HexColor);
            if (appearance.UseSegmentedColor) result.SegmentColors = CloneAssignments(appearance.SegmentColors);
            return result;
        }

        internal static List<SegmentColorAssignment> CloneAssignments(IEnumerable<SegmentColorAssignment> values)
        {
            return (values ?? Enumerable.Empty<SegmentColorAssignment>()).Where(x => x != null).Select(x => new SegmentColorAssignment
            {
                Name = x.Name,
                HexColor = x.HexColor,
                Brightness = x.Brightness,
                SegmentIndices = (x.SegmentIndices ?? new List<int>()).ToList()
            }).ToList();
        }
    }

    public sealed class DeviceSettings
    {
        public string DeviceId { get; set; }
        public string Sku { get; set; }
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public bool Selected { get; set; }
        public bool? LastKnownPower { get; set; }
        public TransportMode Transport { get; set; } = TransportMode.Hybrid;
        public List<string> Capabilities { get; set; } = new List<string>();
        public SegmentTopology SegmentTopology { get; set; } = new SegmentTopology();
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Sku : Name + " (" + Sku + ")";
        public string TargetId => !string.IsNullOrWhiteSpace(DeviceId) ? DeviceId : "ip:" + (IpAddress ?? string.Empty);
        public bool IsLogical => string.Equals(Sku, "DreamViewScenic", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class DeviceState
    {
        public bool Online { get; set; }
        public bool? PowerOn { get; set; }
        public int? Brightness { get; set; }
        public int? Rgb { get; set; }
        public int? ColorTemperatureKelvin { get; set; }
        public List<SegmentColorAssignment> SegmentColors { get; set; } = new List<SegmentColorAssignment>();
        public DeviceState Clone()
        {
            var clone = (DeviceState)MemberwiseClone();
            clone.SegmentColors = DesiredLightState.CloneAssignments(SegmentColors);
            return clone;
        }
    }

    public sealed class OperationResult
    {
        public bool Success { get; private set; }
        public bool UsedCloudFallback { get; private set; }
        public string Message { get; private set; }
        public static OperationResult Ok(string message, bool fallback = false) => new OperationResult { Success = true, Message = message, UsedCloudFallback = fallback };
        public static OperationResult Fail(string message) => new OperationResult { Message = message };
    }

    public static class SettingsValidator
    {
        public static void Normalize(PluginSettings settings)
        {
            bool migrateAmbiguousV2PresetProfiles = settings.SchemaVersion < 3;
            if (settings.Devices == null) settings.Devices = new List<DeviceSettings>();
            foreach (var device in settings.Devices)
            {
                if (device.Capabilities == null) device.Capabilities = new List<string>();
                NormalizeTopology(device);
                device.Sku = (device.Sku ?? string.Empty).Trim();
                device.DeviceId = (device.DeviceId ?? string.Empty).Trim();
                device.IpAddress = (device.IpAddress ?? string.Empty).Trim();
                SegmentTopologyCatalog.ApplyKnownMapping(device);
            }
            if (settings.Presets == null) settings.Presets = new List<LightPreset>();
            if (settings.GameProfiles == null) settings.GameProfiles = new List<GameProfile>();
            if (settings.ManualActions == null) settings.ManualActions = new List<ManualActionDefinition>();
            if (settings.DefaultGameProfile == null) settings.DefaultGameProfile = GameProfile.CreateDefault();
            NormalizeProfile(settings.DefaultGameProfile);
            foreach (var p in settings.Presets)
            {
                if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("N");
                p.Name = (p.Name ?? string.Empty).Trim(); p.HexColor = NormalizeHex(p.HexColor);
                if (p.Brightness.HasValue) p.Brightness = Math.Max(0, Math.Min(100, p.Brightness.Value));
                if (p.TargetDeviceIds == null) p.TargetDeviceIds = new List<string>();
                if (p.DeviceAppearances == null) p.DeviceAppearances = new List<DevicePresetAppearance>();
                foreach (var appearance in p.DeviceAppearances.Where(x => x != null))
                {
                    appearance.TargetId = (appearance.TargetId ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(appearance.HexColor)) appearance.HexColor = NormalizeHex(appearance.HexColor);
                    if (appearance.Brightness.HasValue) appearance.Brightness = Math.Max(0, Math.Min(100, appearance.Brightness.Value));
                    appearance.SegmentColors = NormalizeAssignments(appearance.SegmentColors);
                }
                p.DeviceAppearances.RemoveAll(x => x == null || string.IsNullOrWhiteSpace(x.TargetId));
            }
            var presetNames = settings.Presets.ToDictionary(p => p.Id, p => p.Name);
            if (string.IsNullOrWhiteSpace(settings.StartupPresetId) || !presetNames.ContainsKey(settings.StartupPresetId)) settings.StartupPresetId = null;
            if (migrateAmbiguousV2PresetProfiles)
            {
                MigratePresetBehavior(settings.DefaultGameProfile, presetNames);
                foreach (var profile in settings.GameProfiles) MigratePresetBehavior(profile, presetNames);
            }
            foreach (var p in settings.GameProfiles) { NormalizeProfile(p); p.PresetName = PresetName(presetNames, p.PresetId); }
            settings.DefaultGameProfile.PresetName = PresetName(presetNames, settings.DefaultGameProfile.PresetId);
            foreach (var a in settings.ManualActions)
            {
                if (string.IsNullOrWhiteSpace(a.Id)) a.Id = Guid.NewGuid().ToString("N");
                a.ActionKey = NormalizeActionKey(a.ActionKey); a.DisplayName = string.IsNullOrWhiteSpace(a.DisplayName) ? a.ActionKey : a.DisplayName.Trim();
                if (a.TargetDeviceIds == null) a.TargetDeviceIds = new List<string>();
                a.PresetName = PresetName(presetNames, a.PresetId);
            }
            if (string.IsNullOrWhiteSpace(settings.EncryptedApiKey)) settings.RefreshStateBeforeAction = false;
            settings.SegmentTestDurationSeconds = Math.Max(1, Math.Min(120, settings.SegmentTestDurationSeconds));
            settings.SchemaVersion = 5;
        }

        private static void NormalizeTopology(DeviceSettings device)
        {
            if (device.SegmentTopology == null) device.SegmentTopology = new SegmentTopology();
            var topology = device.SegmentTopology;
            topology.SupportsSegmentedColor = topology.SupportsSegmentedColor || (device.Capabilities ?? new List<string>()).Any(x => x != null && x.EndsWith("/segmentedColorRgb", StringComparison.OrdinalIgnoreCase));
            topology.SupportsSegmentedBrightness = topology.SupportsSegmentedBrightness || (device.Capabilities ?? new List<string>()).Any(x => x != null && x.EndsWith("/segmentedBrightness", StringComparison.OrdinalIgnoreCase));
            topology.AdvertisedSegmentIndices = NormalizeIndices(topology.AdvertisedSegmentIndices);
            topology.VerifiedSegmentIndices = NormalizeIndices(topology.VerifiedSegmentIndices);
            if (topology.Zones == null) topology.Zones = new List<SegmentZone>();
            foreach (var zone in topology.Zones.Where(x => x != null))
            {
                if (string.IsNullOrWhiteSpace(zone.Id)) zone.Id = Guid.NewGuid().ToString("N");
                zone.Name = string.IsNullOrWhiteSpace(zone.Name) ? "Segment group" : zone.Name.Trim();
                zone.SegmentIndices = NormalizeIndices(zone.SegmentIndices);
            }
            topology.Zones.RemoveAll(x => x == null || x.SegmentIndices.Count == 0);
        }

        private static List<SegmentColorAssignment> NormalizeAssignments(IEnumerable<SegmentColorAssignment> assignments)
        {
            var result = DesiredLightState.CloneAssignments(assignments);
            foreach (var item in result)
            {
                item.Name = string.IsNullOrWhiteSpace(item.Name) ? "Segment group" : item.Name.Trim();
                item.HexColor = NormalizeHex(item.HexColor);
                if (item.Brightness.HasValue) item.Brightness = Math.Max(0, Math.Min(100, item.Brightness.Value));
                item.SegmentIndices = NormalizeIndices(item.SegmentIndices);
            }
            return result.Where(x => x.SegmentIndices.Count > 0).ToList();
        }

        private static List<int> NormalizeIndices(IEnumerable<int> values) => (values ?? Enumerable.Empty<int>()).Where(x => x >= 0).Distinct().OrderBy(x => x).ToList();

        private static void MigratePresetBehavior(GameProfile profile, IDictionary<string, string> presets)
        {
            if (profile != null && profile.Behavior == ProfileBehavior.PowerOn && !string.IsNullOrWhiteSpace(profile.PresetId) && presets.ContainsKey(profile.PresetId))
                profile.Behavior = ProfileBehavior.ApplyPreset;
        }

        private static void NormalizeProfile(GameProfile p)
        {
            if (string.IsNullOrWhiteSpace(p.Id)) p.Id = Guid.NewGuid().ToString("N");
            p.GameCode = (p.GameCode ?? string.Empty).Trim(); p.DisplayName = (p.DisplayName ?? string.Empty).Trim();
            if (p.TargetDeviceIds == null) p.TargetDeviceIds = new List<string>();
        }

        public static string NormalizeHex(string value) => IsValidRgbHex(value) ? value.ToUpperInvariant() : "#FFFFFF";
        public static int HexToRgb(string value) { if (!IsValidRgbHex(value)) throw new ArgumentException("Use a color in #RRGGBB format."); return Convert.ToInt32(value.Substring(1), 16); }
        public static string NormalizeActionKey(string value)
        {
            string text = new string((value ?? string.Empty).Trim().Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            return text;
        }
        private static string PresetName(IDictionary<string, string> names, string id) { string name; return !string.IsNullOrWhiteSpace(id) && names.TryGetValue(id, out name) ? name : string.Empty; }

        public static bool IsValidRgbHex(string value)
        {
            int parsed;
            return !string.IsNullOrWhiteSpace(value) && value.Length == 7 && value[0] == '#' && int.TryParse(value.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out parsed);
        }
    }
}
