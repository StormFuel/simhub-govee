using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SimHub.Govee
{
    public partial class SegmentAppearanceEditorWindow : Window
    {
        private readonly GoveePlugin _plugin; private readonly DeviceSettings _device; private readonly SegmentTopology _topology; private readonly string _uniform; private readonly DeviceState _restoreState;
        private readonly Dictionary<int, TextBox> _colors = new Dictionary<int, TextBox>(); private readonly Dictionary<int, TextBox> _brightness = new Dictionary<int, TextBox>(); private readonly Dictionary<int, Button> _pickers = new Dictionary<int, Button>();
        private readonly List<DispatcherTimer> _timers = new List<DispatcherTimer>(); private readonly List<Task> _pending = new List<Task>(); private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1); private bool _suppressColorPreview; private CancellationTokenSource _testCancellation; private Task _testTask;
        public DevicePresetAppearance ResultAppearance { get; private set; }

        public SegmentAppearanceEditorWindow(Window owner, GoveePlugin plugin, DeviceSettings device, DevicePresetAppearance existing, string uniform, DeviceState restoreState)
        {
            InitializeComponent(); Owner = owner; if (owner != null) { Foreground = owner.Foreground; Background = owner.Background; } _plugin = plugin; _device = device; _topology = device.SegmentTopology; _uniform = SettingsValidator.NormalizeHex(uniform); _restoreState = restoreState.Clone(); HeadingText.Text = "Segmented appearance — " + device.DisplayName; TestSecondsBox.Text = _plugin.Settings.SegmentTestDurationSeconds.ToString(CultureInfo.InvariantCulture); Closing += (s, e) => { if (_testCancellation != null) _testCancellation.Cancel(); };
            var current = new Dictionary<int, SegmentColorAssignment>(); foreach (var assignment in existing == null ? new List<SegmentColorAssignment>() : existing.SegmentColors ?? new List<SegmentColorAssignment>()) foreach (int index in assignment.SegmentIndices ?? new List<int>()) current[index] = assignment;
            BuildZones(); foreach (int index in _topology.VerifiedSegmentIndices) BuildRow(index, current.ContainsKey(index) ? current[index] : null);
        }
        private void BuildZones()
        {
            foreach (var zone in _topology.Zones ?? new List<SegmentZone>()) { var captured = zone; var button = new Button { Content = "Choose " + zone.Name + " color…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 7, 5) }; button.Click += async (s, e) => { string color = ChooseColor(_uniform); if (color == null) return; _suppressColorPreview = true; try { foreach (int index in captured.SegmentIndices) if (_colors.ContainsKey(index)) _colors[index].Text = color; } finally { _suppressColorPreview = false; } await Track(PreviewColor(captured.SegmentIndices, color)); }; ZoneButtonsPanel.Children.Add(button); }
        }
        private void BuildRow(int index, SegmentColorAssignment existing)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.Children.Add(new TextBlock { Text = SegmentLabel(index), VerticalAlignment = VerticalAlignment.Center });
            var colorBox = new TextBox { Text = existing == null ? _uniform : SettingsValidator.NormalizeHex(existing.HexColor), Width = 90, HorizontalAlignment = HorizontalAlignment.Left }; Grid.SetColumn(colorBox, 1); row.Children.Add(colorBox);
            var picker = new Button { Content = "Pick…", Width = 60, Height = 23, HorizontalAlignment = HorizontalAlignment.Left }; SetPicker(picker, colorBox.Text); Grid.SetColumn(picker, 2); row.Children.Add(picker); _colors[index] = colorBox; _pickers[index] = picker;
            var colorTimer = NewTimer(); colorTimer.Tick += async (s, e) => { colorTimer.Stop(); if (SettingsValidator.IsValidRgbHex(colorBox.Text.Trim())) await Track(PreviewColor(new[] { index }, colorBox.Text.Trim())); };
            colorBox.TextChanged += (s, e) => { SetPicker(picker, colorBox.Text); colorTimer.Stop(); if (!_suppressColorPreview && SettingsValidator.IsValidRgbHex(colorBox.Text.Trim())) colorTimer.Start(); };
            colorBox.KeyDown += async (s, e) => { if (e.Key != System.Windows.Input.Key.Enter || !SettingsValidator.IsValidRgbHex(colorBox.Text.Trim())) return; e.Handled = true; colorTimer.Stop(); await Track(PreviewColor(new[] { index }, colorBox.Text.Trim())); };
            picker.Click += async (s, e) => { string color = ChooseColor(colorBox.Text); if (color == null) return; colorTimer.Stop(); _suppressColorPreview = true; try { colorBox.Text = color; } finally { _suppressColorPreview = false; } await Track(PreviewColor(new[] { index }, color)); };
            var brightnessPanel = new StackPanel { Orientation = Orientation.Horizontal }; var brightnessBox = new TextBox { Text = existing != null && existing.Brightness.HasValue ? existing.Brightness.Value.ToString(CultureInfo.InvariantCulture) : string.Empty, Width = 45, Margin = new Thickness(0, 0, 8, 0), IsEnabled = _topology.SupportsSegmentedBrightness, ToolTip = "Optional 0–100; typing and paste are supported" }; var slider = new Slider { Minimum = 0, Maximum = 100, Value = existing != null && existing.Brightness.HasValue ? existing.Brightness.Value : 100, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 145, IsEnabled = _topology.SupportsSegmentedBrightness }; brightnessPanel.Children.Add(brightnessBox); brightnessPanel.Children.Add(slider); Grid.SetColumn(brightnessPanel, 3); row.Children.Add(brightnessPanel); _brightness[index] = brightnessBox;
            var brightnessTimer = NewTimer(); bool syncing = false; brightnessTimer.Tick += async (s, e) => { brightnessTimer.Stop(); int value; if (int.TryParse(brightnessBox.Text, out value) && value >= 0 && value <= 100) await Track(PreviewBrightness(new[] { index }, value)); };
            brightnessBox.TextChanged += (s, e) => { int value; brightnessTimer.Stop(); if (!int.TryParse(brightnessBox.Text, out value) || value < 0 || value > 100) return; syncing = true; slider.Value = value; syncing = false; brightnessTimer.Start(); };
            slider.ValueChanged += (s, e) => { if (!syncing) brightnessBox.Text = ((int)Math.Round(slider.Value)).ToString(CultureInfo.InvariantCulture); };
            brightnessBox.KeyDown += async (s, e) => { int value; if (e.Key != System.Windows.Input.Key.Enter || !int.TryParse(brightnessBox.Text, out value) || value < 0 || value > 100) return; e.Handled = true; brightnessTimer.Stop(); await Track(PreviewBrightness(new[] { index }, value)); };
            RowsPanel.Children.Add(row);
        }
        private DispatcherTimer NewTimer() { var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; _timers.Add(timer); return timer; }
        private async Task Track(Task task) { _pending.Add(task); try { await task; } finally { _pending.Remove(task); } }
        private async Task PreviewColor(IEnumerable<int> indices, string hex)
        {
            await _gate.WaitAsync(); try { var segments = indices.Distinct().OrderBy(x => x).ToList(); int rgb = SettingsValidator.HexToRgb(hex); StatusText.Text = "Sending color preview to segment(s) " + string.Join(",", segments) + "…"; var result = await _plugin.Controller.SetSegmentColorAsync(_device, segments, rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255, CancellationToken.None); StatusText.Text = result.Success ? "Color preview accepted. Confirm the physical result." : "Preview failed: " + result.Message; } catch (Exception ex) { StatusText.Text = "Preview failed: " + GoveePlugin.SafeMessage(ex); } finally { _gate.Release(); }
        }
        private async Task PreviewBrightness(IEnumerable<int> indices, int value)
        {
            await _gate.WaitAsync(); try { var segments = indices.Distinct().OrderBy(x => x).ToList(); StatusText.Text = "Sending brightness preview…"; var result = await _plugin.Controller.SetSegmentBrightnessAsync(_device, segments, value, CancellationToken.None); StatusText.Text = result.Success ? "Brightness preview accepted. Confirm the physical result." : "Preview failed: " + result.Message; } catch (Exception ex) { StatusText.Text = "Preview failed: " + GoveePlugin.SafeMessage(ex); } finally { _gate.Release(); }
        }
        private void TestSecondsChanged(object sender, TextChangedEventArgs e)
        {
            if (TestButton == null) return; int seconds; TestButton.Content = int.TryParse(TestSecondsBox.Text, out seconds) && seconds >= 1 && seconds <= 120 ? "Test for " + seconds + " seconds" : "Test appearance";
        }
        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            if (_testCancellation != null) { _testCancellation.Cancel(); return; }
            int seconds; List<SegmentColorAssignment> assignments;
            if (!int.TryParse(TestSecondsBox.Text, out seconds) || seconds < 1 || seconds > 120) { ErrorText.Text = "Test duration must be between 1 and 120 seconds."; return; }
            if (!TryReadAssignments(out assignments)) return;
            _plugin.Settings.SegmentTestDurationSeconds = seconds; _plugin.SaveSettings();
            foreach (var timer in _timers) timer.Stop(); if (_pending.Count > 0) await Task.WhenAll(_pending.ToArray());
            _testCancellation = new CancellationTokenSource(); _testTask = RunTimedTestAsync(assignments, seconds, _testCancellation.Token);
            SetTestUi(true);
            try { await _testTask; }
            catch (OperationCanceledException) { StatusText.Text = "Test stopped. Restoring the pre-editor state…"; }
            catch (Exception ex) { ErrorText.Text = "Test failed: " + GoveePlugin.SafeMessage(ex); }
            finally
            {
                try { var restored = await _plugin.Controller.RestoreStateAsync(_device, _restoreState, _plugin.Settings.CloudFallback, CancellationToken.None); StatusText.Text = restored.Success ? "Test finished; the pre-editor light state was restored." : "Test finished, but restoration was incomplete: " + restored.Message; }
                catch (Exception ex) { StatusText.Text = "Test finished, but restoration failed: " + GoveePlugin.SafeMessage(ex); }
                _testCancellation.Dispose(); _testCancellation = null; _testTask = null; SetTestUi(false);
            }
        }
        private async Task RunTimedTestAsync(IList<SegmentColorAssignment> assignments, int seconds, CancellationToken token)
        {
            await _gate.WaitAsync(token); try
            {
                var result = await _plugin.Controller.SetPowerAsync(_device, true, false, _plugin.Settings.CloudFallback, token); if (!result.Success) throw new InvalidOperationException(result.Message);
                foreach (var group in assignments.GroupBy(x => SettingsValidator.NormalizeHex(x.HexColor))) { int rgb = SettingsValidator.HexToRgb(group.Key); result = await _plugin.Controller.SetSegmentColorAsync(_device, group.SelectMany(x => x.SegmentIndices).Distinct().OrderBy(x => x).ToList(), rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255, token); if (!result.Success) throw new InvalidOperationException(result.Message); }
                if (_topology.SupportsSegmentedBrightness) foreach (var group in assignments.Where(x => x.Brightness.HasValue).GroupBy(x => x.Brightness.Value)) { result = await _plugin.Controller.SetSegmentBrightnessAsync(_device, group.SelectMany(x => x.SegmentIndices).Distinct().OrderBy(x => x).ToList(), group.Key, token); if (!result.Success) throw new InvalidOperationException(result.Message); }
            }
            finally { _gate.Release(); }
            for (int remaining = seconds; remaining > 0; remaining--) { StatusText.Text = "Testing complete appearance — " + remaining + " second" + (remaining == 1 ? string.Empty : "s") + " remaining…"; await Task.Delay(1000, token); }
        }
        private void SetTestUi(bool testing)
        {
            RowsPanel.IsEnabled = !testing; ZoneButtonsPanel.IsEnabled = !testing; TestSecondsBox.IsEnabled = !testing; SaveButton.IsEnabled = !testing; UniformButton.IsEnabled = !testing; TestButton.Content = testing ? "Stop test" : "Test for " + TestSecondsBox.Text + " seconds";
        }
        private bool TryReadAssignments(out List<SegmentColorAssignment> assignments)
        {
            assignments = null; ErrorText.Text = string.Empty;
            foreach (var box in _colors.Values) if (!SettingsValidator.IsValidRgbHex(box.Text.Trim())) { ErrorText.Text = "Every segment color must use #RRGGBB."; return false; }
            foreach (var box in _brightness.Values.Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.Text))) { int value; if (!int.TryParse(box.Text, out value) || value < 0 || value > 100) { ErrorText.Text = "Brightness must be blank or 0–100."; return false; } }
            assignments = _colors.Select(x => { int value; return new SegmentColorAssignment { Name = SegmentLabel(x.Key), SegmentIndices = new List<int> { x.Key }, HexColor = x.Value.Text.Trim().ToUpperInvariant(), Brightness = int.TryParse(_brightness[x.Key].Text, out value) ? value : (int?)null }; }).ToList(); return true;
        }
        public async Task FinishAsync() { foreach (var timer in _timers) timer.Stop(); if (_testCancellation != null) _testCancellation.Cancel(); if (_testTask != null) try { await _testTask; } catch (OperationCanceledException) { } if (_pending.Count > 0) await Task.WhenAll(_pending.ToArray()); _gate.Dispose(); }
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            List<SegmentColorAssignment> assignments; if (!TryReadAssignments(out assignments)) return;
            ResultAppearance = new DevicePresetAppearance { TargetId = _device.TargetId, UseSegmentedColor = true, SegmentColors = assignments }; DialogResult = true; Close();
        }
        private void Uniform_Click(object sender, RoutedEventArgs e) { ResultAppearance = null; DialogResult = true; Close(); }
        private string SegmentLabel(int index) { var zone = (_topology.Zones ?? new List<SegmentZone>()).FirstOrDefault(x => x.SegmentIndices != null && x.SegmentIndices.Contains(index)); return (zone == null ? "Segment" : zone.Name + " · segment") + " " + index; }
        private static string ChooseColor(string initial) { using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true }) { try { int rgb = SettingsValidator.HexToRgb(initial); dialog.Color = System.Drawing.Color.FromArgb(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255); } catch { dialog.Color = System.Drawing.Color.White; } return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? string.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B) : null; } }
        private static void SetPicker(Button button, string hex) { if (!SettingsValidator.IsValidRgbHex(hex)) { button.Background = Brushes.LightGray; button.Foreground = Brushes.Black; return; } int rgb = SettingsValidator.HexToRgb(hex), r = rgb >> 16 & 255, g = rgb >> 8 & 255, b = rgb & 255; button.Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b)); button.Foreground = r * 299 + g * 587 + b * 114 >= 128000 ? Brushes.Black : Brushes.White; }
    }
}
