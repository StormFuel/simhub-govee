using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace SimHub.Govee.Probe
{
    internal sealed class GoveeApiClient : IDisposable
    {
        private readonly string _dllPath;
        private readonly Assembly _assembly;
        private readonly Type _apiType;
        private readonly object _instance;

        private GoveeApiClient(string dllPath, Assembly assembly, Type apiType, object instance)
        {
            _dllPath = dllPath;
            _assembly = assembly;
            _apiType = apiType;
            _instance = instance;
        }

        public static GoveeApiClient Load(string directory)
        {
            string dllPath = GoveeApiLocator.FindDll(directory);
            Assembly assembly = Assembly.LoadFrom(dllPath);
            Type apiType = assembly.GetType("GoveeAPI.ConnectGovee", false, false);
            object instance = apiType == null ? null : Activator.CreateInstance(apiType);
            return new GoveeApiClient(dllPath, assembly, apiType, instance);
        }

        public CompatibilityReport InspectCompatibility()
        {
            return CompatibilityReport.Create(_dllPath, _assembly, _apiType);
        }

        public int Initialize(string guid)
        {
            return (int)Invoke("InitConnect", new[] { typeof(string) }, guid);
        }

        public DiscoveryReport Discover()
        {
            var watch = Stopwatch.StartNew();
            string raw = (string)Invoke("GetDeviceBaseInfo", Type.EmptyTypes);
            watch.Stop();
            return DiscoveryReport.Parse(raw, watch.Elapsed);
        }

        public string SetPower(string device, bool on)
        {
            return InvokeString("DeviceSwitchControl", new[] { typeof(string), typeof(int) }, device, on ? 1 : 0);
        }

        public string SetBrightness(string device, int brightness)
        {
            return InvokeString("DeviceBrightnessControl", new[] { typeof(string), typeof(int) }, device, brightness);
        }

        public string SetColor(string device, int red, int green, int blue)
        {
            return InvokeString("DeviceColorControl", new[] { typeof(string), typeof(int), typeof(int), typeof(int) }, device, red, green, blue);
        }

        public string SetSegments(string device, string colors, bool gradientEnabled)
        {
            // Vendor documentation defines 0 as no gradient and 1 as gradient enabled despite the parameter name.
            return InvokeString("DeviceSegmentsColor", new[] { typeof(string), typeof(string), typeof(int) }, device, colors, gradientEnabled ? 1 : 0);
        }

        public void Dispose()
        {
            var disposable = _instance as IDisposable;
            if (disposable != null) disposable.Dispose();
        }

        private string InvokeString(string methodName, Type[] parameterTypes, params object[] values)
        {
            var watch = Stopwatch.StartNew();
            string result = (string)Invoke(methodName, parameterTypes, values);
            watch.Stop();
            Console.WriteLine("Command latency: " + watch.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms");
            return result;
        }

        private object Invoke(string methodName, Type[] parameterTypes, params object[] values)
        {
            MethodInfo method = _apiType == null ? null : _apiType.GetMethod(methodName, parameterTypes);
            if (method == null) throw new MissingMethodException("Required Govee API method is missing: " + methodName);

            try
            {
                return method.Invoke(_instance, values);
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                throw new InvalidOperationException("Govee API call " + methodName + " failed: " + cause.Message, cause);
            }
        }
    }

    internal sealed class CompatibilityReport
    {
        private static readonly MethodContract[] RequiredMethods =
        {
            new MethodContract("InitConnect", typeof(int), typeof(string)),
            new MethodContract("GetDeviceBaseInfo", typeof(string)),
            new MethodContract("GetDeviceBaseInfoByName", typeof(string), typeof(string)),
            new MethodContract("DeviceSwitchControl", typeof(string), typeof(string), typeof(int)),
            new MethodContract("DeviceBrightnessControl", typeof(string), typeof(string), typeof(int)),
            new MethodContract("DeviceColorControl", typeof(string), typeof(string), typeof(int), typeof(int), typeof(int)),
            new MethodContract("DeviceSegmentsColor", typeof(string), typeof(string), typeof(string), typeof(int))
        };

        public string DllPath { get; private set; }
        public string AssemblyIdentity { get; private set; }
        public bool IsCompatible { get; private set; }
        public IList<string> Checks { get; private set; }

        public static CompatibilityReport Create(string path, Assembly assembly, Type apiType)
        {
            var checks = new List<string>();
            bool compatible = apiType != null;
            checks.Add((apiType != null ? "PASS" : "FAIL") + " type GoveeAPI.ConnectGovee");

            foreach (MethodContract contract in RequiredMethods)
            {
                MethodInfo method = apiType == null ? null : apiType.GetMethod(contract.Name, contract.ParameterTypes);
                bool passed = method != null && method.ReturnType == contract.ReturnType;
                compatible &= passed;
                checks.Add((passed ? "PASS" : "FAIL") + " " + contract.ToDisplayText());
            }

            return new CompatibilityReport
            {
                DllPath = path,
                AssemblyIdentity = assembly.FullName,
                IsCompatible = compatible,
                Checks = checks
            };
        }

        public string ToDisplayText()
        {
            return "DLL: " + DllPath + Environment.NewLine +
                   "Assembly: " + AssemblyIdentity + Environment.NewLine +
                   string.Join(Environment.NewLine, Checks) + Environment.NewLine +
                   "Compatibility: " + (IsCompatible ? "PASS" : "FAIL");
        }

        private sealed class MethodContract
        {
            public MethodContract(string name, Type returnType, params Type[] parameterTypes)
            {
                Name = name;
                ReturnType = returnType;
                ParameterTypes = parameterTypes;
            }

            public string Name { get; private set; }
            public Type ReturnType { get; private set; }
            public Type[] ParameterTypes { get; private set; }

            public string ToDisplayText()
            {
                return ReturnType.Name + " " + Name + "(" + string.Join(", ", ParameterTypes.Select(p => p.Name)) + ")";
            }
        }
    }

    internal sealed class DiscoveryReport
    {
        public bool IsSuccess { get; private set; }
        public string ResultDescription { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        public IList<DeviceRecord> Devices { get; private set; }
        public IList<string> ExtraFields { get; private set; }

        public static DiscoveryReport Parse(string raw, TimeSpan elapsed)
        {
            string trimmed = (raw ?? string.Empty).Trim();
            int resultCode;
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out resultCode))
            {
                return new DiscoveryReport
                {
                    IsSuccess = false,
                    ResultDescription = ResultCodes.Describe(resultCode),
                    Elapsed = elapsed,
                    Devices = new List<DeviceRecord>(),
                    ExtraFields = new List<string>()
                };
            }

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                object parsed = serializer.DeserializeObject(trimmed);
                var dictionaries = NormalizeDeviceObjects(parsed);
                var devices = new List<DeviceRecord>();
                var extras = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (IDictionary<string, object> dictionary in dictionaries)
                {
                    devices.Add(DeviceRecord.FromDictionary(dictionary));
                    foreach (string key in dictionary.Keys)
                    {
                        if (!DeviceRecord.KnownFields.Contains(key)) extras.Add(key);
                    }
                }

                return new DiscoveryReport
                {
                    IsSuccess = true,
                    ResultDescription = "Success",
                    Elapsed = elapsed,
                    Devices = devices,
                    ExtraFields = extras.ToList()
                };
            }
            catch (Exception ex)
            {
                return new DiscoveryReport
                {
                    IsSuccess = false,
                    ResultDescription = "Unrecognized discovery response (length " + trimmed.Length + "): " + Redactor.Sanitize(ex.Message),
                    Elapsed = elapsed,
                    Devices = new List<DeviceRecord>(),
                    ExtraFields = new List<string>()
                };
            }
        }

        public string ToDisplayText()
        {
            var lines = new List<string>
            {
                "Discovery: " + ResultDescription,
                "Discovery latency: " + Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms",
                "Devices: " + Devices.Count
            };

            for (int i = 0; i < Devices.Count; i++)
            {
                lines.Add("  [" + (i + 1) + "] " + Devices[i].ToDisplayText());
            }

            lines.Add(ExtraFields.Count == 0
                ? "Undocumented discovery fields: none"
                : "Undocumented discovery fields: " + string.Join(", ", ExtraFields));
            return string.Join(Environment.NewLine, lines);
        }

        private static IList<IDictionary<string, object>> NormalizeDeviceObjects(object parsed)
        {
            var result = new List<IDictionary<string, object>>();
            var dictionary = parsed as IDictionary<string, object>;
            if (dictionary != null)
            {
                object nested;
                if (TryGet(dictionary, "Data", out nested) || TryGet(dictionary, "Devices", out nested) || TryGet(dictionary, "DeviceList", out nested))
                {
                    return NormalizeDeviceObjects(nested);
                }

                result.Add(dictionary);
                return result;
            }

            var array = parsed as object[];
            if (array != null)
            {
                foreach (object item in array)
                {
                    var itemDictionary = item as IDictionary<string, object>;
                    if (itemDictionary != null) result.Add(itemDictionary);
                }

                return result;
            }

            throw new FormatException("Expected a JSON object or array.");
        }

        private static bool TryGet(IDictionary<string, object> values, string key, out object value)
        {
            foreach (KeyValuePair<string, object> pair in values)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }
    }

    internal sealed class DeviceRecord
    {
        public static readonly ISet<string> KnownFields = new HashSet<string>(
            new[] { "Name", "SegmentNums", "SkuType", "IsLANOn" }, StringComparer.OrdinalIgnoreCase);

        public string Name { get; private set; }
        public int? SegmentCount { get; private set; }
        public string SkuType { get; private set; }
        public int? IsLanOn { get; private set; }

        public static DeviceRecord FromDictionary(IDictionary<string, object> values)
        {
            return new DeviceRecord
            {
                Name = GetString(values, "Name"),
                SegmentCount = GetInt(values, "SegmentNums"),
                SkuType = GetString(values, "SkuType"),
                IsLanOn = GetInt(values, "IsLANOn")
            };
        }

        public string ToDisplayText()
        {
            return "Name=" + Safe(Name) + ", SkuType=" + Safe(SkuType) +
                   ", Segments=" + (SegmentCount.HasValue ? SegmentCount.Value.ToString(CultureInfo.InvariantCulture) : "unknown") +
                   ", LAN=" + (IsLanOn == 1 ? "on" : IsLanOn == 0 ? "off" : "unknown");
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : Redactor.Sanitize(value);
        }

        private static string GetString(IDictionary<string, object> values, string key)
        {
            object value = Find(values, key);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int? GetInt(IDictionary<string, object> values, string key)
        {
            object value = Find(values, key);
            if (value == null) return null;
            int parsed;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? (int?)parsed
                : null;
        }

        private static object Find(IDictionary<string, object> values, string key)
        {
            foreach (KeyValuePair<string, object> pair in values)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }

            return null;
        }
    }

    internal static class ResultCodes
    {
        private static readonly IDictionary<int, string> Descriptions = new Dictionary<int, string>
        {
            { 0, "Succeeded" }, { 1, "Govee Desktop is not running" }, { 100, "Program error" },
            { 101, "Initialization failure" }, { 102, "Device offline" }, { 1001, "API GUID error" },
            { 1002, "Parameter is empty" }, { 1003, "Device does not exist" }, { 1010, "Color value error" },
            { 1011, "Invalid brightness" }, { 4000, "No response / timeout; inspect the light before retrying" },
            { 4001, "Failed to send" }
        };

        public static string Describe(string result)
        {
            int code;
            return int.TryParse((result ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code)
                ? Describe(code)
                : "Unrecognized response (length " + (result == null ? 0 : result.Length) + ")";
        }

        public static string Describe(int code)
        {
            string description;
            return Descriptions.TryGetValue(code, out description)
                ? code.ToString(CultureInfo.InvariantCulture) + " - " + description
                : code.ToString(CultureInfo.InvariantCulture) + " - Unknown result";
        }

        public static bool IsSuccess(string result)
        {
            int code;
            return int.TryParse((result ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code) && code == 0;
        }
    }
}
