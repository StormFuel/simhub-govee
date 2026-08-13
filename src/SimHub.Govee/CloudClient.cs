using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SimHub.Govee
{
    public interface ICloudClient
    {
        Task<IList<DeviceSettings>> GetDevicesAsync(string apiKey, CancellationToken cancellationToken);
        Task<DeviceState> GetStateAsync(string apiKey, DeviceSettings device, CancellationToken cancellationToken);
        Task SetPowerAsync(string apiKey, DeviceSettings device, bool on, CancellationToken cancellationToken);
        Task SetBrightnessAsync(string apiKey, DeviceSettings device, int brightness, CancellationToken cancellationToken);
        Task SetColorAsync(string apiKey, DeviceSettings device, int red, int green, int blue, CancellationToken cancellationToken);
    }

    public sealed class GoveeCloudException : Exception
    {
        public int? ApiCode { get; }
        public HttpStatusCode? StatusCode { get; }
        public GoveeCloudException(string message, int? apiCode = null, HttpStatusCode? statusCode = null, Exception inner = null)
            : base(message, inner) { ApiCode = apiCode; StatusCode = statusCode; }
    }

    public sealed class GoveeCloudClient : ICloudClient, IDisposable
    {
        private const string ApiRoot = "https://openapi.api.govee.com/router/api/v1";
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        private readonly bool _ownsClient;

        public GoveeCloudClient(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            _ownsClient = httpClient == null;
        }

        public async Task<IList<DeviceSettings>> GetDevicesAsync(string apiKey, CancellationToken cancellationToken)
        {
            var root = await SendAsync(HttpMethod.Get, "/user/devices", apiKey, null, cancellationToken).ConfigureAwait(false);
            var result = new List<DeviceSettings>();
            foreach (var item in GetArray(root, "data").OfType<IDictionary<string, object>>())
            {
                var capabilities = new List<string>();
                foreach (var capability in GetArray(item, "capabilities").OfType<IDictionary<string, object>>())
                {
                    string type = GetString(capability, "type"), instance = GetString(capability, "instance");
                    capabilities.Add(string.IsNullOrWhiteSpace(type) ? instance : type + "/" + instance);
                }
                result.Add(new DeviceSettings { DeviceId = GetString(item, "device"), Sku = GetString(item, "sku"), Name = FirstNonEmpty(GetString(item, "deviceName"), GetString(item, "name")), Capabilities = capabilities });
            }
            return result;
        }

        public async Task<DeviceState> GetStateAsync(string apiKey, DeviceSettings device, CancellationToken cancellationToken)
        {
            ValidateDevice(device);
            var root = await SendAsync(HttpMethod.Post, "/device/state", apiKey, RequestPayload(device), cancellationToken).ConfigureAwait(false);
            var state = new DeviceState();
            foreach (var capability in GetArray(GetObject(root, "payload"), "capabilities").OfType<IDictionary<string, object>>())
            {
                string instance = GetString(capability, "instance");
                object value = GetValue(GetObject(capability, "state"), "value");
                if (instance == "online") state.Online = AsBool(value);
                else if (instance == "powerSwitch") state.PowerOn = AsInt(value) == 1;
                else if (instance == "brightness") state.Brightness = AsNullableInt(value);
                else if (instance == "colorRgb") state.Rgb = AsNullableInt(value);
                else if (instance == "colorTemperatureK") state.ColorTemperatureKelvin = AsNullableInt(value);
            }
            return state;
        }

        public Task SetPowerAsync(string apiKey, DeviceSettings device, bool on, CancellationToken token) => ControlAsync(apiKey, device, "devices.capabilities.on_off", "powerSwitch", on ? 1 : 0, token);
        public Task SetBrightnessAsync(string apiKey, DeviceSettings device, int brightness, CancellationToken token)
        { if (brightness < 0 || brightness > 100) throw new ArgumentOutOfRangeException(nameof(brightness)); return ControlAsync(apiKey, device, "devices.capabilities.range", "brightness", brightness, token); }
        public Task SetColorAsync(string apiKey, DeviceSettings device, int red, int green, int blue, CancellationToken token)
        { ValidateByte(red); ValidateByte(green); ValidateByte(blue); return ControlAsync(apiKey, device, "devices.capabilities.color_setting", "colorRgb", (red << 16) | (green << 8) | blue, token); }

        private async Task ControlAsync(string apiKey, DeviceSettings device, string type, string instance, object value, CancellationToken token)
        {
            ValidateDevice(device);
            var request = RequestPayload(device);
            ((Dictionary<string, object>)request["payload"])["capability"] = new Dictionary<string, object> { ["type"] = type, ["instance"] = instance, ["value"] = value };
            await SendAsync(HttpMethod.Post, "/device/control", apiKey, request, token).ConfigureAwait(false);
        }

        private async Task<IDictionary<string, object>> SendAsync(HttpMethod method, string path, string apiKey, object body, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new GoveeCloudException("A Govee Developer API key is required.");
            using (var request = new HttpRequestMessage(method, ApiRoot + path))
            {
                request.Headers.TryAddWithoutValidation("Govee-API-Key", apiKey.Trim());
                if (body != null) request.Content = new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try { response = await _http.SendAsync(request, token).ConfigureAwait(false); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { throw new GoveeCloudException("Could not reach the Govee cloud API.", inner: ex); }
                using (response)
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    IDictionary<string, object> root = null;
                    try { root = _json.DeserializeObject(text) as IDictionary<string, object>; } catch { }
                    int? code = root == null ? null : AsNullableInt(GetValue(root, "code"));
                    if (!response.IsSuccessStatusCode || (code.HasValue && code.Value != 200))
                    {
                        string message = root == null ? null : FirstNonEmpty(GetString(root, "message"), GetString(root, "msg"));
                        if (response.StatusCode == (HttpStatusCode)429) message = "Govee API rate limit reached. Wait briefly and try again.";
                        else if (response.StatusCode == HttpStatusCode.Unauthorized) message = "The Govee Developer API key was rejected.";
                        throw new GoveeCloudException(message ?? "Govee API request failed.", code, response.StatusCode);
                    }
                    if (root == null) throw new GoveeCloudException("Govee returned an unreadable response.");
                    return root;
                }
            }
        }

        private static Dictionary<string, object> RequestPayload(DeviceSettings d) => new Dictionary<string, object> { ["requestId"] = Guid.NewGuid().ToString(), ["payload"] = new Dictionary<string, object> { ["sku"] = d.Sku, ["device"] = d.DeviceId } };
        private static void ValidateDevice(DeviceSettings d) { if (d == null || string.IsNullOrWhiteSpace(d.Sku) || string.IsNullOrWhiteSpace(d.DeviceId)) throw new ArgumentException("A cloud device ID and SKU are required."); }
        private static void ValidateByte(int v) { if (v < 0 || v > 255) throw new ArgumentOutOfRangeException(nameof(v)); }
        private static string FirstNonEmpty(string a, string b) => string.IsNullOrWhiteSpace(a) ? b : a;
        private static object GetValue(IDictionary<string, object> d, string key) { object v; return d != null && d.TryGetValue(key, out v) ? v : null; }
        private static string GetString(IDictionary<string, object> d, string key) => Convert.ToString(GetValue(d, key), CultureInfo.InvariantCulture);
        private static IDictionary<string, object> GetObject(IDictionary<string, object> d, string key) => GetValue(d, key) as IDictionary<string, object>;
        private static IEnumerable GetArray(IDictionary<string, object> d, string key) => GetValue(d, key) as IEnumerable ?? Array.Empty<object>();
        private static int AsInt(object v) => Convert.ToInt32(v, CultureInfo.InvariantCulture);
        private static int? AsNullableInt(object v) { if (v == null || v is IDictionary<string, object> || v is IEnumerable && !(v is string)) return null; int n; return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out n) ? n : (int?)null; }
        private static bool AsBool(object v) { if (v is bool) return (bool)v; int n; return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out n) && n != 0; }
        public void Dispose() { if (_ownsClient) _http.Dispose(); }
    }
}
