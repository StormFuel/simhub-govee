using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SimHub.Govee
{
    public partial class PresetEditorWindow : Window
    {
        private readonly IList<DeviceSettings> _devices;
        private readonly Func<DeviceSettings, LightPreset, Task> _configureSegments;
        private readonly List<Tuple<DeviceSettings, CheckBox>> _targetBoxes = new List<Tuple<DeviceSettings, CheckBox>>();
        public LightPreset Draft { get; }

        public PresetEditorWindow(Window owner, LightPreset source, IList<DeviceSettings> devices, Func<DeviceSettings, LightPreset, Task> configureSegments, bool isNew)
        {
            InitializeComponent(); Owner = owner; ApplyOwnerTheme(owner); _devices = devices ?? new List<DeviceSettings>(); _configureSegments = configureSegments; Draft = SettingsDrafts.Preset(source);
            HeadingText.Text = isNew ? "Add light preset" : "Edit light preset"; NameBox.Text = Draft.Name; HexBox.Text = SettingsValidator.NormalizeHex(Draft.HexColor); BrightnessBox.Text = Draft.Brightness.HasValue ? Draft.Brightness.Value.ToString() : string.Empty; TurnOnCheck.IsChecked = Draft.TurnOn;
            BuildSwatches(); BuildTargets(); AdvancedDeviceBox.ItemsSource = _devices.Where(x => x.Selected && x.SegmentTopology != null && x.SegmentTopology.HasUsableMapping).ToList(); AdvancedDeviceBox.SelectedIndex = AdvancedDeviceBox.Items.Count > 0 ? 0 : -1;
            ModeTabs.SelectedIndex = Draft.DeviceAppearances != null && Draft.DeviceAppearances.Count > 0 ? 1 : 0; RefreshAdvanced(); UpdatePreview();
        }

        private void ApplyOwnerTheme(Window owner) { if (owner == null) return; Foreground = owner.Foreground; Background = owner.Background; }
        private void BuildSwatches()
        {
            foreach (var item in new[] { "#FFFFFF", "#FF0000", "#FF8000", "#FFFF00", "#00FF00", "#00BFFF", "#0000FF", "#8000FF", "#FF00FF" })
            {
                string color = item; var button = new Button { Width = 27, Height = 27, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), Margin = new Thickness(0, 0, 5, 0), ToolTip = PresetNameSuggester.FriendlyColor(color) };
                button.Click += (s, e) => { HexBox.Text = color; if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = PresetNameSuggester.FriendlyColor(color); }; SwatchesPanel.Children.Add(button);
            }
        }
        private void BuildTargets()
        {
            var selected = new HashSet<string>(Draft.TargetDeviceIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase); bool all = selected.Count == 0; AllTargetsRadio.IsChecked = all; SpecificTargetsRadio.IsChecked = !all;
            foreach (var device in _devices.Where(x => x.Selected)) { var box = new CheckBox { Content = device.DisplayName, IsChecked = selected.Contains(device.TargetId), Margin = new Thickness(0, 2, 0, 2) }; _targetBoxes.Add(Tuple.Create(device, box)); TargetsPanel.Children.Add(box); }
            TargetsPanel.IsEnabled = !all;
        }
        private void TargetModeChanged(object sender, RoutedEventArgs e) { if (TargetsPanel != null) TargetsPanel.IsEnabled = SpecificTargetsRadio.IsChecked == true; }
        private void HexChanged(object sender, TextChangedEventArgs e) { UpdatePreview(); }
        private void UpdatePreview() { if (ColorPreview == null || !SettingsValidator.IsValidRgbHex(HexBox.Text.Trim())) return; ColorPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexBox.Text.Trim())); }
        private void ChooseColor_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
            {
                try { int rgb = SettingsValidator.HexToRgb(HexBox.Text.Trim()); dialog.Color = System.Drawing.Color.FromArgb(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255); } catch { dialog.Color = System.Drawing.Color.White; }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) HexBox.Text = string.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B);
            }
        }
        private bool CopySimpleToDraft(bool requireName = true)
        {
            ErrorText.Text = string.Empty; int brightness;
            if (requireName && string.IsNullOrWhiteSpace(NameBox.Text)) { ErrorText.Text = "Enter a preset name."; return false; }
            if (!SettingsValidator.IsValidRgbHex(HexBox.Text.Trim())) { ErrorText.Text = "Color must use #RRGGBB format."; return false; }
            if (!string.IsNullOrWhiteSpace(BrightnessBox.Text) && (!int.TryParse(BrightnessBox.Text, out brightness) || brightness < 0 || brightness > 100)) { ErrorText.Text = "Brightness must be blank or between 0 and 100."; return false; }
            Draft.Name = NameBox.Text.Trim(); Draft.HexColor = HexBox.Text.Trim().ToUpperInvariant(); Draft.Brightness = int.TryParse(BrightnessBox.Text, out brightness) ? brightness : (int?)null; Draft.TurnOn = TurnOnCheck.IsChecked == true;
            Draft.TargetDeviceIds = AllTargetsRadio.IsChecked == true ? new List<string>() : _targetBoxes.Where(x => x.Item2.IsChecked == true).Select(x => x.Item1.TargetId).ToList();
            if (SpecificTargetsRadio.IsChecked == true && Draft.TargetDeviceIds.Count == 0) { ErrorText.Text = "Choose at least one target light or select All lights."; return false; }
            return true;
        }
        private async void ConfigureSegments_Click(object sender, RoutedEventArgs e)
        {
            var device = AdvancedDeviceBox.SelectedItem as DeviceSettings; if (device == null) { ErrorText.Text = "Choose a verified segmented device."; return; }
            await ConfigureDeviceAsync(device);
        }
        private async Task ConfigureDeviceAsync(DeviceSettings device)
        {
            if (!CopySimpleToDraft(false)) { ModeTabs.SelectedIndex = 0; return; }
            await _configureSegments(device, Draft); NameBox.Text = Draft.Name; RefreshAdvanced();
        }
        private async void AvailableDeviceDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var device = AdvancedDeviceBox.SelectedItem as DeviceSettings; if (device != null) await ConfigureDeviceAsync(device);
        }
        private async void OverrideDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = OverridesGrid.SelectedItem as AppearanceRow; if (row == null) return;
            var device = _devices.FirstOrDefault(x => string.Equals(x.TargetId, row.TargetId, StringComparison.OrdinalIgnoreCase)); if (device == null) { ErrorText.Text = "That configured device is no longer available."; return; }
            await ConfigureDeviceAsync(device);
        }
        private void RefreshAdvanced()
        {
            var rows = (Draft.DeviceAppearances ?? new List<DevicePresetAppearance>()).Select(x => { var device = _devices.FirstOrDefault(d => string.Equals(d.TargetId, x.TargetId, StringComparison.OrdinalIgnoreCase)); return new AppearanceRow { TargetId = x.TargetId, DeviceName = device == null ? "Unavailable device" : device.DisplayName, Summary = x.UseSegmentedColor ? (x.SegmentColors == null ? 0 : x.SegmentColors.Count) + " segment(s)" : "Device override" }; }).ToList();
            OverridesGrid.ItemsSource = rows; AdvancedPreservedBanner.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        private void ConvertToSimple_Click(object sender, RoutedEventArgs e)
        {
            if (Draft.DeviceAppearances == null || Draft.DeviceAppearances.Count == 0) return;
            if (MessageBox.Show("Remove every per-device and segmented override from this preset? The Simple color and brightness will remain.", "Convert to simple preset", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            Draft.DeviceAppearances.Clear(); RefreshAdvanced(); ModeTabs.SelectedIndex = 0;
        }
        private void Save_Click(object sender, RoutedEventArgs e) { if (!CopySimpleToDraft()) { ModeTabs.SelectedIndex = 0; return; } DialogResult = true; Close(); }
        private sealed class AppearanceRow { public string TargetId { get; set; } public string DeviceName { get; set; } public string Summary { get; set; } }
    }
}
