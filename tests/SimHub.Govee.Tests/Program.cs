using SimHub.Govee;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private static int _assertions;
    private static async Task<int> Main()
    {
        try
        {
            SettingsTests(); CredentialTests(); EmbeddedAssetTests(); LanValidationTests(); await CloudParsingTests(); await CloudErrorTests(); await ControllerTests();
            Console.WriteLine("PASS: " + _assertions + " assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
    }
    private static void SettingsTests()
    {
        var s = new PluginSettings { Devices = null }; SettingsValidator.Normalize(s); Equal(0, s.Devices.Count, "normalizes null devices");
        True(SettingsValidator.IsValidRgbHex("#00AfF1"), "accepts RGB hex"); True(!SettingsValidator.IsValidRgbHex("00AfF1"), "requires #"); True(!SettingsValidator.IsValidRgbHex("#GG0000"), "rejects invalid hex");
        var d = new DeviceSettings { Name = "Bars", Sku = "H6046" }; Equal("Bars (H6046)", d.DisplayName, "display name");
        True(new DeviceSettings { Sku = "DreamViewScenic" }.IsLogical, "logical device detection");
        Equal(ExitPolicy.Off, new PluginSettings().ExitPolicy, "safe exit default"); True(new PluginSettings().CloudFallback, "fallback default");
    }
    private static void CredentialTests()
    {
        var p = new DpapiCredentialProtector(); string encrypted = p.Protect("not-a-real-key");
        True(encrypted != "not-a-real-key", "credential encrypted"); Equal("not-a-real-key", p.Unprotect(encrypted), "DPAPI round trip");
    }
    private static void EmbeddedAssetTests()
    {
        using (var stream = typeof(DeviceSettings).Assembly.GetManifestResourceStream("SimHub.Govee.Assets.SimHubGoveeIcon.png"))
        {
            True(stream != null, "plugin icon embedded");
            True(stream != null && stream.Length > 1000, "plugin icon is nonempty");
        }
    }
    private static void LanValidationTests()
    {
        var lan = new GoveeLanClient();
        Throws<ArgumentException>(() => lan.SendPower("not-an-ip", true), "invalid IP rejected");
        Throws<ArgumentOutOfRangeException>(() => lan.SendBrightness("192.0.2.1", 101), "brightness range enforced");
        Throws<ArgumentOutOfRangeException>(() => lan.SendColor("192.0.2.1", -1, 0, 0), "color range enforced");
    }
    private static async Task CloudParsingTests()
    {
        var handler = new FakeHttpHandler(); using (var http = new HttpClient(handler)) using (var cloud = new GoveeCloudClient(http))
        {
            handler.Response = "{\"code\":200,\"data\":[{\"sku\":\"H6046\",\"device\":\"AA:BB\",\"deviceName\":\"Bars\",\"capabilities\":[{\"type\":\"devices.capabilities.on_off\",\"instance\":\"powerSwitch\"}]}]}";
            var devices = await cloud.GetDevicesAsync("test", CancellationToken.None); Equal(1, devices.Count, "cloud device count"); Equal("Bars", devices[0].Name, "cloud name"); Equal("AA:BB", devices[0].DeviceId, "cloud id"); Equal(1, devices[0].Capabilities.Count, "cloud capabilities");
            handler.Response = "{\"code\":200,\"payload\":{\"capabilities\":[{\"instance\":\"online\",\"state\":{\"value\":true}},{\"instance\":\"powerSwitch\",\"state\":{\"value\":1}},{\"instance\":\"brightness\",\"state\":{\"value\":72}},{\"instance\":\"colorRgb\",\"state\":{\"value\":16711680}}]}}";
            var state = await cloud.GetStateAsync("test", devices[0], CancellationToken.None); True(state.Online, "online parsed"); Equal(true, state.PowerOn, "power parsed"); Equal(72, state.Brightness, "brightness parsed"); Equal(16711680, state.Rgb, "RGB parsed");
            handler.Response = "{\"code\":200,\"message\":\"ok\"}"; await cloud.SetPowerAsync("test", devices[0], false, CancellationToken.None);
            True(handler.LastBody.Contains("powerSwitch") && handler.LastBody.Contains("\"value\":0"), "power request shape"); True(handler.SawKey, "API key header");
        }
    }
    private static async Task CloudErrorTests()
    {
        var handler = new FakeHttpHandler { Status = HttpStatusCode.Unauthorized, Response = "{\"code\":401,\"message\":\"secret server detail\"}" };
        using (var cloud = new GoveeCloudClient(new HttpClient(handler)))
        {
            try { await cloud.GetDevicesAsync("bad", CancellationToken.None); throw new Exception("expected unauthorized exception"); }
            catch (GoveeCloudException ex) { True(ex.Message.Contains("rejected"), "unauthorized is actionable"); Equal(HttpStatusCode.Unauthorized, ex.StatusCode, "HTTP status retained"); }
        }
        handler = new FakeHttpHandler { Status = (HttpStatusCode)429, Response = "{\"code\":429}" };
        using (var cloud = new GoveeCloudClient(new HttpClient(handler)))
        {
            try { await cloud.GetDevicesAsync("key", CancellationToken.None); throw new Exception("expected rate exception"); }
            catch (GoveeCloudException ex) { True(ex.Message.Contains("rate limit"), "rate limit is actionable"); }
        }
    }
    private static async Task ControllerTests()
    {
        var cloud = new FakeCloud(); var lan = new FakeLan(); var c = new GoveeController(lan, cloud, () => "key");
        var d = Device(); var result = await c.SetPowerAsync(d, true, false, true, CancellationToken.None); True(result.Success, "local succeeds"); Equal(1, lan.PowerCalls, "local called"); Equal(0, cloud.PowerCalls, "cloud unused");
        lan.Throw = true; result = await c.SetPowerAsync(d, false, false, true, CancellationToken.None); True(result.Success && result.UsedCloudFallback, "fallback succeeds"); Equal(1, cloud.PowerCalls, "cloud fallback called");
        d.Transport = TransportMode.LocalOnly; result = await c.SetPowerAsync(d, false, false, false, CancellationToken.None); True(!result.Success, "local-only failure returned");
        var settings = new PluginSettings { HideLogicalDevices = true, Devices = new List<DeviceSettings> { new DeviceSettings { DeviceId = "id", Selected = true, IpAddress = "1.2.3.4", Transport = TransportMode.Hybrid } } };
        cloud.Devices = new List<DeviceSettings> { new DeviceSettings { DeviceId = "id", Sku = "H6046" }, new DeviceSettings { DeviceId = "logical", Sku = "DreamViewScenic" } };
        var found = await c.DiscoverAsync(settings, CancellationToken.None); Equal(1, found.Count, "logical hidden"); True(found[0].Selected, "selection merged"); Equal("1.2.3.4", found[0].IpAddress, "IP merged");
        lan.Throw = false; found[0].Transport = TransportMode.Hybrid; settings.StartupPolicy = StartupPolicy.LeaveUnchanged; int before = lan.PowerCalls;
        await c.ApplyStartupAsync(settings, CancellationToken.None); Equal(before, lan.PowerCalls, "leave unchanged sends no startup command");
        settings.StartupPolicy = StartupPolicy.ConfiguredState; settings.StartupPowerOn = true; await c.ApplyStartupAsync(settings, CancellationToken.None); Equal(before + 1, lan.PowerCalls, "configured startup command");
        cloud.State = new DeviceState { Online = true, PowerOn = false, Brightness = 30, Rgb = 0x112233 }; lan.Commands.Clear();
        await c.CaptureInitialStatesAsync(found, CancellationToken.None); settings.ExitPolicy = ExitPolicy.RestorePrevious; await c.ApplyExitAsync(settings, CancellationToken.None);
        Equal("brightness,color,power:False", string.Join(",", lan.Commands), "restore applies power last");
    }
    private static DeviceSettings Device() => new DeviceSettings { DeviceId = "id", Sku = "H6046", IpAddress = "192.0.2.1", Transport = TransportMode.Hybrid };
    private static void True(bool value, string name) { _assertions++; if (!value) throw new Exception(name); }
    private static void Equal(object expected, object actual, string name) { _assertions++; if (!object.Equals(expected, actual)) throw new Exception(name + ": expected " + expected + ", got " + actual); }
    private static void Throws<T>(Action action, string name) where T : Exception { _assertions++; try { action(); } catch (T) { return; } throw new Exception(name); }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        public string Response, LastBody; public bool SawKey; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SawKey = request.Headers.Contains("Govee-API-Key"); LastBody = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
        }
    }
    private sealed class FakeLan : ILanClient
    {
        public bool Throw; public int PowerCalls; public List<string> Commands = new List<string>();
        public void SendPower(string ip, bool on) { PowerCalls++; Commands.Add("power:" + on); if (Throw) throw new Exception("UDP failed"); }
        public void SendBrightness(string ip, int value) { Commands.Add("brightness"); if (Throw) throw new Exception("UDP failed"); }
        public void SendColor(string ip, int r, int g, int b) { Commands.Add("color"); if (Throw) throw new Exception("UDP failed"); }
    }
    private sealed class FakeCloud : ICloudClient
    {
        public int PowerCalls; public IList<DeviceSettings> Devices = new List<DeviceSettings>(); public DeviceState State = new DeviceState { Online = true, PowerOn = true };
        public Task<IList<DeviceSettings>> GetDevicesAsync(string key, CancellationToken token) => Task.FromResult(Devices);
        public Task<DeviceState> GetStateAsync(string key, DeviceSettings d, CancellationToken token) => Task.FromResult(State);
        public Task SetPowerAsync(string key, DeviceSettings d, bool on, CancellationToken token) { PowerCalls++; return Task.CompletedTask; }
        public Task SetBrightnessAsync(string key, DeviceSettings d, int value, CancellationToken token) => Task.CompletedTask;
        public Task SetColorAsync(string key, DeviceSettings d, int r, int g, int b, CancellationToken token) => Task.CompletedTask;
    }
}
