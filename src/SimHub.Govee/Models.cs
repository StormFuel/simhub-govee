using System;
using System.Collections.Generic;
using System.Linq;

namespace SimHub.Govee
{
    public enum TransportMode { Hybrid, Cloud, LocalOnly }
    public enum StartupPolicy { ConfiguredState, LeaveUnchanged }
    public enum ExitPolicy { Off, LeaveUnchanged, ConfiguredState, RestorePrevious }

    public sealed class PluginSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public string EncryptedApiKey { get; set; }
        public List<DeviceSettings> Devices { get; set; } = new List<DeviceSettings>();
        public StartupPolicy StartupPolicy { get; set; } = StartupPolicy.ConfiguredState;
        public ExitPolicy ExitPolicy { get; set; } = ExitPolicy.Off;
        public bool StartupPowerOn { get; set; } = true;
        public bool ExitPowerOn { get; set; }
        public bool CloudFallback { get; set; } = true;
        public bool HideLogicalDevices { get; set; } = true;
    }

    public sealed class DeviceSettings
    {
        public string DeviceId { get; set; }
        public string Sku { get; set; }
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public bool Selected { get; set; }
        public TransportMode Transport { get; set; } = TransportMode.Hybrid;
        public List<string> Capabilities { get; set; } = new List<string>();
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Sku : Name + " (" + Sku + ")";
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
            if (settings.Devices == null) settings.Devices = new List<DeviceSettings>();
            foreach (var device in settings.Devices)
            {
                if (device.Capabilities == null) device.Capabilities = new List<string>();
                device.Sku = (device.Sku ?? string.Empty).Trim();
                device.DeviceId = (device.DeviceId ?? string.Empty).Trim();
                device.IpAddress = (device.IpAddress ?? string.Empty).Trim();
            }
        }

        public static bool IsValidRgbHex(string value)
        {
            int parsed;
            return !string.IsNullOrWhiteSpace(value) && value.Length == 7 && value[0] == '#' && int.TryParse(value.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out parsed);
        }
    }
}
