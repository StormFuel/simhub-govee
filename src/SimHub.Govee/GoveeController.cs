using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimHub.Govee
{
    public sealed class GoveeController
    {
        private readonly ILanClient _lan;
        private readonly ICloudClient _cloud;
        private readonly Func<string> _getApiKey;
        private readonly Dictionary<string, DeviceState> _initial = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastCloudCommand = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        public GoveeController(ILanClient lan, ICloudClient cloud, Func<string> getApiKey) { _lan = lan; _cloud = cloud; _getApiKey = getApiKey; }

        public async Task<IList<DeviceSettings>> DiscoverAsync(PluginSettings settings, CancellationToken token)
        {
            var discovered = await _cloud.GetDevicesAsync(RequireKey(), token).ConfigureAwait(false);
            var saved = (settings.Devices ?? new List<DeviceSettings>()).Where(d => !string.IsNullOrWhiteSpace(d.DeviceId)).ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
            var merged = new List<DeviceSettings>();
            foreach (var cloud in discovered)
            {
                DeviceSettings old;
                if (saved.TryGetValue(cloud.DeviceId ?? "", out old)) { cloud.Selected = old.Selected; cloud.IpAddress = old.IpAddress; cloud.Transport = old.Transport; }
                if (!settings.HideLogicalDevices || !cloud.IsLogical) merged.Add(cloud);
            }
            merged.AddRange((settings.Devices ?? new List<DeviceSettings>()).Where(d => string.IsNullOrWhiteSpace(d.DeviceId)));
            settings.Devices = merged;
            return merged;
        }

        public Task<DeviceState> GetStateAsync(DeviceSettings device, CancellationToken token) => _cloud.GetStateAsync(RequireKey(), device, token);
        public async Task CaptureInitialStatesAsync(IEnumerable<DeviceSettings> devices, CancellationToken token)
        {
            foreach (var d in devices.Where(x => x.Selected && !string.IsNullOrWhiteSpace(x.DeviceId))) try { _initial[d.DeviceId] = await GetStateAsync(d, token).ConfigureAwait(false); } catch { }
        }

        public Task<OperationResult> SetPowerAsync(DeviceSettings d, bool on, bool verify, bool fallback, CancellationToken token) => ExecuteAsync(d, fallback, verify, () => _lan.SendPower(d.IpAddress, on), () => _cloud.SetPowerAsync(RequireKey(), d, on, token), s => s.PowerOn == on, "power", token);
        public Task<OperationResult> SetBrightnessAsync(DeviceSettings d, int value, bool fallback, CancellationToken token) => ExecuteAsync(d, fallback, false, () => _lan.SendBrightness(d.IpAddress, value), () => _cloud.SetBrightnessAsync(RequireKey(), d, value, token), null, "brightness", token);
        public Task<OperationResult> SetColorAsync(DeviceSettings d, int r, int g, int b, bool fallback, CancellationToken token) => ExecuteAsync(d, fallback, false, () => _lan.SendColor(d.IpAddress, r, g, b), () => _cloud.SetColorAsync(RequireKey(), d, r, g, b, token), null, "color", token);

        private async Task<OperationResult> ExecuteAsync(DeviceSettings d, bool fallback, bool verify, Action local, Func<Task> cloud, Func<DeviceState, bool> predicate, string label, CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                bool localMode = d.Transport != TransportMode.Cloud;
                if (localMode && !string.IsNullOrWhiteSpace(d.IpAddress))
                {
                    try
                    {
                        local();
                        if (!verify || predicate == null) return OperationResult.Ok("Local " + label + " command sent.");
                        for (int i = 0; i < 4; i++) { await Task.Delay(2000, token).ConfigureAwait(false); if (predicate(await GetStateAsync(d, token).ConfigureAwait(false))) return OperationResult.Ok("Local " + label + " command verified through cloud."); }
                        if (!fallback) return OperationResult.Fail("Local command was sent but cloud did not verify it.");
                    }
                    catch when (fallback && d.Transport == TransportMode.Hybrid) { }
                }
                else if (localMode && !fallback) return OperationResult.Fail("A valid device IPv4 address is required for local control.");
                if (d.Transport == TransportMode.LocalOnly) return OperationResult.Fail("Local-only control failed and cloud fallback is disabled.");
                string rateKey = d.DeviceId ?? d.Sku ?? "device";
                DateTime last;
                if (_lastCloudCommand.TryGetValue(rateKey, out last))
                {
                    TimeSpan wait = TimeSpan.FromMilliseconds(550) - (DateTime.UtcNow - last);
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false);
                }
                await cloud().ConfigureAwait(false);
                _lastCloudCommand[rateKey] = DateTime.UtcNow;
                if (verify && predicate != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        await Task.Delay(2000, token).ConfigureAwait(false);
                        if (predicate(await GetStateAsync(d, token).ConfigureAwait(false))) return OperationResult.Ok("Cloud " + label + " fallback verified.", localMode);
                    }
                    return OperationResult.Fail("The cloud command was accepted but its state was not verified.");
                }
                return OperationResult.Ok("Cloud " + label + " command accepted.", localMode);
            }
            catch (Exception ex) { return OperationResult.Fail(Sanitize(ex.Message)); }
            finally { _gate.Release(); }
        }

        public async Task ApplyStartupAsync(PluginSettings settings, CancellationToken token)
        {
            await CaptureInitialStatesAsync(settings.Devices, token).ConfigureAwait(false);
            if (settings.StartupPolicy == StartupPolicy.LeaveUnchanged) return;
            foreach (var d in settings.Devices.Where(x => x.Selected)) await SetPowerAsync(d, settings.StartupPowerOn, false, settings.CloudFallback, token).ConfigureAwait(false);
        }
        public async Task ApplyExitAsync(PluginSettings settings, CancellationToken token)
        {
            if (settings.ExitPolicy == ExitPolicy.LeaveUnchanged) return;
            foreach (var d in settings.Devices.Where(x => x.Selected))
            {
                if (settings.ExitPolicy == ExitPolicy.RestorePrevious)
                {
                    DeviceState state; if (_initial.TryGetValue(d.DeviceId ?? "", out state)) await RestoreAsync(d, state, settings.CloudFallback, token).ConfigureAwait(false);
                }
                else await SetPowerAsync(d, settings.ExitPolicy == ExitPolicy.ConfiguredState && settings.ExitPowerOn, false, settings.CloudFallback, token).ConfigureAwait(false);
            }
        }
        private async Task RestoreAsync(DeviceSettings d, DeviceState s, bool fallback, CancellationToken token)
        {
            if (s.Brightness.HasValue) await SetBrightnessAsync(d, s.Brightness.Value, fallback, token).ConfigureAwait(false);
            if (s.Rgb.HasValue) { int rgb = s.Rgb.Value; await SetColorAsync(d, rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255, fallback, token).ConfigureAwait(false); }
            // Power is deliberately last: some lights may wake when brightness/color is changed.
            if (s.PowerOn.HasValue) await SetPowerAsync(d, s.PowerOn.Value, false, fallback, token).ConfigureAwait(false);
        }
        private string RequireKey() { string key = _getApiKey(); if (string.IsNullOrWhiteSpace(key)) throw new GoveeCloudException("Save a Govee Developer API key first."); return key; }
        private static string Sanitize(string message) => string.IsNullOrWhiteSpace(message) ? "Govee operation failed." : message.Replace("\r", " ").Replace("\n", " ");
    }
}
