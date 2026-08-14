using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimHub.Govee
{
    public partial class SettingsView : UserControl
    {
        public static Array TransportModes => Enum.GetValues(typeof(TransportMode));
        private readonly GoveePlugin _plugin;
        private ObservableCollection<DeviceSettings> _devices;
        private ObservableCollection<LightPreset> _presets;
        private ObservableCollection<GameProfile> _profiles;
        private ObservableCollection<ManualActionDefinition> _actions;
        private readonly List<Tuple<DeviceSettings, CheckBox>> _presetTargetBoxes = new List<Tuple<DeviceSettings, CheckBox>>();
        private bool _updatingPresetColor;
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
            _presets = new ObservableCollection<LightPreset>(s.Presets); PresetsGrid.ItemsSource = _presets;
            _profiles = new ObservableCollection<GameProfile>(s.GameProfiles); ProfilesGrid.ItemsSource = _profiles;
            _actions = new ObservableCollection<ManualActionDefinition>(s.ManualActions); ActionsGrid.ItemsSource = _actions;
            PopulatePresetTargets(null);
            DefaultBehaviorBox.ItemsSource = Enum.GetValues(typeof(ProfileBehavior)); DefaultBehaviorBox.SelectedItem = s.DefaultGameProfile.Behavior;
            NewProfileBehaviorBox.ItemsSource = Enum.GetValues(typeof(ProfileBehavior)); NewProfileBehaviorBox.SelectedItem = ProfileBehavior.LeaveUnchanged;
            ActionTypeBox.ItemsSource = Enum.GetValues(typeof(ManualActionType)); ActionTypeBox.SelectedItem = ManualActionType.PowerOn;
            RefreshPresetSources(); DefaultPresetBox.SelectedValue = s.DefaultGameProfile.PresetId;
            RefreshGameCatalog();
            HideLogicalCheck.IsChecked = s.HideLogicalDevices; FallbackCheck.IsChecked = s.CloudFallback; StartupOnCheck.IsChecked = s.StartupPowerOn; ExitOnCheck.IsChecked = s.ExitPowerOn;
            RefreshStateBeforeActionCheck.IsChecked = s.RefreshStateBeforeAction; RefreshStateBeforeActionCheck.IsEnabled = _plugin.HasApiKey;
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
            s.RefreshStateBeforeAction = _plugin.HasApiKey && RefreshStateBeforeActionCheck.IsChecked == true;
            s.Presets = _presets.ToList(); s.GameProfiles = _profiles.ToList(); s.ManualActions = _actions.ToList();
            if (DefaultBehaviorBox.SelectedItem != null) s.DefaultGameProfile.Behavior = (ProfileBehavior)DefaultBehaviorBox.SelectedItem;
            s.DefaultGameProfile.PresetId = s.DefaultGameProfile.Behavior == ProfileBehavior.ApplyPreset ? DefaultPresetBox.SelectedValue as string : null;
            if (StartupPolicyBox.SelectedItem != null) s.StartupPolicy = (StartupPolicy)StartupPolicyBox.SelectedItem;
            if (ExitPolicyBox.SelectedItem != null) s.ExitPolicy = (ExitPolicy)ExitPolicyBox.SelectedItem;
        }
        private void SettingChanged(object sender, RoutedEventArgs e) { CopyToSettings(); }
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CopyToSettings(); ValidateSettings(); _plugin.SaveSettings(); LoadSettings(); ShowStatus("Settings saved.");
            }
            catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); }
        }
        private void SaveKey_Click(object sender, RoutedEventArgs e) { try { _plugin.SaveApiKey(ApiKeyBox.Password); ApiKeyBox.Clear(); LoadSettings(); } catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); } }
        private void RemoveKey_Click(object sender, RoutedEventArgs e) { _plugin.RemoveApiKey(); ApiKeyBox.Clear(); LoadSettings(); }
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await BusyAsync(async () => { CopyToSettings(); var devices = await _plugin.Controller.DiscoverAsync(_plugin.Settings, CancellationToken.None); _devices = new ObservableCollection<DeviceSettings>(devices); DevicesGrid.ItemsSource = _devices; PopulatePresetTargets(null); _plugin.SaveSettings(); return "Found " + devices.Count + " device(s)."; });
        }
        private async void TestOn_Click(object sender, RoutedEventArgs e) { await TestPowerAsync(true); }
        private async void TestOff_Click(object sender, RoutedEventArgs e) { await TestPowerAsync(false); }
        private async Task TestPowerAsync(bool on)
        {
            CopyToSettings(); var selected = _devices.Where(d => d.Selected).ToList();
            if (selected.Count == 0) { ShowStatus("Select at least one device first."); return; }
            if (MessageBox.Show("Set " + selected.Count + " selected device(s) " + (on ? "ON" : "OFF") + " and verify each through cloud?", "Govee Controller test", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            await BusyAsync(async () => { foreach (var d in selected) { var result = await _plugin.Controller.SetPowerAsync(d, on, true, _plugin.Settings.CloudFallback, CancellationToken.None); if (!result.Success) throw new InvalidOperationException(d.DisplayName + ": " + result.Message); } return "Test succeeded and cloud verification passed."; });
        }
        private async Task BusyAsync(Func<Task<string>> action)
        {
            IsEnabled = false; ShowStatus("Working…");
            try { ShowStatus(await action()); } catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); } finally { IsEnabled = true; }
        }
        private void ShowStatus(string text) { _plugin.SetStatus(text); StatusText.Text = text; }
        private void ValidateSettings()
        {
            foreach (var p in _presets)
            {
                if (string.IsNullOrWhiteSpace(p.Name)) throw new ArgumentException("Every preset needs a name.");
                if (!SettingsValidator.IsValidRgbHex(p.HexColor)) throw new ArgumentException("Preset '" + p.Name + "' must use a #RRGGBB color.");
                if (p.Brightness.HasValue && (p.Brightness < 0 || p.Brightness > 100)) throw new ArgumentException("Preset '" + p.Name + "' brightness must be 0-100 or blank.");
            }
            foreach (var p in _profiles) if (p.Behavior == ProfileBehavior.ApplyPreset && !_presets.Any(x => x.Id == p.PresetId)) throw new ArgumentException("Game profile '" + p.GameCode + "' must reference an existing preset.");
            if (_profiles.GroupBy(p => p.GameCode ?? "", StringComparer.OrdinalIgnoreCase).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1)) throw new ArgumentException("Every custom game profile needs a unique game code.");
            if (_actions.GroupBy(a => a.ActionKey ?? "", StringComparer.OrdinalIgnoreCase).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1)) throw new ArgumentException("Every SimHub action needs a unique immutable key.");
            foreach (var a in _actions) if (a.Type == ManualActionType.SetColor && !_presets.Any(x => x.Id == a.PresetId)) throw new ArgumentException("Set Color action '" + a.RegisteredName + "' must reference an existing preset.");
            if (_plugin.Settings.DefaultGameProfile.Behavior == ProfileBehavior.ApplyPreset && !_presets.Any(x => x.Id == _plugin.Settings.DefaultGameProfile.PresetId)) throw new ArgumentException("The Default Game Profile must reference an existing preset.");
        }

        private void RefreshPresetSources()
        {
            DefaultPresetBox.ItemsSource = _presets; NewProfilePresetBox.ItemsSource = _presets; ActionPresetBox.ItemsSource = _presets;
        }
        private void AddPreset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var preset = ReadPresetForm(); _presets.Add(preset);
                RefreshPresetSources(); CopyToSettings(); _plugin.SaveSettings(); LoadSettings(); ShowStatus("Preset and its managed color action added.");
            }
            catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); }
        }
        private LightPreset ReadPresetForm(LightPreset existing = null)
        {
            string name = PresetNameBox.Text.Trim(); if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a preset name.");
            string hex = HexModeRadio.IsChecked == true ? PresetHexBox.Text.Trim() : RgbToHex(PresetRedBox.Text, PresetGreenBox.Text, PresetBlueBox.Text);
            if (!SettingsValidator.IsValidRgbHex(hex)) throw new ArgumentException("HEX color must use #RRGGBB format.");
            int brightness; int? value = null;
            if (!string.IsNullOrWhiteSpace(PresetBrightnessBox.Text)) { if (!int.TryParse(PresetBrightnessBox.Text, out brightness) || brightness < 0 || brightness > 100) throw new ArgumentException("Brightness must be blank or between 0 and 100."); value = brightness; }
            var targetIds = ReadPresetTargetIds();
            var result = existing ?? new LightPreset(); result.Name = name; result.HexColor = hex.ToUpperInvariant(); result.Brightness = value; result.TurnOn = PresetTurnOnCheck.IsChecked == true; result.TargetDeviceIds = targetIds; return result;
        }
        private static string RgbToHex(string red, string green, string blue)
        {
            int r, g, b; if (!int.TryParse(red, out r) || !int.TryParse(green, out g) || !int.TryParse(blue, out b) || new[] { r, g, b }.Any(v => v < 0 || v > 255)) throw new ArgumentException("Red, green, and blue must each be between 0 and 255.");
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", r, g, b);
        }
        private void UpdatePreset_Click(object sender, RoutedEventArgs e)
        {
            var selected = PresetsGrid.SelectedItem as LightPreset; if (selected == null) { ShowStatus("Select a preset to update."); return; }
            try { ReadPresetForm(selected); PresetsGrid.Items.Refresh(); RefreshPresetSources(); CopyToSettings(); _plugin.SaveSettings(); LoadSettings(); ShowStatus("Preset and its managed color action updated; the registered key was preserved."); }
            catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); }
        }
        private void NewPreset_Click(object sender, RoutedEventArgs e)
        {
            PresetsGrid.SelectedItem = null; PresetNameBox.Clear(); PresetBrightnessBox.Clear(); PresetTurnOnCheck.IsChecked = true; HexModeRadio.IsChecked = true; SetPresetColor("#FFFFFF"); PresetAllTargetsRadio.IsChecked = true; PopulatePresetTargets(null); ShowStatus("Enter the new preset settings.");
        }
        private void PresetSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return; var p = PresetsGrid.SelectedItem as LightPreset; if (p == null) return;
            PresetNameBox.Text = p.Name; PresetBrightnessBox.Text = p.Brightness.HasValue ? p.Brightness.Value.ToString(CultureInfo.InvariantCulture) : ""; PresetTurnOnCheck.IsChecked = p.TurnOn; SetPresetColor(p.HexColor); PresetAllTargetsRadio.IsChecked = p.TargetDeviceIds == null || p.TargetDeviceIds.Count == 0; PresetSpecificTargetsRadio.IsChecked = !PresetAllTargetsRadio.IsChecked; PopulatePresetTargets(p.TargetDeviceIds);
        }
        private void ColorModeChanged(object sender, RoutedEventArgs e)
        {
            if (PresetHexBox == null || PresetRgbPanel == null) return; PresetHexBox.IsEnabled = HexModeRadio.IsChecked == true; PresetRgbPanel.IsEnabled = RgbModeRadio.IsChecked == true; UpdatePresetPreview();
        }
        private void PresetColorChanged(object sender, TextChangedEventArgs e) { if (!_updatingPresetColor) UpdatePresetPreview(); }
        private void UpdatePresetPreview()
        {
            if (PresetColorPreview == null) return;
            try { string hex = HexModeRadio.IsChecked == true ? PresetHexBox.Text.Trim() : RgbToHex(PresetRedBox.Text, PresetGreenBox.Text, PresetBlueBox.Text); if (SettingsValidator.IsValidRgbHex(hex)) PresetColorPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { }
        }
        private void SetPresetColor(string hex)
        {
            if (!SettingsValidator.IsValidRgbHex(hex)) hex = "#FFFFFF"; int rgb = SettingsValidator.HexToRgb(hex); _updatingPresetColor = true;
            try { PresetHexBox.Text = hex.ToUpperInvariant(); PresetRedBox.Text = ((rgb >> 16) & 255).ToString(CultureInfo.InvariantCulture); PresetGreenBox.Text = ((rgb >> 8) & 255).ToString(CultureInfo.InvariantCulture); PresetBlueBox.Text = (rgb & 255).ToString(CultureInfo.InvariantCulture); PresetColorPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            finally { _updatingPresetColor = false; }
        }
        private void PresetSwatch_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button; SetPresetColor(button == null ? "#FFFFFF" : Convert.ToString(button.Tag, CultureInfo.InvariantCulture));
            if (button != null && string.IsNullOrWhiteSpace(PresetNameBox.Text)) PresetNameBox.Text = Convert.ToString(button.ToolTip, CultureInfo.CurrentCulture);
        }
        private void ChoosePresetColor_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
            {
                try { int rgb = SettingsValidator.HexToRgb(HexModeRadio.IsChecked == true ? PresetHexBox.Text : RgbToHex(PresetRedBox.Text, PresetGreenBox.Text, PresetBlueBox.Text)); dialog.Color = System.Drawing.Color.FromArgb(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255); } catch { dialog.Color = System.Drawing.Color.White; }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) SetPresetColor(string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B));
            }
        }
        private void PresetTargetModeChanged(object sender, RoutedEventArgs e) { if (PresetTargetList != null) PresetTargetList.IsEnabled = PresetSpecificTargetsRadio.IsChecked == true; }
        private void PopulatePresetTargets(IEnumerable<string> selectedIds)
        {
            if (PresetTargetList == null) return; var selected = new HashSet<string>(selectedIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase); PresetTargetList.Children.Clear(); _presetTargetBoxes.Clear();
            foreach (var d in _devices.Where(x => x.Selected)) { var box = new CheckBox { Content = d.DisplayName, IsChecked = selected.Contains(d.TargetId), Margin = new Thickness(0, 2, 0, 2) }; _presetTargetBoxes.Add(Tuple.Create(d, box)); PresetTargetList.Children.Add(box); }
            PresetNoTargetsText.Visibility = _presetTargetBoxes.Count == 0 ? Visibility.Visible : Visibility.Collapsed; PresetTargetList.IsEnabled = PresetSpecificTargetsRadio.IsChecked == true;
        }
        private List<string> ReadPresetTargetIds()
        {
            if (PresetAllTargetsRadio.IsChecked == true) return new List<string>();
            var ids = _presetTargetBoxes.Where(x => x.Item2.IsChecked == true).Select(x => x.Item1.TargetId).ToList(); if (ids.Count == 0) throw new ArgumentException("Choose at least one target light, or select All lights."); return ids;
        }
        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            var p = PresetsGrid.SelectedItem as LightPreset; if (p == null) return;
            if (_profiles.Any(x => x.PresetId == p.Id) || _actions.Any(x => !x.IsManaged && x.PresetId == p.Id)) { ShowStatus("This preset is used by a game profile or custom action and cannot be deleted."); return; }
            if (MessageBox.Show("Delete preset '" + p.Name + "' and its managed color action? Existing bindings to that generated action will stop working.", "Delete preset", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            _presets.Remove(p); RefreshPresetSources(); CopyToSettings(); _plugin.SaveSettings(); LoadSettings(); ShowStatus("Preset and its managed color action deleted.");
        }
        private void AddProfile_Click(object sender, RoutedEventArgs e)
        {
            string game = GamePickerBox.SelectedValue as string; if (string.IsNullOrWhiteSpace(game)) { ShowStatus("Choose a registered SimHub game or use the detected game."); return; }
            if (_profiles.Any(p => string.Equals(p.GameCode, game, StringComparison.OrdinalIgnoreCase))) { ShowStatus("A profile for this game already exists."); return; }
            var behavior = (ProfileBehavior)NewProfileBehaviorBox.SelectedItem; string preset = behavior == ProfileBehavior.ApplyPreset ? NewProfilePresetBox.SelectedValue as string : null;
            if (behavior == ProfileBehavior.ApplyPreset && string.IsNullOrWhiteSpace(preset)) { ShowStatus("Choose a preset for Apply Preset."); return; }
            _profiles.Add(new GameProfile { GameCode = game, DisplayName = game, Behavior = behavior, PresetId = preset, PresetName = SelectedPresetName(preset) }); CopyToSettings(); _plugin.SaveSettings(); ShowStatus("Game profile added.");
        }
        private void UpdateProfile_Click(object sender, RoutedEventArgs e)
        {
            var p = ProfilesGrid.SelectedItem as GameProfile; if (p == null) { ShowStatus("Select a game profile to update."); return; }
            var behavior = (ProfileBehavior)NewProfileBehaviorBox.SelectedItem; string preset = behavior == ProfileBehavior.ApplyPreset ? NewProfilePresetBox.SelectedValue as string : null;
            if (behavior == ProfileBehavior.ApplyPreset && string.IsNullOrWhiteSpace(preset)) { ShowStatus("Choose a preset for Apply Preset."); return; }
            p.Behavior = behavior; p.PresetId = preset; p.PresetName = SelectedPresetName(preset); ProfilesGrid.Items.Refresh(); CopyToSettings(); _plugin.SaveSettings(); ShowStatus("Game profile updated.");
        }
        private void UseDetectedGame_Click(object sender, RoutedEventArgs e)
        {
            string detected = _plugin.LastDetectedGame; if (string.IsNullOrWhiteSpace(detected)) { ShowStatus("No running game has been detected yet."); return; }
            RefreshGameCatalog(detected); GamePickerBox.SelectedValue = detected; ShowStatus("Selected detected game: " + detected + ".");
        }
        private void DefaultPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading && DefaultPresetBox.SelectedValue != null) DefaultBehaviorBox.SelectedItem = ProfileBehavior.ApplyPreset;
        }
        private void ProfilePresetSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading && NewProfilePresetBox.SelectedValue != null) NewProfileBehaviorBox.SelectedItem = ProfileBehavior.ApplyPreset;
        }
        private void DeleteProfile_Click(object sender, RoutedEventArgs e) { var p = ProfilesGrid.SelectedItem as GameProfile; if (p != null) { _profiles.Remove(p); CopyToSettings(); _plugin.SaveSettings(); } }
        private void AddAction_Click(object sender, RoutedEventArgs e)
        {
            if (ActionKeyBox.IsReadOnly) { ShowStatus("Click New action before creating another immutable action key."); return; }
            string key = SettingsValidator.NormalizeActionKey(ActionKeyBox.Text); if (string.IsNullOrWhiteSpace(key)) { ShowStatus("Enter an action key using letters, numbers, hyphen, or underscore."); return; }
            if (_actions.Any(a => string.Equals(a.ActionKey, key, StringComparison.OrdinalIgnoreCase))) { ShowStatus("That immutable action key already exists."); return; }
            var type = (ManualActionType)ActionTypeBox.SelectedItem; string preset = ActionPresetBox.SelectedValue as string;
            if (type == ManualActionType.SetColor && string.IsNullOrWhiteSpace(preset)) { ShowStatus("Choose a color preset for Set Color."); return; }
            _actions.Add(new ManualActionDefinition { ActionKey = key, DisplayName = string.IsNullOrWhiteSpace(ActionNameBox.Text) ? key : ActionNameBox.Text.Trim(), Type = type, PresetId = preset, PresetName = SelectedPresetName(preset) });
            CopyToSettings(); _plugin.SaveSettings(); ShowStatus("Action registered as SimHubGovee." + key + ". The key cannot be renamed.");
        }
        private void NewAction_Click(object sender, RoutedEventArgs e)
        {
            ActionsGrid.SelectedItem = null; ActionKeyBox.IsReadOnly = false; ActionNameBox.IsReadOnly = false; ActionTypeBox.IsEnabled = true; ActionPresetBox.IsEnabled = true; ActionTargetsButton.IsEnabled = true; DeleteActionButton.IsEnabled = true; ActionKeyBox.Clear(); ActionNameBox.Clear(); ActionTypeBox.SelectedItem = ManualActionType.PowerOn; ActionPresetBox.SelectedItem = null; ShowStatus("Enter a permanent action key. It cannot be renamed after creation.");
        }
        private void UpdateAction_Click(object sender, RoutedEventArgs e)
        {
            var a = ActionsGrid.SelectedItem as ManualActionDefinition; if (a == null) { ShowStatus("Select an action to update. Its registered key will remain unchanged."); return; }
            if (a.IsManaged) { ShowStatus("This action is managed automatically. Change color actions through their preset."); return; }
            var type = (ManualActionType)ActionTypeBox.SelectedItem; string preset = ActionPresetBox.SelectedValue as string;
            if (type == ManualActionType.SetColor && string.IsNullOrWhiteSpace(preset)) { ShowStatus("Choose a color preset for Set Color."); return; }
            a.Type = type; a.PresetId = preset; a.PresetName = SelectedPresetName(preset); if (!string.IsNullOrWhiteSpace(ActionNameBox.Text)) a.DisplayName = ActionNameBox.Text.Trim();
            ActionsGrid.Items.Refresh(); CopyToSettings(); _plugin.SaveSettings(); ShowStatus(a.RegisteredName + " updated. Its immutable key and existing bindings were preserved.");
        }
        private void DeleteAction_Click(object sender, RoutedEventArgs e)
        {
            var a = ActionsGrid.SelectedItem as ManualActionDefinition; if (a == null) return;
            if (a.IsManaged) { ShowStatus("Managed actions cannot be deleted independently. Delete a color preset to remove its generated action."); return; }
            if (MessageBox.Show("Delete " + a.RegisteredName + "? Existing SimHub and Dash Studio bindings to this immutable action will stop working.", "Delete SimHub action", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            _actions.Remove(a); CopyToSettings(); _plugin.SaveSettings(); ShowStatus("Action deleted; existing bindings may need remapping.");
        }
        private void ProfileTargets_Click(object sender, RoutedEventArgs e) { var p = ProfilesGrid.SelectedItem as GameProfile; if (p != null && EditTargets(p.TargetDeviceIds, p.GameCode)) ProfilesGrid.Items.Refresh(); }
        private void DefaultTargets_Click(object sender, RoutedEventArgs e) { EditTargets(_plugin.Settings.DefaultGameProfile.TargetDeviceIds, "Default Game Profile"); }
        private void ActionTargets_Click(object sender, RoutedEventArgs e) { var a = ActionsGrid.SelectedItem as ManualActionDefinition; if (a != null && a.IsManaged) { ShowStatus("Managed color actions inherit targets from their preset; default power actions target all selected lights."); return; } if (a != null && EditTargets(a.TargetDeviceIds, a.DisplayName)) ActionsGrid.Items.Refresh(); }
        private bool EditTargets(List<string> targetIds, string title)
        {
            var panel = new StackPanel { Margin = new Thickness(12) }; panel.Children.Add(new TextBlock { Text = "Select no lights to target all globally selected lights.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var boxes = new List<Tuple<DeviceSettings, CheckBox>>();
            foreach (var d in _devices.Where(x => x.Selected)) { var box = new CheckBox { Content = d.DisplayName, IsChecked = targetIds.Contains(d.TargetId), Margin = new Thickness(0, 3, 0, 3) }; boxes.Add(Tuple.Create(d, box)); panel.Children.Add(box); }
            var ok = new Button { Content = "Save targets", IsDefault = true, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(ok);
            var window = new Window { Title = "Target lights — " + title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, MinWidth = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this) };
            ok.Click += (s, e) => { window.DialogResult = true; window.Close(); };
            if (window.ShowDialog() != true) return false;
            targetIds.Clear(); targetIds.AddRange(boxes.Where(x => x.Item2.IsChecked == true).Select(x => x.Item1.TargetId)); CopyToSettings(); _plugin.SaveSettings(); return true;
        }
        private string SelectedPresetName(string id) { var p = _presets.FirstOrDefault(x => x.Id == id); return p == null ? "" : p.Name; }
        private void RefreshGameCatalog(string additionalCode = null)
        {
            var include = _profiles.Select(p => p.GameCode).Concat(new[] { _plugin.LastDetectedGame, additionalCode }); var selected = GamePickerBox.SelectedValue as string;
            GamePickerBox.ItemsSource = SimHubGameCatalog.GetGames(include); if (!string.IsNullOrWhiteSpace(selected)) GamePickerBox.SelectedValue = selected;
        }
        private void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return; var p = ProfilesGrid.SelectedItem as GameProfile; if (p == null) return;
            RefreshGameCatalog(p.GameCode); GamePickerBox.SelectedValue = p.GameCode; NewProfilePresetBox.SelectedValue = p.PresetId; NewProfileBehaviorBox.SelectedItem = p.Behavior;
        }
        private void ActionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return; var a = ActionsGrid.SelectedItem as ManualActionDefinition; if (a == null) return;
            ActionKeyBox.Text = a.ActionKey; ActionKeyBox.IsReadOnly = true; ActionNameBox.Text = a.DisplayName; ActionNameBox.IsReadOnly = a.IsManaged; ActionTypeBox.SelectedItem = a.Type; ActionTypeBox.IsEnabled = !a.IsManaged; ActionPresetBox.SelectedValue = a.PresetId; ActionPresetBox.IsEnabled = !a.IsManaged; ActionTargetsButton.IsEnabled = !a.IsManaged; DeleteActionButton.IsEnabled = !a.IsManaged;
        }
    }
}
