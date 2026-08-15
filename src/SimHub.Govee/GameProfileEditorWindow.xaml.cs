using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SimHub.Govee
{
    public partial class GameProfileEditorWindow : Window
    {
        private readonly bool _isDefault;
        private readonly string _detectedGame;
        private readonly IList<LightPreset> _presets;
        private readonly List<Tuple<DeviceSettings, CheckBox>> _targetBoxes = new List<Tuple<DeviceSettings, CheckBox>>();
        public GameProfile Draft { get; }
        public GameProfileEditorWindow(Window owner, GameProfile source, IEnumerable<GameCatalogItem> games, IList<LightPreset> presets, IList<DeviceSettings> devices, string detectedGame, bool isDefault, bool isNew)
        {
            InitializeComponent(); Owner = owner; if (owner != null) { Foreground = owner.Foreground; Background = owner.Background; } _isDefault = isDefault; _detectedGame = detectedGame; _presets = presets ?? new List<LightPreset>(); Draft = SettingsDrafts.Profile(source);
            HeadingText.Text = isDefault ? "Edit Default Game Profile" : isNew ? "Add game profile" : "Edit game profile"; EnabledCheck.IsChecked = Draft.Enabled; EnabledCheck.IsEnabled = !isDefault;
            GameBox.ItemsSource = games; GameBox.SelectedValue = Draft.GameCode; GameBox.IsEnabled = !isDefault; DetectedButton.IsEnabled = !isDefault;
            BehaviorBox.ItemsSource = Enum.GetValues(typeof(ProfileBehavior)); BehaviorBox.SelectedItem = Draft.Behavior; PresetBox.ItemsSource = _presets; PresetBox.SelectedValue = Draft.PresetId;
            var selected = new HashSet<string>(Draft.TargetDeviceIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase); AllTargetsRadio.IsChecked = selected.Count == 0; SpecificTargetsRadio.IsChecked = selected.Count > 0;
            foreach (var device in (devices ?? new List<DeviceSettings>()).Where(x => x.Selected)) { var box = new CheckBox { Content = device.DisplayName, IsChecked = selected.Contains(device.TargetId), Margin = new Thickness(0, 2, 0, 2) }; _targetBoxes.Add(Tuple.Create(device, box)); TargetsPanel.Children.Add(box); }
            TargetsPanel.IsEnabled = selected.Count > 0; UpdateBehavior();
        }
        private void UseDetected_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(_detectedGame)) GameBox.SelectedValue = _detectedGame; else ErrorText.Text = "No running game has been detected yet."; }
        private void BehaviorChanged(object sender, SelectionChangedEventArgs e) { UpdateBehavior(); }
        private void PresetChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded && PresetBox.SelectedValue != null) BehaviorBox.SelectedItem = ProfileBehavior.ApplyPreset; }
        private void UpdateBehavior() { if (PresetBox != null) PresetBox.IsEnabled = BehaviorBox.SelectedItem is ProfileBehavior && (ProfileBehavior)BehaviorBox.SelectedItem == ProfileBehavior.ApplyPreset; }
        private void TargetModeChanged(object sender, RoutedEventArgs e) { if (TargetsPanel != null) TargetsPanel.IsEnabled = SpecificTargetsRadio.IsChecked == true; }
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty; string game = GameBox.SelectedValue as string; var behavior = BehaviorBox.SelectedItem is ProfileBehavior ? (ProfileBehavior)BehaviorBox.SelectedItem : ProfileBehavior.LeaveUnchanged; string preset = PresetBox.SelectedValue as string;
            if (!_isDefault && string.IsNullOrWhiteSpace(game)) { ErrorText.Text = "Choose a registered SimHub game."; return; }
            if (behavior == ProfileBehavior.ApplyPreset && string.IsNullOrWhiteSpace(preset)) { ErrorText.Text = "Choose a preset for Apply Preset."; return; }
            var targets = AllTargetsRadio.IsChecked == true ? new List<string>() : _targetBoxes.Where(x => x.Item2.IsChecked == true).Select(x => x.Item1.TargetId).ToList(); if (SpecificTargetsRadio.IsChecked == true && targets.Count == 0) { ErrorText.Text = "Choose at least one target or select All lights."; return; }
            Draft.GameCode = _isDefault ? string.Empty : game; Draft.DisplayName = _isDefault ? "Default Game Profile" : game; Draft.Enabled = _isDefault || EnabledCheck.IsChecked == true; Draft.Behavior = behavior; Draft.PresetId = behavior == ProfileBehavior.ApplyPreset ? preset : null; Draft.PresetName = Draft.PresetId == null ? string.Empty : (_presets.FirstOrDefault(x => x.Id == Draft.PresetId)?.Name ?? string.Empty); Draft.TargetDeviceIds = targets;
            DialogResult = true; Close();
        }
    }
}
