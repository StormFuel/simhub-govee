using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SimHub.Govee
{
    public static class CompatibilityReportBuilder
    {
        public static string Build(DeviceSettings device, DeviceState state, string observations, IEnumerable<int> observedEffectiveIndices = null, bool activeTestRun = false)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var topology = device.SegmentTopology ?? new SegmentTopology();
            var text = new StringBuilder();
            text.AppendLine("Govee Controller Plugin for SimHub - Device Compatibility Report");
            text.AppendLine("Report format: 1");
            text.AppendLine("Plugin version: " + (Assembly.GetExecutingAssembly().GetName().Version == null ? "unknown" : Assembly.GetExecutingAssembly().GetName().Version.ToString()));
            text.AppendLine("Generated UTC: " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            text.AppendLine("Model/SKU: " + Safe(device.Sku));
            text.AppendLine("Cloud state request: " + (state == null ? "failed or unavailable" : "succeeded"));
            text.AppendLine("Active segment test run: " + YesNo(activeTestRun));
            text.AppendLine("Tester-confirmed effective indices: " + Indices(observedEffectiveIndices));
            text.AppendLine("Power state readable: " + YesNo(state != null && state.PowerOn.HasValue));
            text.AppendLine("Brightness readable: " + YesNo(state != null && state.Brightness.HasValue));
            text.AppendLine("Whole-device RGB readable: " + YesNo(state != null && state.Rgb.HasValue));
            text.AppendLine("Segmented color advertised: " + YesNo(topology.SupportsSegmentedColor));
            text.AppendLine("Segmented brightness advertised: " + YesNo(topology.SupportsSegmentedBrightness));
            text.AppendLine("Advertised segment indices: " + Indices(topology.AdvertisedSegmentIndices));
            text.AppendLine("Verified effective indices: " + Indices(topology.VerifiedSegmentIndices));
            text.AppendLine("Mapping source: " + Safe(topology.Source));
            text.AppendLine("Zones:");
            foreach (var zone in topology.Zones ?? new List<SegmentZone>())
                text.AppendLine("- " + Safe(zone.Name) + ": " + Indices(zone.SegmentIndices));
            text.AppendLine("Advertised capabilities:");
            foreach (string capability in (device.Capabilities ?? new List<string>()).Where(IsSafeCapability).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                text.AppendLine("- " + Safe(capability));
            text.AppendLine("Tester observations:");
            text.AppendLine(SafeMultiline(observations));
            text.AppendLine();
            text.AppendLine("Privacy: this report intentionally excludes API keys, device/account identifiers, device names, IP/MAC addresses, and raw API responses.");
            return text.ToString();
        }

        private static string YesNo(bool value) => value ? "yes" : "no";
        private static string Indices(IEnumerable<int> values)
        {
            var list = (values ?? Enumerable.Empty<int>()).Distinct().OrderBy(x => x).ToList();
            return list.Count == 0 ? "none/unknown" : string.Join(",", list);
        }
        private static bool IsSafeCapability(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 160;
        private static string Safe(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Replace("\r", " ").Replace("\n", " ").Trim();
        private static string SafeMultiline(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "(none supplied)";
            return string.Join(Environment.NewLine, value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Take(40));
        }
    }
}
