using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
        private bool _loading;

        public SettingsView(GoveePlugin plugin) { InitializeComponent(); _plugin = plugin; LoadSettings(); }

        private void LoadSettings()
        {
            _loading = true; var settings = _plugin.Settings;
            _devices = new ObservableCollection<DeviceSettings>(settings.Devices); DevicesGrid.ItemsSource = _devices;
            _presets = new ObservableCollection<LightPreset>(settings.Presets); PresetsGrid.ItemsSource = _presets;
            _profiles = new ObservableCollection<GameProfile>(settings.GameProfiles); ProfilesGrid.ItemsSource = _profiles;
            _actions = new ObservableCollection<ManualActionDefinition>(settings.ManualActions); ActionsGrid.ItemsSource = _actions;
            HideLogicalCheck.IsChecked = settings.HideLogicalDevices; FallbackCheck.IsChecked = settings.CloudFallback; StartupOnCheck.IsChecked = settings.StartupPowerOn; ExitOnCheck.IsChecked = settings.ExitPowerOn;
            RefreshStateBeforeActionCheck.IsChecked = settings.RefreshStateBeforeAction; RefreshStateBeforeActionCheck.IsEnabled = _plugin.HasApiKey; ShowLivePreviewWarningCheck.IsChecked = settings.ShowLivePreviewWarning;
            StartupPolicyBox.ItemsSource = Enum.GetValues(typeof(StartupPolicy)); StartupPolicyBox.SelectedItem = settings.StartupPolicy; ExitPolicyBox.ItemsSource = Enum.GetValues(typeof(ExitPolicy)); ExitPolicyBox.SelectedItem = settings.ExitPolicy;
            RefreshPresetChoices(); StartupPresetBox.SelectedValue = settings.StartupPresetId ?? string.Empty;
            KeyStatusText.Text = _plugin.HasApiKey ? "A key is saved (encrypted with Windows DPAPI)." : "No key is saved."; RefreshDefaultSummary(); StatusText.Text = _plugin.Status; _loading = false;
        }

        private void CopyToSettings()
        {
            if (_loading) return; DevicesGrid.CommitEdit(); var settings = _plugin.Settings;
            settings.Devices = _devices.ToList(); settings.Presets = _presets.ToList(); settings.GameProfiles = _profiles.ToList(); settings.ManualActions = _actions.ToList();
            settings.HideLogicalDevices = HideLogicalCheck.IsChecked == true; settings.CloudFallback = FallbackCheck.IsChecked == true; settings.StartupPowerOn = StartupOnCheck.IsChecked == true; settings.ExitPowerOn = ExitOnCheck.IsChecked == true;
            settings.RefreshStateBeforeAction = _plugin.HasApiKey && RefreshStateBeforeActionCheck.IsChecked == true; settings.ShowLivePreviewWarning = ShowLivePreviewWarningCheck.IsChecked == true;
            string startup = StartupPresetBox.SelectedValue as string; settings.StartupPresetId = string.IsNullOrWhiteSpace(startup) ? null : startup;
            if (StartupPolicyBox.SelectedItem != null) settings.StartupPolicy = (StartupPolicy)StartupPolicyBox.SelectedItem; if (ExitPolicyBox.SelectedItem != null) settings.ExitPolicy = (ExitPolicy)ExitPolicyBox.SelectedItem;
        }

        private void ValidateSettings()
        {
            foreach (var preset in _presets) { if (string.IsNullOrWhiteSpace(preset.Name)) throw new ArgumentException("Every preset needs a name."); if (!SettingsValidator.IsValidRgbHex(preset.HexColor)) throw new ArgumentException("Preset '" + preset.Name + "' must use #RRGGBB."); }
            if (_profiles.GroupBy(x => x.GameCode ?? string.Empty, StringComparer.OrdinalIgnoreCase).Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1)) throw new ArgumentException("Every custom game profile needs a unique game code.");
            if (_actions.GroupBy(x => x.ActionKey ?? string.Empty, StringComparer.OrdinalIgnoreCase).Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Count() > 1)) throw new ArgumentException("Every SimHub action needs a unique immutable key.");
        }
        private void SettingChanged(object sender, RoutedEventArgs e) { CopyToSettings(); }
        private void Save_Click(object sender, RoutedEventArgs e) { try { CopyToSettings(); ValidateSettings(); _plugin.SaveSettings(); LoadSettings(); ShowStatus("Settings saved."); } catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); } }
        private void SaveKey_Click(object sender, RoutedEventArgs e) { try { _plugin.SaveApiKey(ApiKeyBox.Password); ApiKeyBox.Clear(); LoadSettings(); } catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); } }
        private void RemoveKey_Click(object sender, RoutedEventArgs e) { _plugin.RemoveApiKey(); ApiKeyBox.Clear(); LoadSettings(); }
        private async void Refresh_Click(object sender, RoutedEventArgs e) { await BusyAsync(async () => { CopyToSettings(); var found = await _plugin.Controller.DiscoverAsync(_plugin.Settings, CancellationToken.None); _devices = new ObservableCollection<DeviceSettings>(found); DevicesGrid.ItemsSource = _devices; _plugin.SaveSettings(); return "Found " + found.Count + " device(s)."; }); }
        private async void TestOn_Click(object sender, RoutedEventArgs e) { await TestPowerAsync(true); }
        private async void TestOff_Click(object sender, RoutedEventArgs e) { await TestPowerAsync(false); }
        private async Task TestPowerAsync(bool on)
        {
            CopyToSettings(); var selected = _devices.Where(x => x.Selected).ToList(); if (selected.Count == 0) { ShowStatus("Select at least one device first."); return; }
            if (MessageBox.Show("Set " + selected.Count + " selected device(s) " + (on ? "ON" : "OFF") + " and verify through cloud?", "Govee Controller test", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            await BusyAsync(async () => { foreach (var device in selected) { var result = await _plugin.Controller.SetPowerAsync(device, on, true, _plugin.Settings.CloudFallback, CancellationToken.None); if (!result.Success) throw new InvalidOperationException(device.DisplayName + ": " + result.Message); } return "Test succeeded and cloud verification passed."; });
        }
        private async Task BusyAsync(Func<Task<string>> action) { IsEnabled = false; ShowStatus("Working…"); try { ShowStatus(await action()); } catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); } finally { IsEnabled = true; } }
        private void ShowStatus(string text) { _plugin.SetStatus(text); StatusText.Text = text; }

        private void RefreshPresetChoices()
        {
            string selected = StartupPresetBox.SelectedValue as string; var choices = new List<LightPreset> { new LightPreset { Id = string.Empty, Name = "No preset — power only" } }; choices.AddRange(_presets); StartupPresetBox.ItemsSource = choices; StartupPresetBox.SelectedValue = selected ?? _plugin.Settings.StartupPresetId ?? string.Empty;
        }
        private void RefreshDefaultSummary()
        {
            var profile = _plugin.Settings.DefaultGameProfile; string preset = profile.Behavior == ProfileBehavior.ApplyPreset ? " · " + (profile.PresetName ?? "No preset") : string.Empty; DefaultProfileSummaryText.Text = "Default Game Profile · " + profile.Behavior + preset + " · " + profile.TargetSummary;
        }
        private void PersistAndReload(string message) { CopyToSettings(); ValidateSettings(); _plugin.SaveSettings(); LoadSettings(); ShowStatus(message); }

        private async void AddPreset_Click(object sender, RoutedEventArgs e) { await OpenPresetEditorAsync(new LightPreset { Name = string.Empty, HexColor = "#FFFFFF", TurnOn = true }, true); }
        private async void EditPreset_Click(object sender, RoutedEventArgs e) { var preset = PresetsGrid.SelectedItem as LightPreset; if (preset == null) { ShowStatus("Select a preset to edit."); return; } await OpenPresetEditorAsync(preset, false); }
        private async void PresetDoubleClick(object sender, MouseButtonEventArgs e) { if (PresetsGrid.SelectedItem is LightPreset) await OpenPresetEditorAsync((LightPreset)PresetsGrid.SelectedItem, false); }
        private async Task OpenPresetEditorAsync(LightPreset source, bool isNew)
        {
            DevicesGrid.CommitEdit(DataGridEditingUnit.Cell, true); DevicesGrid.CommitEdit(DataGridEditingUnit.Row, true); CopyToSettings();
            var dialog = new PresetEditorWindow(OwnerWindow(), source, _devices.ToList(), ConfigureSegmentsAsync, isNew); if (dialog.ShowDialog() != true) return;
            if (isNew) _presets.Add(dialog.Draft); else { int index = _presets.ToList().FindIndex(x => string.Equals(x.Id, source.Id, StringComparison.OrdinalIgnoreCase)); if (index >= 0) _presets[index] = dialog.Draft; }
            PersistAndReload(isNew ? "Preset and managed action added." : "Preset updated; its stable ID and action key were preserved.");
        }
        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            var preset = PresetsGrid.SelectedItem as LightPreset; if (preset == null) { ShowStatus("Select a preset to delete."); return; }
            if (_profiles.Any(x => x.PresetId == preset.Id) || _actions.Any(x => !x.IsManaged && x.PresetId == preset.Id) || string.Equals(_plugin.Settings.StartupPresetId, preset.Id, StringComparison.OrdinalIgnoreCase)) { ShowStatus("This preset is used by a game profile, startup profile, or custom action and cannot be deleted."); return; }
            if (MessageBox.Show("Delete '" + preset.Name + "' and its managed SimHub action? Existing bindings to that action will stop working.", "Delete preset", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            _presets.Remove(preset); PersistAndReload("Preset and managed action deleted.");
        }

        private void EditDefaultProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = CreateProfileDialog(_plugin.Settings.DefaultGameProfile, true, false); if (dialog.ShowDialog() != true) return; _plugin.Settings.DefaultGameProfile = dialog.Draft; PersistAndReload("Default Game Profile updated.");
        }
        private void AddProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = CreateProfileDialog(new GameProfile { Enabled = true, Behavior = ProfileBehavior.LeaveUnchanged }, false, true); if (dialog.ShowDialog() != true) return;
            if (_profiles.Any(x => string.Equals(x.GameCode, dialog.Draft.GameCode, StringComparison.OrdinalIgnoreCase))) { ShowStatus("A profile for that game already exists."); return; } _profiles.Add(dialog.Draft); PersistAndReload("Game profile added.");
        }
        private void EditProfile_Click(object sender, RoutedEventArgs e) { var profile = ProfilesGrid.SelectedItem as GameProfile; if (profile == null) { ShowStatus("Select a game profile to edit."); return; } OpenProfileEditor(profile); }
        private void ProfileDoubleClick(object sender, MouseButtonEventArgs e) { var profile = ProfilesGrid.SelectedItem as GameProfile; if (profile != null) OpenProfileEditor(profile); }
        private void OpenProfileEditor(GameProfile profile)
        {
            var dialog = CreateProfileDialog(profile, false, false); if (dialog.ShowDialog() != true) return;
            if (_profiles.Any(x => !string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(x.GameCode, dialog.Draft.GameCode, StringComparison.OrdinalIgnoreCase))) { ShowStatus("A profile for that game already exists."); return; }
            int index = _profiles.IndexOf(profile); if (index >= 0) _profiles[index] = dialog.Draft; PersistAndReload("Game profile updated.");
        }
        private GameProfileEditorWindow CreateProfileDialog(GameProfile source, bool isDefault, bool isNew)
        {
            var include = _profiles.Select(x => x.GameCode).Concat(new[] { source == null ? null : source.GameCode, _plugin.LastDetectedGame }); return new GameProfileEditorWindow(OwnerWindow(), source, SimHubGameCatalog.GetGames(include), _presets.ToList(), _devices.ToList(), _plugin.LastDetectedGame, isDefault, isNew);
        }
        private void DeleteProfile_Click(object sender, RoutedEventArgs e) { var profile = ProfilesGrid.SelectedItem as GameProfile; if (profile == null) { ShowStatus("Select a game profile to delete."); return; } if (MessageBox.Show("Delete the profile for " + profile.GameCode + "?", "Delete game profile", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return; _profiles.Remove(profile); PersistAndReload("Game profile deleted."); }

        private void AddAction_Click(object sender, RoutedEventArgs e) { OpenActionEditor(new ManualActionDefinition { Type = ManualActionType.PowerOn }, true); }
        private void EditAction_Click(object sender, RoutedEventArgs e) { var action = ActionsGrid.SelectedItem as ManualActionDefinition; if (action == null) { ShowStatus("Select an action to edit."); return; } EditSelectedAction(action); }
        private void ActionDoubleClick(object sender, MouseButtonEventArgs e) { var action = ActionsGrid.SelectedItem as ManualActionDefinition; if (action != null) EditSelectedAction(action); }
        private async void EditSelectedAction(ManualActionDefinition action)
        {
            if (action.IsManaged && action.Type == ManualActionType.SetColor) { var preset = _presets.FirstOrDefault(x => x.Id == action.PresetId); if (preset != null) await OpenPresetEditorAsync(preset, false); return; }
            OpenActionEditor(action, false);
        }
        private void OpenActionEditor(ManualActionDefinition source, bool isNew)
        {
            DevicesGrid.CommitEdit(DataGridEditingUnit.Cell, true); DevicesGrid.CommitEdit(DataGridEditingUnit.Row, true); CopyToSettings();
            var dialog = new ActionEditorWindow(OwnerWindow(), source, _presets.ToList(), _devices.ToList(), isNew); if (dialog.ShowDialog() != true) return;
            if (isNew && _actions.Any(x => string.Equals(x.ActionKey, dialog.Draft.ActionKey, StringComparison.OrdinalIgnoreCase))) { ShowStatus("That immutable action key already exists."); return; }
            if (isNew) _actions.Add(dialog.Draft); else { int index = _actions.IndexOf(source); if (index >= 0) _actions[index] = dialog.Draft; } PersistAndReload(isNew ? "Custom SimHub action added." : dialog.Draft.IsManaged ? "Managed SimHub action targets updated." : "Custom SimHub action updated; its immutable key was preserved.");
        }
        private void DeleteAction_Click(object sender, RoutedEventArgs e)
        {
            var action = ActionsGrid.SelectedItem as ManualActionDefinition; if (action == null) { ShowStatus("Select an action to delete."); return; } if (action.IsManaged) { ShowStatus("Managed actions cannot be deleted independently. Delete a color preset to remove its managed color action."); return; }
            if (MessageBox.Show("Delete " + action.RegisteredName + "? Existing SimHub bindings will stop working.", "Delete SimHub action", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return; _actions.Remove(action); PersistAndReload("Custom SimHub action deleted.");
        }

        private void ResetAllSettings_Click(object sender, RoutedEventArgs e)
        {
            const string warning = "Reset every Govee Controller setting?\n\nThis permanently removes the encrypted API key, devices/IPs, presets, game profiles, action configuration, lifecycle choices, and preferences. It cannot be undone. The lights will not be changed. Restart SimHub afterward.";
            if (MessageBox.Show(warning, "Reset all Govee Controller settings", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return; _plugin.ResetSettings(); ApiKeyBox.Clear(); LoadSettings(); ShowStatus("All plugin settings reset. Restart SimHub before configuring it again.");
        }
        private Window OwnerWindow() { return Window.GetWindow(this) ?? (Application.Current == null ? null : Application.Current.MainWindow); }

        private async Task ConfigureSegmentsAsync(DeviceSettings device, LightPreset draft)
        {
            if (!_plugin.HasApiKey || device.Transport == TransportMode.LocalOnly) { ShowStatus("Live segmented preview requires the API key saved in Step 1 and Cloud or Hybrid transport."); return; }
            if (_plugin.Settings.ShowLivePreviewWarning && !ConfirmLivePreviewWarning()) return;
            DeviceState before;
            try
            {
                before = await _plugin.Controller.GetStateAsync(device, CancellationToken.None); if (before == null) throw new InvalidOperationException("No state was returned."); var known = _plugin.Dispatcher.GetLastKnown(device);
                if (known != null) { if (!before.PowerOn.HasValue) before.PowerOn = known.PowerOn; if (!before.Brightness.HasValue) before.Brightness = known.Brightness; if (!before.Rgb.HasValue) before.Rgb = known.Rgb; if (known.SegmentColors != null && known.SegmentColors.Count > 0) before.SegmentColors = DesiredLightState.CloneAssignments(known.SegmentColors); }
            }
            catch (Exception ex) { ShowStatus("Preview was not started because the current state could not be captured: " + GoveePlugin.SafeMessage(ex)); return; }
            var existing = (draft.DeviceAppearances ?? new List<DevicePresetAppearance>()).FirstOrDefault(x => string.Equals(x.TargetId, device.TargetId, StringComparison.OrdinalIgnoreCase)); var editor = new SegmentAppearanceEditorWindow(ActiveWindow(), _plugin, device, existing, draft.HexColor, before);
            try
            {
                bool saved = editor.ShowDialog() == true; await editor.FinishAsync(); if (saved) { draft.DeviceAppearances.RemoveAll(x => string.Equals(x.TargetId, device.TargetId, StringComparison.OrdinalIgnoreCase)); if (editor.ResultAppearance != null) { draft.DeviceAppearances.Add(editor.ResultAppearance); if (string.IsNullOrWhiteSpace(draft.Name) || string.Equals(draft.Name, "New preset", StringComparison.OrdinalIgnoreCase)) draft.Name = PresetNameSuggester.ForSegmentedAppearance(device.SegmentTopology, editor.ResultAppearance.SegmentColors); } }
            }
            finally
            {
                var restored = await _plugin.Controller.RestoreStateAsync(device, before, _plugin.Settings.CloudFallback, CancellationToken.None); ShowStatus(restored.Success ? "Pre-preview light state restored." : "Preview ended, but restoration was incomplete: " + restored.Message);
            }
        }
        private bool ConfirmLivePreviewWarning()
        {
            var suppress = new CheckBox { Content = "Never warn me again", Margin = new Thickness(0, 12, 0, 12) }; var proceed = new Button { Content = "Continue with live preview", IsDefault = true, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) }; var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 5, 12, 5) }; var buttons = new WrapPanel(); buttons.Children.Add(proceed); buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 560 }; panel.Children.Add(new TextBlock { Text = "Live preview temporarily changes this light while you choose colors.", FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(new TextBlock { Text = "When the editor closes, the plugin restores readable power, brightness, and whole-light color plus any segmented pattern last commanded by this plugin.\n\nGovee does not report segmented state, so a pattern created by another app, scene, music mode, or DreamView cannot be restored exactly.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }); panel.Children.Add(suppress); panel.Children.Add(buttons);
            var window = DialogWindow("Live segmented preview warning", panel, 520); proceed.Click += (s, e) => { window.DialogResult = true; window.Close(); }; if (window.ShowDialog() != true) return false;
            if (suppress.IsChecked == true) { _plugin.Settings.ShowLivePreviewWarning = false; ShowLivePreviewWarningCheck.IsChecked = false; _plugin.SaveSettings(); } return true;
        }
        private Window ActiveWindow() { return Application.Current == null ? OwnerWindow() : Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? OwnerWindow(); }
        private Window DialogWindow(string title, object content, double minWidth) { var owner = ActiveWindow(); return new Window { Title = title, Content = content, SizeToContent = SizeToContent.WidthAndHeight, MinWidth = minWidth, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, Foreground = owner == null ? Foreground : owner.Foreground, Background = owner == null ? Background : owner.Background }; }

        private async void CompatibilityReport_Click(object sender, RoutedEventArgs e)
        {
            var device = DevicesGrid.SelectedItem as DeviceSettings; if (device == null) { ShowStatus("Select one device row first."); return; } DeviceState state = null; var observed = new List<int>(); bool testCompleted = false, stopped = false, active = false; var advertised = (device.SegmentTopology == null ? new List<int>() : device.SegmentTopology.AdvertisedSegmentIndices ?? new List<int>()).Distinct().OrderBy(x => x).ToList();
            if (_plugin.HasApiKey && device.Transport != TransportMode.LocalOnly && advertised.Count > 0 && device.SegmentTopology.SupportsSegmentedColor) { var choice = MessageBox.Show("Run an active segment-identification test?\n\nYES changes the light and tests each advertised index. Existing segmented scenes may be lost. NO creates a read-only report.", "Device compatibility test", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning); if (choice == MessageBoxResult.Cancel) return; active = choice == MessageBoxResult.Yes; }
            try
            {
                if (_plugin.HasApiKey && !string.IsNullOrWhiteSpace(device.DeviceId)) try { state = await _plugin.Controller.GetStateAsync(device, CancellationToken.None); } catch { }
                if (active) { if (state == null) throw new InvalidOperationException("The active test was not started because pre-test state could not be captured."); IsEnabled = false; var result = await _plugin.Controller.SetPowerAsync(device, true, false, _plugin.Settings.CloudFallback, CancellationToken.None); if (!result.Success) throw new InvalidOperationException(result.Message); result = await _plugin.Controller.SetBrightnessAsync(device, 35, _plugin.Settings.CloudFallback, CancellationToken.None); if (!result.Success) throw new InvalidOperationException(result.Message); foreach (int index in advertised) { result = await _plugin.Controller.SetColorAsync(device, 255, 0, 0, _plugin.Settings.CloudFallback, CancellationToken.None); if (!result.Success) throw new InvalidOperationException(result.Message); await Task.Delay(350); result = await _plugin.Controller.SetSegmentColorAsync(device, new[] { index }, 0, 0, 255, CancellationToken.None); if (!result.Success) throw new InvalidOperationException(result.Message); await Task.Delay(350); var visible = MessageBox.Show("Did advertised segment " + index + " visibly change to blue?", "Observe segment " + index, MessageBoxButton.YesNoCancel, MessageBoxImage.Question); if (visible == MessageBoxResult.Cancel) { stopped = true; break; } if (visible == MessageBoxResult.Yes) observed.Add(index); } testCompleted = !stopped; }
            }
            catch (Exception ex) { ShowStatus(GoveePlugin.SafeMessage(ex)); }
            finally { if (active && state != null) { var restored = await _plugin.Controller.RestoreStateAsync(device, state, _plugin.Settings.CloudFallback, CancellationToken.None); if (!restored.Success) MessageBox.Show("Restoration failed: " + restored.Message, "Restore warning", MessageBoxButton.OK, MessageBoxImage.Warning); } IsEnabled = true; }
            string automatic = active ? "Active test " + (testCompleted ? "completed" : "stopped") + ". Advertised: " + string.Join(",", advertised) + ". Visible response: " + (observed.Count == 0 ? "none" : string.Join(",", observed)) + "." : "Read-only capability report; no hardware commands were sent."; string notes = PromptForNotes("Compatibility observations", automatic + "\n\nDescribe which physical bar/section changed."); if (notes == null) return;
            var save = new SaveFileDialog { Title = "Save sanitized Govee compatibility report", Filter = "Text report (*.txt)|*.txt", FileName = "govee-" + SafeFilePart(device.Sku) + "-compatibility.txt", AddExtension = true }; if (save.ShowDialog() != true) return; File.WriteAllText(save.FileName, CompatibilityReportBuilder.Build(device, state, automatic + Environment.NewLine + notes, observed, active)); ShowStatus("Sanitized report saved. Review it before attaching it to GitHub."); if (MessageBox.Show("Open the GitHub compatibility issue form?", "Compatibility report", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) Process.Start("https://github.com/StormFuel/simhub-govee/issues/new?template=device-compatibility.yml&title=%5BDevice%20test%5D%20" + Uri.EscapeDataString(device.Sku ?? string.Empty));
        }
        private string PromptForNotes(string title, string prompt) { var notes = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, MinWidth = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 10) }; var ok = new Button { Content = "Continue", IsDefault = true, Padding = new Thickness(12, 5, 12, 5), HorizontalAlignment = HorizontalAlignment.Left }; var panel = new StackPanel { Margin = new Thickness(14) }; panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(notes); panel.Children.Add(ok); var window = DialogWindow(title, panel, 500); ok.Click += (s, e) => { window.DialogResult = true; window.Close(); }; return window.ShowDialog() == true ? notes.Text : null; }
        private static string SafeFilePart(string value) { var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()); string result = new string((value ?? "unknown-model").Where(c => !invalid.Contains(c) && (char.IsLetterOrDigit(c) || c == '-' || c == '_')).ToArray()); return string.IsNullOrWhiteSpace(result) ? "unknown-model" : result; }
    }
}
