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
        public int SchemaVersion { get; set; } = 4;
        public string EncryptedApiKey { get; set; }
        public List<DeviceSettings> Devices { get; set; } = new List<DeviceSettings>();
        public StartupPolicy StartupPolicy { get; set; } = StartupPolicy.ConfiguredState;
        public ExitPolicy ExitPolicy { get; set; } = ExitPolicy.Off;
        public bool StartupPowerOn { get; set; } = true;
        public bool ExitPowerOn { get; set; }
        public bool CloudFallback { get; set; } = true;
        public bool HideLogicalDevices { get; set; } = true;
        public bool RefreshStateBeforeAction { get; set; }
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
        public string TargetSummary => TargetDeviceIds == null || TargetDeviceIds.Count == 0 ? "All selected lights" : TargetDeviceIds.Count + " light(s)";
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
        public string Source { get; set; }
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
        public DeviceState Clone() => (DeviceState)MemberwiseClone();
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
                device.Sku = (device.Sku ?? string.Empty).Trim();
                device.DeviceId = (device.DeviceId ?? string.Empty).Trim();
                device.IpAddress = (device.IpAddress ?? string.Empty).Trim();
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
            }
            var presetNames = settings.Presets.ToDictionary(p => p.Id, p => p.Name);
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
            settings.SchemaVersion = 4;
        }

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
