using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SimHub.Govee
{
    public partial class ActionEditorWindow : Window
    {
        private readonly bool _isNew;
        private readonly bool _isManaged;
        private readonly IList<LightPreset> _presets;
        private readonly List<Tuple<DeviceSettings, CheckBox>> _targetBoxes = new List<Tuple<DeviceSettings, CheckBox>>();
        public ManualActionDefinition Draft { get; }
        public ActionEditorWindow(Window owner, ManualActionDefinition source, IList<LightPreset> presets, IList<DeviceSettings> devices, bool isNew)
        {
            InitializeComponent(); Owner = owner; if (owner != null) { Foreground = owner.Foreground; Background = owner.Background; } _isNew = isNew; _presets = presets ?? new List<LightPreset>(); Draft = SettingsDrafts.Action(source); _isManaged = Draft.IsManaged;
            HeadingText.Text = _isManaged ? "Configure managed SimHub action" : isNew ? "Add SimHub action" : "Edit SimHub action"; KeyBox.Text = Draft.ActionKey; KeyBox.IsReadOnly = !isNew || _isManaged; NameBox.Text = Draft.DisplayName; NameBox.IsReadOnly = _isManaged; TypeBox.ItemsSource = Enum.GetValues(typeof(ManualActionType)); TypeBox.SelectedItem = Draft.Type; TypeBox.IsEnabled = !_isManaged; PresetBox.ItemsSource = _presets; PresetBox.SelectedValue = Draft.PresetId;
            if (_isManaged) IntroText.Text = "This built-in action keeps its registered key, label, and behavior so existing SimHub bindings remain valid. You may choose which selected lights it controls.";
            var selected = new HashSet<string>(Draft.TargetDeviceIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase); AllTargetsRadio.IsChecked = selected.Count == 0; SpecificTargetsRadio.IsChecked = selected.Count > 0;
            foreach (var device in (devices ?? new List<DeviceSettings>()).Where(x => x.Selected)) { var box = new CheckBox { Content = device.DisplayName, IsChecked = selected.Contains(device.TargetId), Margin = new Thickness(0, 2, 0, 2) }; _targetBoxes.Add(Tuple.Create(device, box)); TargetsPanel.Children.Add(box); }
            TargetsPanel.IsEnabled = selected.Count > 0; UpdateType();
        }
        private void TypeChanged(object sender, SelectionChangedEventArgs e) { UpdateType(); }
        private void UpdateType() { if (PresetBox != null) PresetBox.IsEnabled = TypeBox.SelectedItem is ManualActionType && (ManualActionType)TypeBox.SelectedItem == ManualActionType.SetColor; }
        private void TargetModeChanged(object sender, RoutedEventArgs e) { if (TargetsPanel != null) TargetsPanel.IsEnabled = SpecificTargetsRadio.IsChecked == true; }
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty; string key = SettingsValidator.NormalizeActionKey(KeyBox.Text); var type = TypeBox.SelectedItem is ManualActionType ? (ManualActionType)TypeBox.SelectedItem : ManualActionType.PowerOn; string preset = PresetBox.SelectedValue as string;
            if (string.IsNullOrWhiteSpace(key)) { ErrorText.Text = "Enter an action key using letters, numbers, hyphen, or underscore."; return; }
            if (type == ManualActionType.SetColor && string.IsNullOrWhiteSpace(preset)) { ErrorText.Text = "Choose a color preset for Set Color."; return; }
            var targets = AllTargetsRadio.IsChecked == true ? new List<string>() : _targetBoxes.Where(x => x.Item2.IsChecked == true).Select(x => x.Item1.TargetId).ToList(); if (SpecificTargetsRadio.IsChecked == true && targets.Count == 0) { ErrorText.Text = "Choose at least one target or select All lights."; return; }
            Draft.ActionKey = _isNew ? key : Draft.ActionKey; if (!_isManaged) { Draft.DisplayName = string.IsNullOrWhiteSpace(NameBox.Text) ? Draft.ActionKey : NameBox.Text.Trim(); Draft.Type = type; Draft.PresetId = type == ManualActionType.SetColor ? preset : null; Draft.PresetName = Draft.PresetId == null ? string.Empty : (_presets.FirstOrDefault(x => x.Id == Draft.PresetId)?.Name ?? string.Empty); } Draft.TargetDeviceIds = targets; Draft.IsManaged = _isManaged;
            DialogResult = true; Close();
        }
    }
}
