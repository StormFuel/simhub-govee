using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimHub.Govee
{
    public static class GameRuntimeIdentity
    {
        public static string Resolve(string supportedGameCode, string managerGameName, string dataGameName)
        {
            if (!string.IsNullOrWhiteSpace(supportedGameCode)) return supportedGameCode.Trim();
            if (!string.IsNullOrWhiteSpace(managerGameName)) return managerGameName.Trim();
            return string.IsNullOrWhiteSpace(dataGameName) ? null : dataGameName.Trim();
        }

        public static bool IsDetected(bool gameRunning, bool gameProcessDetected)
        {
            return gameRunning || gameProcessDetected;
        }
    }

    public static class AutomationPolicy
    {
        public static GameProfile ResolveProfile(PluginSettings settings, string gameCode)
        {
            return settings.GameProfiles.FirstOrDefault(p => p.Enabled && string.Equals(p.GameCode, gameCode, StringComparison.OrdinalIgnoreCase)) ?? settings.DefaultGameProfile;
        }
        public static IList<DeviceSettings> ResolveTargets(PluginSettings settings, IEnumerable<string> ids)
        {
            var requested = new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return settings.Devices.Where(d => d.Selected && (requested.Count == 0 || requested.Contains(d.TargetId))).ToList();
        }
        public static bool ToggleShouldTurnOn(IList<DeviceSettings> targets)
        {
            return targets == null || targets.Count == 0 || !targets.All(d => d.LastKnownPower == true);
        }
        public static DesiredLightState StateForStartup(PluginSettings settings)
        {
            if (settings == null || settings.StartupPolicy == StartupPolicy.LeaveUnchanged) return null;
            var preset = settings.Presets.FirstOrDefault(x => string.Equals(x.Id, settings.StartupPresetId, StringComparison.OrdinalIgnoreCase));
            return preset == null ? new DesiredLightState { PowerOn = settings.StartupPowerOn, Source = "SimHub startup" } : StateForPreset(settings, preset.Id, "SimHub startup");
        }
        public static IList<DeviceSettings> ResolveStartupTargets(PluginSettings settings)
        {
            var preset = settings.Presets.FirstOrDefault(x => string.Equals(x.Id, settings.StartupPresetId, StringComparison.OrdinalIgnoreCase));
            return ResolveTargets(settings, preset == null ? null : preset.TargetDeviceIds);
        }
        public static DesiredLightState StateForProfile(PluginSettings settings, GameProfile profile)
        {
            if (profile == null || !profile.Enabled || profile.Behavior == ProfileBehavior.LeaveUnchanged) return null;
            if (profile.Behavior == ProfileBehavior.PowerOn) return new DesiredLightState { PowerOn = true, Source = "Game profile" };
            if (profile.Behavior == ProfileBehavior.PowerOff) return new DesiredLightState { PowerOn = false, Source = "Game profile" };
            return StateForPreset(settings, profile.PresetId, "Game profile");
        }
        public static DesiredLightState StateForPreset(PluginSettings settings, string presetId, string source, bool forcePowerOn = false)
        {
            var p = settings.Presets.FirstOrDefault(x => x.Id == presetId);
            if (p == null) return null;
            return new DesiredLightState { PowerOn = forcePowerOn || p.TurnOn ? true : (bool?)null, Brightness = p.Brightness, Rgb = SettingsValidator.HexToRgb(p.HexColor), DeviceAppearances = p.DeviceAppearances ?? new List<DevicePresetAppearance>(), Source = source };
        }
    }

    public static class ActionRegistrationPlanner
    {
        public static IList<ManualActionDefinition> Build(IEnumerable<ManualActionDefinition> actions)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return (actions ?? Enumerable.Empty<ManualActionDefinition>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.ActionKey) && seen.Add(a.RegisteredName))
                .ToList();
        }
    }

    public static class ManagedActionPlanner
    {
        public static void Reconcile(PluginSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.ManualActions == null) settings.ManualActions = new List<ManualActionDefinition>();
            if (settings.Presets == null) settings.Presets = new List<LightPreset>();

            EnsureDefault(settings.ManualActions, "LightsOn", "Lights On", ManualActionType.PowerOn);
            EnsureDefault(settings.ManualActions, "LightsOff", "Lights Off", ManualActionType.PowerOff);
            EnsureDefault(settings.ManualActions, "LightsToggle", "Toggle Lights", ManualActionType.TogglePower);

            var presetIds = new HashSet<string>(settings.Presets.Where(p => p != null).Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            settings.ManualActions.RemoveAll(a => a != null && a.IsManaged && a.Type == ManualActionType.SetColor && !presetIds.Contains(a.PresetId ?? string.Empty));
            foreach (var preset in settings.Presets.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id)))
            {
                var action = settings.ManualActions.FirstOrDefault(a => a != null && a.IsManaged && a.Type == ManualActionType.SetColor && string.Equals(a.PresetId, preset.Id, StringComparison.OrdinalIgnoreCase));
                if (action == null)
                {
                    action = new ManualActionDefinition
                    {
                        ActionKey = UniqueKey(settings.ManualActions, "Color_" + SettingsValidator.NormalizeActionKey(preset.Name)),
                        Type = ManualActionType.SetColor,
                        PresetId = preset.Id,
                        IsManaged = true
                    };
                    settings.ManualActions.Add(action);
                }
                action.DisplayName = "Color: " + preset.Name;
                action.PresetName = preset.Name;
                action.PresetId = preset.Id;
                action.Type = ManualActionType.SetColor;
                action.IsManaged = true;
                action.TargetDeviceIds = new List<string>();
            }
        }

        private static void EnsureDefault(IList<ManualActionDefinition> actions, string key, string label, ManualActionType type)
        {
            var action = actions.FirstOrDefault(a => a != null && string.Equals(a.ActionKey, key, StringComparison.OrdinalIgnoreCase));
            if (action == null)
            {
                action = new ManualActionDefinition { ActionKey = key, TargetDeviceIds = new List<string>() };
                actions.Add(action);
            }
            action.DisplayName = label;
            action.Type = type;
            action.PresetId = null;
            action.PresetName = string.Empty;
            if (action.TargetDeviceIds == null) action.TargetDeviceIds = new List<string>();
            action.IsManaged = true;
        }

        private static string UniqueKey(IEnumerable<ManualActionDefinition> actions, string requested)
        {
            string baseKey = string.IsNullOrWhiteSpace(requested) || string.Equals(requested, "Color_", StringComparison.OrdinalIgnoreCase) ? "Color_Preset" : requested;
            var used = new HashSet<string>((actions ?? Enumerable.Empty<ManualActionDefinition>()).Where(a => a != null).Select(a => a.ActionKey), StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(baseKey)) return baseKey;
            int suffix = 2;
            while (used.Contains(baseKey + "_" + suffix)) suffix++;
            return baseKey + "_" + suffix;
        }
    }

    public sealed class GameTransitionDetector
    {
        private readonly TimeSpan _stopDelay;
        private string _activeGame;
        private DateTime? _stoppedAt;
        public GameTransitionDetector(TimeSpan? stopDelay = null) { _stopDelay = stopDelay ?? TimeSpan.FromSeconds(2); }
        public GameTransition Observe(bool running, string game, DateTime now)
        {
            game = (game ?? string.Empty).Trim();
            if (running && !string.IsNullOrWhiteSpace(game))
            {
                _stoppedAt = null;
                if (_activeGame == null) { _activeGame = game; return new GameTransition(GameTransitionType.Started, null, game); }
                if (!string.Equals(_activeGame, game, StringComparison.OrdinalIgnoreCase)) { string old = _activeGame; _activeGame = game; return new GameTransition(GameTransitionType.Switched, old, game); }
                return GameTransition.None;
            }
            if (_activeGame == null) return GameTransition.None;
            if (!_stoppedAt.HasValue) { _stoppedAt = now; return GameTransition.None; }
            if (now - _stoppedAt.Value < _stopDelay) return GameTransition.None;
            string ended = _activeGame; _activeGame = null; _stoppedAt = null; return new GameTransition(GameTransitionType.Stopped, ended, null);
        }
    }
    public enum GameTransitionType { None, Started, Switched, Stopped }
    public sealed class GameTransition
    {
        public static readonly GameTransition None = new GameTransition(GameTransitionType.None, null, null);
        public GameTransitionType Type { get; }
        public string PreviousGame { get; }
        public string CurrentGame { get; }
        public GameTransition(GameTransitionType type, string previous, string current) { Type = type; PreviousGame = previous; CurrentGame = current; }
    }

    public sealed class LightStateDispatcher
    {
        private readonly GoveeController _controller;
        private long _generation;
        private readonly Dictionary<string, DeviceState> _lastKnown = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
        public LightStateDispatcher(GoveeController controller) { _controller = controller; }
        public async Task<OperationResult> ApplyAsync(IEnumerable<DeviceSettings> devices, DesiredLightState state, bool fallback, CancellationToken token)
        {
            if (state == null) return OperationResult.Ok("No lighting change requested.");
            long mine = Interlocked.Increment(ref _generation); OperationResult last = OperationResult.Ok("No selected target lights.");
            foreach (var d in devices)
            {
                if (mine != Interlocked.Read(ref _generation)) return OperationResult.Fail("Superseded by a newer lighting command.");
                var deviceState = state.ForDevice(d);
                if (deviceState.Brightness.HasValue) last = await _controller.SetBrightnessAsync(d, deviceState.Brightness.Value, fallback, token).ConfigureAwait(false);
                if (last.Success && deviceState.Rgb.HasValue) { int rgb = deviceState.Rgb.Value; last = await _controller.SetColorAsync(d, rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255, fallback, token).ConfigureAwait(false); }
                if (last.Success && deviceState.SegmentColors != null && deviceState.SegmentColors.Count > 0)
                {
                    var allowed = new HashSet<int>((d.SegmentTopology == null ? null : d.SegmentTopology.VerifiedSegmentIndices) ?? new List<int>());
                    if (allowed.Count == 0) return OperationResult.Fail(d.DisplayName + " has no verified segment mapping. Run or import a compatibility test before using segmented presets.");
                    var commands = deviceState.SegmentColors
                        .SelectMany(a => (a.SegmentIndices ?? new List<int>()).Where(allowed.Contains).Select(index => new { Index = index, Hex = SettingsValidator.NormalizeHex(a.HexColor) }))
                        .GroupBy(x => x.Hex, StringComparer.OrdinalIgnoreCase);
                    foreach (var command in commands)
                    {
                        int rgb = SettingsValidator.HexToRgb(command.Key);
                        last = await _controller.SetSegmentColorAsync(d, command.Select(x => x.Index).Distinct().OrderBy(x => x).ToList(), rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255, token).ConfigureAwait(false);
                        if (!last.Success) break;
                    }
                    if (last.Success && d.SegmentTopology.SupportsSegmentedBrightness)
                    {
                        var brightnessCommands = deviceState.SegmentColors.Where(x => x.Brightness.HasValue)
                            .SelectMany(a => (a.SegmentIndices ?? new List<int>()).Where(allowed.Contains).Select(index => new { Index = index, Brightness = a.Brightness.Value }))
                            .GroupBy(x => x.Brightness);
                        foreach (var command in brightnessCommands)
                        {
                            last = await _controller.SetSegmentBrightnessAsync(d, command.Select(x => x.Index).Distinct().OrderBy(x => x).ToList(), command.Key, token).ConfigureAwait(false);
                            if (!last.Success) break;
                        }
                    }
                }
                if (last.Success && deviceState.PowerOn.HasValue) last = await _controller.SetPowerAsync(d, deviceState.PowerOn.Value, false, fallback, token).ConfigureAwait(false);
                if (!last.Success) return last;
                DeviceState known; if (!_lastKnown.TryGetValue(d.DeviceId ?? d.IpAddress ?? "", out known)) known = new DeviceState();
                if (deviceState.Brightness.HasValue) known.Brightness = deviceState.Brightness;
                if (deviceState.Rgb.HasValue) { known.Rgb = deviceState.Rgb; known.SegmentColors.Clear(); }
                if (deviceState.SegmentColors != null && deviceState.SegmentColors.Count > 0) known.SegmentColors = DesiredLightState.CloneAssignments(deviceState.SegmentColors);
                if (deviceState.PowerOn.HasValue) known.PowerOn = deviceState.PowerOn;
                _lastKnown[d.DeviceId ?? d.IpAddress ?? ""] = known;
            }
            return last;
        }
        public DeviceState GetLastKnown(DeviceSettings device) { DeviceState state; return _lastKnown.TryGetValue(device.DeviceId ?? device.IpAddress ?? "", out state) ? state.Clone() : null; }
    }

    public sealed class GameSessionCoordinator
    {
        private readonly GoveeController _controller;
        private readonly LightStateDispatcher _dispatcher;
        private IDictionary<string, DeviceState> _preGame = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
        private bool _sessionActive;
        private readonly SemaphoreSlim _transitionGate = new SemaphoreSlim(1, 1);
        public GameSessionCoordinator(GoveeController controller, LightStateDispatcher dispatcher) { _controller = controller; _dispatcher = dispatcher; }

        public async Task<OperationResult> HandleAsync(GameTransition transition, PluginSettings settings, CancellationToken token)
        {
            await _transitionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (transition.Type == GameTransitionType.None) return OperationResult.Ok("No game transition.");
                if (transition.Type == GameTransitionType.Stopped) return await RestoreAsync(settings, token).ConfigureAwait(false);
                if (!_sessionActive)
                {
                    _preGame = await _controller.CaptureStatesAsync(settings.Devices.Where(d => d.Selected), token).ConfigureAwait(false);
                    foreach (var d in settings.Devices.Where(d => d.Selected))
                    {
                        var known = _dispatcher.GetLastKnown(d);
                        if (known == null) continue;
                        string key = d.DeviceId ?? d.IpAddress ?? "";
                        DeviceState captured;
                        if (_preGame.TryGetValue(key, out captured))
                        {
                            if (known.SegmentColors != null && known.SegmentColors.Count > 0) captured.SegmentColors = DesiredLightState.CloneAssignments(known.SegmentColors);
                        }
                        else _preGame[key] = known;
                    }
                    _sessionActive = true;
                }
                var profile = AutomationPolicy.ResolveProfile(settings, transition.CurrentGame);
                var state = AutomationPolicy.StateForProfile(settings, profile);
                var targetIds = profile == null ? null : profile.TargetDeviceIds;
                if (profile != null && profile.Behavior == ProfileBehavior.ApplyPreset && (targetIds == null || targetIds.Count == 0))
                {
                    var preset = settings.Presets.FirstOrDefault(p => p.Id == profile.PresetId);
                    if (preset != null) targetIds = preset.TargetDeviceIds;
                }
                return await _dispatcher.ApplyAsync(AutomationPolicy.ResolveTargets(settings, targetIds), state, settings.CloudFallback, token).ConfigureAwait(false);
            }
            finally { _transitionGate.Release(); }
        }

        private async Task<OperationResult> RestoreAsync(PluginSettings settings, CancellationToken token)
        {
            OperationResult last = OperationResult.Ok("No pre-game state was available.");
            foreach (var d in settings.Devices.Where(x => x.Selected))
            {
                DeviceState state;
                if (_preGame.TryGetValue(d.DeviceId ?? d.IpAddress ?? "", out state)) last = await _controller.RestoreStateAsync(d, state, settings.CloudFallback, token).ConfigureAwait(false);
                else
                {
                    var fallback = new DesiredLightState { PowerOn = settings.StartupPowerOn, Source = "Best-effort game restore" };
                    last = await _dispatcher.ApplyAsync(new[] { d }, fallback, settings.CloudFallback, token).ConfigureAwait(false);
                }
            }
            _preGame.Clear(); _sessionActive = false; return last;
        }
    }
}
