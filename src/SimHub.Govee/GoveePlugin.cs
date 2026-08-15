using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimHub.Govee
{
    [PluginDescription("Govee Controller Plugin for SimHub controls compatible Govee lights using local UDP with cloud discovery and fallback.")]
    [PluginAuthor("StormFuel")]
    [PluginName("Govee Controller Plugin for SimHub")]
    public sealed class GoveePlugin : IDataPlugin, IWPFSettingsV2
    {
        private const string SettingsKey = "SimHubGoveeSettings";
        private readonly ICredentialProtector _protector = new DpapiCredentialProtector();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private GoveeCloudClient _cloud;
        internal GoveeController Controller { get; private set; }
        internal LightStateDispatcher Dispatcher { get; private set; }
        private GameSessionCoordinator _gameCoordinator;
        private readonly GameTransitionDetector _gameTransitions = new GameTransitionDetector();
        private readonly object _automationLock = new object();
        private Task _automationTail = Task.CompletedTask;
        internal PluginSettings Settings { get; private set; }
        public PluginManager PluginManager { get; set; }
        public string Status { get; private set; } = "Not initialized";
        internal string LastDetectedGame { get; private set; }
        private static readonly ImageSource DarkThemePluginIcon = LoadPluginIcon("SimHub.Govee.Assets.SimHubGoveeIcon.png");
        private static readonly ImageSource LightThemePluginIcon = LoadPluginIcon("SimHub.Govee.Assets.SimHubGoveeIconLight.png");
        public ImageSource PictureIcon => IsDarkApplicationTheme() ? DarkThemePluginIcon : LightThemePluginIcon;
        public string LeftMenuTitle => "Govee Controller";

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            Settings = this.ReadCommonSettings<PluginSettings>(SettingsKey, () => new PluginSettings());
            SettingsValidator.Normalize(Settings);
            ManagedActionPlanner.Reconcile(Settings);
            _cloud = new GoveeCloudClient();
            Controller = new GoveeController(new GoveeLanClient(), _cloud, GetApiKey);
            Dispatcher = new LightStateDispatcher(Controller);
            _gameCoordinator = new GameSessionCoordinator(Controller, Dispatcher);
            RegisterActions();
            Status = HasApiKey ? "Ready — refresh devices in settings" : "Setup required — save a Developer API key";
            if (Settings.Devices.Exists(d => d.Selected))
            {
                Task.Run(async () =>
                {
                    try { Status = "Applying startup policy…"; await ApplyStartupPolicyAsync(_lifetime.Token).ConfigureAwait(false); Status = "Ready"; }
                    catch (Exception ex) { Status = "Startup: " + SafeMessage(ex); SimHub.Logging.Current.Warn("Govee Controller startup failed: " + SafeMessage(ex)); }
                });
            }
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            var manager = pluginManager ?? PluginManager;
            string gameCode = GameRuntimeIdentity.Resolve(manager?.GameDescription?.Code, manager?.GameName, data?.GameName);
            bool gameDetected = data != null && GameRuntimeIdentity.IsDetected(data.GameRunning, data.RunningGameProcessDetected);
            if (gameDetected && !string.IsNullOrWhiteSpace(gameCode)) LastDetectedGame = gameCode;
            var transition = _gameTransitions.Observe(gameDetected, gameCode, DateTime.UtcNow);
            if (transition.Type == GameTransitionType.None) return;
            SimHub.Logging.Current.Info("Govee Controller game transition: " + transition.Type +
                (string.IsNullOrWhiteSpace(transition.CurrentGame) ? string.Empty : " (" + transition.CurrentGame + ")"));
            QueueAutomation(async () =>
            {
                try
                {
                    Status = transition.Type == GameTransitionType.Stopped ? "Restoring pre-game light state…" : "Applying profile for " + transition.CurrentGame + "…";
                    var result = await _gameCoordinator.HandleAsync(transition, Settings, _lifetime.Token).ConfigureAwait(false);
                    Status = result.Message;
                    string outcome = "Govee Controller profile " + (result.Success ? "succeeded" : "failed") + " for " + (transition.CurrentGame ?? transition.PreviousGame ?? "unknown game") + ": " + result.Message;
                    if (result.Success) SimHub.Logging.Current.Info(outcome); else SimHub.Logging.Current.Warn(outcome);
                }
                catch (Exception ex) { Status = "Game profile: " + SafeMessage(ex); SimHub.Logging.Current.Warn("Govee Controller profile exception: " + SafeMessage(ex)); }
            });
        }

        public void End(PluginManager pluginManager)
        {
            try
            {
                // Stop game/manual work first so the clean-exit policy is the final command source.
                _lifetime.Cancel();
                if (Controller != null && Settings != null)
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                        try { Controller.ApplyExitAsync(Settings, timeout.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { SimHub.Logging.Current.Warn("Govee Controller exit policy timed out."); }
                }
                SaveSettings();
            }
            catch (Exception ex) { SimHub.Logging.Current.Error("Govee Controller shutdown failed: " + SafeMessage(ex)); }
            finally { _cloud?.Dispose(); _lifetime.Dispose(); }
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsView(this);
        internal bool HasApiKey => Settings != null && !string.IsNullOrWhiteSpace(Settings.EncryptedApiKey);
        internal void SaveApiKey(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) throw new ArgumentException("Enter a Govee Developer API key.");
            Settings.EncryptedApiKey = _protector.Protect(plainText.Trim()); SaveSettings(); Status = "API key saved securely";
        }
        internal void RemoveApiKey() { Settings.EncryptedApiKey = null; Settings.RefreshStateBeforeAction = false; SaveSettings(); Status = "API key removed"; }
        internal void ResetSettings()
        {
            Settings = new PluginSettings();
            SaveSettings();
            Status = "Plugin settings reset. Restart SimHub to clear in-memory state and obsolete action registrations.";
        }
        internal void SaveSettings() { SettingsValidator.Normalize(Settings); ManagedActionPlanner.Reconcile(Settings); SettingsValidator.Normalize(Settings); this.SaveCommonSettings(SettingsKey, Settings); RegisterActions(); }

        private async Task ApplyStartupPolicyAsync(CancellationToken token)
        {
            var selected = Settings.Devices.Where(x => x.Selected).ToList();
            await Controller.CaptureInitialStatesAsync(selected, token).ConfigureAwait(false);
            var state = AutomationPolicy.StateForStartup(Settings); if (state == null) return;
            var result = await Dispatcher.ApplyAsync(AutomationPolicy.ResolveStartupTargets(Settings), state, Settings.CloudFallback, token).ConfigureAwait(false);
            if (!result.Success) throw new InvalidOperationException(result.Message);
        }
        private string GetApiKey() { return HasApiKey ? _protector.Unprotect(Settings.EncryptedApiKey) : null; }
        internal void SetStatus(string status) { Status = status; }
        internal static string SafeMessage(Exception ex) => (ex?.Message ?? "Unknown error").Replace("\r", " ").Replace("\n", " ");

        private static bool IsDarkApplicationTheme()
        {
            try
            {
                var window = Application.Current == null ? null : Application.Current.MainWindow;
                var foreground = window == null ? null : window.Foreground as SolidColorBrush;
                if (foreground != null) return RelativeLuminance(foreground.Color) > 0.5;
                var background = window == null ? null : window.Background as SolidColorBrush;
                if (background != null) return RelativeLuminance(background.Color) < 0.5;
            }
            catch { }
            return true;
        }

        private static double RelativeLuminance(Color color)
        {
            return (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        }

        private static ImageSource LoadPluginIcon(string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                var icon = new BitmapImage();
                icon.BeginInit();
                icon.CacheOption = BitmapCacheOption.OnLoad;
                icon.StreamSource = stream;
                icon.EndInit();
                icon.Freeze();
                return icon;
            }
        }

        internal void RegisterActions()
        {
            if (PluginManager == null || Settings == null) return;
            PluginManager.ClearActions(GetType());
            foreach (var action in ActionRegistrationPlanner.Build(Settings.ManualActions))
            {
                var captured = action;
                try { PluginManager.AddAction(captured.RegisteredName, GetType(), (manager, parameter) => RunManualAction(captured), null); }
                catch (Exception ex) { SimHub.Logging.Current.Warn("Govee Controller could not register action " + captured.RegisteredName + ": " + SafeMessage(ex)); }
            }
        }

        private void RunManualAction(ManualActionDefinition action)
        {
            QueueAutomation(async () =>
            {
                try
                {
                    DesiredLightState state;
                    IEnumerable<string> ids = action.TargetDeviceIds;
                    if (action.Type == ManualActionType.PowerOn) state = new DesiredLightState { PowerOn = true, Source = action.DisplayName };
                    else if (action.Type == ManualActionType.PowerOff) state = new DesiredLightState { PowerOn = false, Source = action.DisplayName };
                    else if (action.Type == ManualActionType.SetColor)
                    {
                        state = AutomationPolicy.StateForPreset(Settings, action.PresetId, action.DisplayName, action.IsManaged);
                        var preset = Settings.Presets.FirstOrDefault(p => p.Id == action.PresetId);
                        if ((ids == null || !ids.Any()) && preset != null) ids = preset.TargetDeviceIds;
                    }
                    else state = null;

                    var targets = AutomationPolicy.ResolveTargets(Settings, ids);
                    string refreshNote = await RefreshPowerStateBeforeActionAsync(targets).ConfigureAwait(false);
                    if (action.Type == ManualActionType.TogglePower)
                    {
                        state = new DesiredLightState { PowerOn = AutomationPolicy.ToggleShouldTurnOn(targets), Source = action.DisplayName };
                    }
                    var result = await Dispatcher.ApplyAsync(targets, state, Settings.CloudFallback, _lifetime.Token).ConfigureAwait(false);
                    Status = action.DisplayName + ": " + result.Message + refreshNote;
                    string outcome = "Govee Controller action " + action.RegisteredName + (result.Success ? " succeeded: " : " failed: ") + result.Message + refreshNote;
                    if (result.Success) SimHub.Logging.Current.Info(outcome); else SimHub.Logging.Current.Warn(outcome);
                }
                catch (Exception ex) { Status = action.DisplayName + ": " + SafeMessage(ex); SimHub.Logging.Current.Warn("Govee Controller action exception for " + action.RegisteredName + ": " + SafeMessage(ex)); }
            });
        }

        private async Task<string> RefreshPowerStateBeforeActionAsync(IList<DeviceSettings> targets)
        {
            if (!Settings.RefreshStateBeforeAction || !HasApiKey) return string.Empty;
            int attempted = 0, failed = 0;
            foreach (var device in targets.Where(d => !string.IsNullOrWhiteSpace(d.DeviceId)))
            {
                attempted++;
                try { await Controller.GetStateAsync(device, _lifetime.Token).ConfigureAwait(false); }
                catch (Exception ex) { failed++; SimHub.Logging.Current.Warn("Govee Controller could not refresh " + device.DisplayName + " before action: " + SafeMessage(ex)); }
            }
            if (attempted == 0) return " State refresh unavailable for these targets; tracked state was used.";
            if (failed > 0) return " State refresh failed for " + failed + " of " + attempted + " target(s); tracked state was used where needed.";
            return " Power state refreshed before action.";
        }

        private void QueueAutomation(Func<Task> work)
        {
            lock (_automationLock)
            {
                _automationTail = _automationTail.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
            }
        }
    }
}
