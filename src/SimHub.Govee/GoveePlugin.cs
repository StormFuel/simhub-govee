using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimHub.Govee
{
    [PluginDescription("Controls compatible Govee lights from SimHub using local UDP with cloud discovery and fallback.")]
    [PluginAuthor("StormFuel")]
    [PluginName("SimHub Govee")]
    public sealed class GoveePlugin : IDataPlugin, IWPFSettingsV2
    {
        private const string SettingsKey = "SimHubGoveeSettings";
        private readonly ICredentialProtector _protector = new DpapiCredentialProtector();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private GoveeCloudClient _cloud;
        internal GoveeController Controller { get; private set; }
        internal PluginSettings Settings { get; private set; }
        public PluginManager PluginManager { get; set; }
        public string Status { get; private set; } = "Not initialized";
        private static readonly ImageSource PluginIcon = LoadPluginIcon();
        public ImageSource PictureIcon => PluginIcon;
        public string LeftMenuTitle => "SimHub Govee";

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            Settings = this.ReadCommonSettings<PluginSettings>(SettingsKey, () => new PluginSettings());
            SettingsValidator.Normalize(Settings);
            _cloud = new GoveeCloudClient();
            Controller = new GoveeController(new GoveeLanClient(), _cloud, GetApiKey);
            Status = HasApiKey ? "Ready — refresh devices in settings" : "Setup required — save a Developer API key";
            if (Settings.Devices.Exists(d => d.Selected))
            {
                Task.Run(async () =>
                {
                    try { Status = "Applying startup policy…"; await Controller.ApplyStartupAsync(Settings, _lifetime.Token).ConfigureAwait(false); Status = "Ready"; }
                    catch (Exception ex) { Status = "Startup: " + SafeMessage(ex); SimHub.Logging.Current.Warn("SimHub Govee startup failed: " + SafeMessage(ex)); }
                });
            }
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data) { }

        public void End(PluginManager pluginManager)
        {
            try
            {
                if (Controller != null && Settings != null)
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                        try { Controller.ApplyExitAsync(Settings, timeout.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { SimHub.Logging.Current.Warn("SimHub Govee exit policy timed out."); }
                }
                SaveSettings();
            }
            catch (Exception ex) { SimHub.Logging.Current.Error("SimHub Govee shutdown failed: " + SafeMessage(ex)); }
            finally { _lifetime.Cancel(); _cloud?.Dispose(); _lifetime.Dispose(); }
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsView(this);
        internal bool HasApiKey => Settings != null && !string.IsNullOrWhiteSpace(Settings.EncryptedApiKey);
        internal void SaveApiKey(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) throw new ArgumentException("Enter a Govee Developer API key.");
            Settings.EncryptedApiKey = _protector.Protect(plainText.Trim()); SaveSettings(); Status = "API key saved securely";
        }
        internal void RemoveApiKey() { Settings.EncryptedApiKey = null; SaveSettings(); Status = "API key removed"; }
        internal void SaveSettings() { SettingsValidator.Normalize(Settings); this.SaveCommonSettings(SettingsKey, Settings); }
        private string GetApiKey() { return HasApiKey ? _protector.Unprotect(Settings.EncryptedApiKey) : null; }
        internal void SetStatus(string status) { Status = status; }
        internal static string SafeMessage(Exception ex) => (ex?.Message ?? "Unknown error").Replace("\r", " ").Replace("\n", " ");

        private static ImageSource LoadPluginIcon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SimHub.Govee.Assets.SimHubGoveeIcon.png"))
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
    }
}
