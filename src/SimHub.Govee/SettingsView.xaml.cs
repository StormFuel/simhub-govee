using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SimHub.Govee
{
    public partial class SettingsView : UserControl
    {
        public static Array TransportModes => Enum.GetValues(typeof(TransportMode));
        private readonly GoveePlugin _plugin;
        private ObservableCollection<DeviceSettings> _devices;
        private bool _loading;
        public SettingsView(GoveePlugin plugin)
        {
            InitializeComponent(); _plugin = plugin; LoadSettings();
        }
        private void LoadSettings()
        {
            _loading = true;
            var s = _plugin.Settings;
            _devices = new ObservableCollection<DeviceSettings>(s.Devices); DevicesGrid.ItemsSource = _devices;
            HideLogicalCheck.IsChecked = s.HideLogicalDevices; FallbackCheck.IsChecked = s.CloudFallback; StartupOnCheck.IsChecked = s.StartupPowerOn; ExitOnCheck.IsChecked = s.ExitPowerOn;
            StartupPolicyBox.ItemsSource = Enum.GetValues(typeof(StartupPolicy)); StartupPolicyBox.SelectedItem = s.StartupPolicy;
            ExitPolicyBox.ItemsSource = Enum.GetValues(typeof(ExitPolicy)); ExitPolicyBox.SelectedItem = s.ExitPolicy;
            KeyStatusText.Text = _plugin.HasApiKey ? "A key is saved (encrypted with Windows DPAPI)." : "No key is saved.";
            StatusText.Text = _plugin.Status; _loading = false;
        }
        private void CopyToSettings()
        {
            if (_loading) return;
            var s = _plugin.Settings; DevicesGrid.CommitEdit();
            s.Devices = _devices.ToList(); s.HideLogicalDevices = HideLogicalCheck.IsChecked == true; s.CloudFallback = FallbackCheck.IsChecked == true; s.StartupPowerOn = StartupOnCheck.IsChecked == true; s.ExitPowerOn = ExitOnCheck.IsChecked == true;
            if (StartupPolicyBox.SelectedItem != null) s.StartupPolicy = (StartupPolicy)StartupPolicyBox.SelectedItem;
            if (ExitPolicyBox.SelectedItem != null) s.ExitPolicy = (ExitPolicy)ExitPolicyBox.SelectedItem;
        }
        private void SettingChanged(object sender, RoutedEventArgs e) { CopyToSettings(); }
        private void Save_Click(object sender, RoutedEventArgs e) { CopyToSettings(); _plugin.SaveSettings(); ShowStatus("Settings saved."); }
        private void SaveKey_Click(object sender, RoutedEventArgs e) { try { _plugin.SaveApiKey(ApiKeyBox.Password); ApiKeyBox.Clear(); LoadSettings(); } catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); } }
        private void RemoveKey_Click(object sender, RoutedEventArgs e) { _plugin.RemoveApiKey(); ApiKeyBox.Clear(); LoadSettings(); }
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await BusyAsync(async () => { CopyToSettings(); var devices = await _plugin.Controller.DiscoverAsync(_plugin.Settings, CancellationToken.None); _devices = new ObservableCollection<DeviceSettings>(devices); DevicesGrid.ItemsSource = _devices; _plugin.SaveSettings(); return "Found " + devices.Count + " device(s)."; });
        }
        private async void TestOn_Click(object sender, RoutedEventArgs e) { await TestPowerAsync(true); }
        private async void TestOff_Click(object sender, RoutedEventArgs e) { await TestPowerAsync(false); }
        private async Task TestPowerAsync(bool on)
        {
            CopyToSettings(); var selected = _devices.Where(d => d.Selected).ToList();
            if (selected.Count == 0) { ShowStatus("Select at least one device first."); return; }
            if (MessageBox.Show("Set " + selected.Count + " selected device(s) " + (on ? "ON" : "OFF") + " and verify each through cloud?", "SimHub Govee test", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            await BusyAsync(async () => { foreach (var d in selected) { var result = await _plugin.Controller.SetPowerAsync(d, on, true, _plugin.Settings.CloudFallback, CancellationToken.None); if (!result.Success) throw new InvalidOperationException(d.DisplayName + ": " + result.Message); } return "Test succeeded and cloud verification passed."; });
        }
        private async Task BusyAsync(Func<Task<string>> action)
        {
            IsEnabled = false; ShowStatus("Working…");
            try { ShowStatus(await action()); } catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); } finally { IsEnabled = true; }
        }
        private void ShowStatus(string text) { _plugin.SetStatus(text); StatusText.Text = text; }
    }
}
