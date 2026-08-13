using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SimHub.Govee.LanProbe
{
    internal static class Program
    {
        private const int DiscoveryPort = 4001;
        private const int ReplyPort = 4002;
        private const int ControlPort = 4003;
        private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");
        // The working Stream Deck LAN controller sends "reverse". Some published
        // Govee examples show "reserve", so discovery should eventually try both.
        private static readonly byte[] ScanMessage = Encoding.UTF8.GetBytes("{\"msg\":{\"cmd\":\"scan\",\"data\":{\"account_topic\":\"reverse\"}}}");
        private static readonly byte[] StatusMessage = Encoding.UTF8.GetBytes("{\"msg\":{\"cmd\":\"devStatus\",\"data\":{}}}");

        private static int Main(string[] args)
        {
            try
            {
                Options options = Options.Parse(args);
                if (options.Help)
                {
                    Console.WriteLine(Options.HelpText);
                    return 0;
                }

                PrintNetworkSummary();
                IList<DeviceInfo> devices = Discover(options.TimeoutSeconds, options.ScanTargets);
                if (devices.Count == 0)
                {
                    if (options.StatusScanLocalSubnet)
                    {
                        IList<StatusResponse> statusResponses = ScanStatus(options.TimeoutSeconds, options.ScanTargets);
                        if (statusResponses.Count > 0)
                        {
                            Console.WriteLine("Devices responding to direct devStatus: " + statusResponses.Count);
                            foreach (StatusResponse response in statusResponses)
                            {
                                Console.WriteLine("  IP=" + response.IpAddress + ", " + response.State.ToDisplayText());
                            }
                            Console.WriteLine("Read-only status scan complete. No hardware state was changed. Re-run with --ip ADDRESS to target a confirmed light.");
                            return 0;
                        }
                    }
                    Console.Error.WriteLine("No Govee LAN devices responded. Confirm LAN Control in the Govee Home mobile app and allow UDP 4001-4003.");
                    return 3;
                }

                Console.WriteLine("Discovered devices: " + devices.Count);
                foreach (DeviceInfo device in devices)
                {
                    Console.WriteLine("  " + device.ToDisplayText());
                }

                DeviceInfo target = SelectTarget(devices, options);
                Console.WriteLine("Target: " + target.ToDisplayText());
                DeviceState initial = QueryState(target.IpAddress, options.TimeoutSeconds);
                if (initial == null)
                {
                    Console.Error.WriteLine("The target was discovered, but no valid devStatus response was received. No hardware command was sent.");
                    return 4;
                }

                Console.WriteLine("Initial state: " + initial.ToDisplayText());
                if (!options.TogglePower)
                {
                    Console.WriteLine("Read-only probe complete. No hardware state was changed.");
                    return 0;
                }

                if (!options.ConfirmedHardwareChange)
                {
                    Console.Error.WriteLine("Power test requires --confirm-hardware-change.");
                    return 2;
                }

                bool temporaryPower = !initial.IsOn;
                Console.WriteLine("Power test: setting temporary state " + (temporaryPower ? "on" : "off") + ".");
                SendPower(target.IpAddress, temporaryPower);
                Thread.Sleep(options.HoldMilliseconds);

                DeviceState temporary = QueryState(target.IpAddress, options.TimeoutSeconds);
                Console.WriteLine("Observed temporary state: " + (temporary == null ? "no response" : temporary.ToDisplayText()));

                Console.WriteLine("Restoring original power state: " + (initial.IsOn ? "on" : "off") + ".");
                SendPower(target.IpAddress, initial.IsOn);
                Thread.Sleep(750);
                DeviceState restored = QueryState(target.IpAddress, options.TimeoutSeconds);
                Console.WriteLine("Restored state: " + (restored == null ? "no response" : restored.ToDisplayText()));

                if (restored == null || restored.IsOn != initial.IsOn)
                {
                    Console.Error.WriteLine("RESTORE VERIFICATION FAILED. Inspect the light immediately.");
                    return 5;
                }

                if (temporary == null || temporary.IsOn != temporaryPower)
                {
                    Console.Error.WriteLine("The original state was restored, but the temporary power state was not verified.");
                    return 6;
                }

                Console.WriteLine("PASS: power toggled and original power state restored.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("LAN probe failed: " + ex.Message);
                return 10;
            }
        }

        private static IList<DeviceInfo> Discover(int timeoutSeconds, IEnumerable<string> scanTargets)
        {
            var found = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
            using (var listener = CreateReplyListener())
            {
                listener.EnableBroadcast = true;
                listener.MulticastLoopback = false;
                var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                AddEndpoint(endpoints, MulticastAddress, DiscoveryPort);
                foreach (IPAddress broadcast in GetBroadcastAddresses()) AddEndpoint(endpoints, broadcast, DiscoveryPort);
                AddEndpoint(endpoints, IPAddress.Broadcast, DiscoveryPort);
                foreach (string target in scanTargets) AddEndpoint(endpoints, IPAddress.Parse(target), DiscoveryPort);

                foreach (string endpointText in endpoints)
                {
                    IPEndPoint endpoint = ParseEndpoint(endpointText);
                    listener.Send(ScanMessage, ScanMessage.Length, endpoint);
                    Console.WriteLine("Discovery sent to " + endpoint + ".");
                }

                Stopwatch watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
                {
                    IPEndPoint remote = null;
                    try
                    {
                        byte[] packet = listener.Receive(ref remote);
                        DeviceInfo device = DeviceInfo.Parse(packet, remote.Address);
                        if (device != null) found[device.IdentityKey] = device;
                    }
                    catch (SocketException ex)
                    {
                        if (ex.SocketErrorCode != SocketError.TimedOut) throw;
                    }
                }
            }

            return found.Values.OrderBy(d => d.IpAddress.ToString()).ToList();
        }

        private static DeviceState QueryState(IPAddress address, int timeoutSeconds)
        {
            using (var listener = CreateReplyListener())
            {
                listener.Send(StatusMessage, StatusMessage.Length, new IPEndPoint(address, ControlPort));
                Stopwatch watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
                {
                    IPEndPoint remote = null;
                    try
                    {
                        byte[] packet = listener.Receive(ref remote);
                        if (remote.Address.Equals(address))
                        {
                            DeviceState state = DeviceState.Parse(packet);
                            if (state != null) return state;
                        }
                    }
                    catch (SocketException ex)
                    {
                        if (ex.SocketErrorCode != SocketError.TimedOut) throw;
                    }
                }
            }

            return null;
        }

        private static IList<StatusResponse> ScanStatus(int timeoutSeconds, IEnumerable<string> scanTargets)
        {
            var found = new Dictionary<string, StatusResponse>(StringComparer.OrdinalIgnoreCase);
            using (var listener = CreateReplyListener())
            {
                foreach (string target in scanTargets.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    IPAddress address = IPAddress.Parse(target);
                    listener.Send(StatusMessage, StatusMessage.Length, new IPEndPoint(address, ControlPort));
                }

                Console.WriteLine("Direct devStatus sent to " + scanTargets.Distinct(StringComparer.OrdinalIgnoreCase).Count() + " local addresses.");
                Stopwatch watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
                {
                    IPEndPoint remote = null;
                    try
                    {
                        byte[] packet = listener.Receive(ref remote);
                        DeviceState state = DeviceState.Parse(packet);
                        if (state != null)
                        {
                            found[remote.Address.ToString()] = new StatusResponse(remote.Address, state);
                        }
                    }
                    catch (SocketException ex)
                    {
                        if (ex.SocketErrorCode != SocketError.TimedOut) throw;
                    }
                }
            }

            return found.Values.OrderBy(response => response.IpAddress.ToString()).ToList();
        }

        private static void SendPower(IPAddress address, bool on)
        {
            string json = "{\"msg\":{\"cmd\":\"turn\",\"data\":{\"value\":" + (on ? "1" : "0") + "}}}";
            byte[] payload = Encoding.UTF8.GetBytes(json);
            using (var sender = new UdpClient(AddressFamily.InterNetwork))
            {
                sender.Send(payload, payload.Length, new IPEndPoint(address, ControlPort));
            }
        }

        private static UdpClient CreateReplyListener()
        {
            var client = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, ReplyPort));
                client.Client.ReceiveTimeout = 250;
                return client;
            }
            catch (SocketException ex)
            {
                client.Dispose();
                throw new InvalidOperationException(
                    "Cannot bind UDP port 4002, which Govee uses for all LAN replies. Close the application currently using that port (on this computer it may be Corsair iCUE), then retry.",
                    ex);
            }
        }

        private static IList<IPAddress> GetBroadcastAddresses()
        {
            var result = new List<IPAddress>();
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                    byte[] ip = unicast.Address.GetAddressBytes();
                    byte[] mask = unicast.IPv4Mask.GetAddressBytes();
                    byte[] broadcast = new byte[4];
                    for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | (mask[i] ^ 255));
                    result.Add(new IPAddress(broadcast));
                }
            }

            return result.Distinct().ToList();
        }

        private static void PrintNetworkSummary()
        {
            Console.WriteLine("Active IPv4 interfaces:");
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                string addresses = string.Join(", ", adapter.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address + "/" + a.IPv4Mask));
                if (addresses.Length > 0) Console.WriteLine("  " + adapter.Name + ": " + addresses);
            }
        }

        private static DeviceInfo SelectTarget(IList<DeviceInfo> devices, Options options)
        {
            IEnumerable<DeviceInfo> matches = devices;
            if (!string.IsNullOrWhiteSpace(options.TargetIp))
            {
                IPAddress ip = IPAddress.Parse(options.TargetIp);
                matches = matches.Where(d => d.IpAddress.Equals(ip));
            }
            else if (!string.IsNullOrWhiteSpace(options.Sku))
            {
                matches = matches.Where(d => string.Equals(d.Sku, options.Sku, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                matches = matches.Where(d => string.Equals(d.Sku, "H6046", StringComparison.OrdinalIgnoreCase));
            }

            List<DeviceInfo> list = matches.ToList();
            if (list.Count != 1)
            {
                throw new InvalidOperationException("Expected exactly one target but found " + list.Count + ". Use --ip ADDRESS or --sku MODEL.");
            }

            return list[0];
        }

        private static void AddEndpoint(ISet<string> endpoints, IPAddress address, int port)
        {
            endpoints.Add(address + ":" + port);
        }

        private static IPEndPoint ParseEndpoint(string value)
        {
            int separator = value.LastIndexOf(':');
            return new IPEndPoint(IPAddress.Parse(value.Substring(0, separator)), int.Parse(value.Substring(separator + 1)));
        }
    }

    internal sealed class Options
    {
        public bool Help { get; private set; }
        public bool TogglePower { get; private set; }
        public bool ConfirmedHardwareChange { get; private set; }
        public string TargetIp { get; private set; }
        public string Sku { get; private set; }
        public int TimeoutSeconds { get; private set; }
        public int HoldMilliseconds { get; private set; }
        public bool ScanLocalSubnet { get; private set; }
        public bool StatusScanLocalSubnet { get; private set; }
        public IList<string> ScanTargets { get; private set; }

        public const string HelpText =
            "SimHub Govee direct LAN probe\n\n" +
            "  SimHub.Govee.LanProbe.exe [--sku H6046 | --ip ADDRESS] [--timeout 5] [--scan-local-subnet] [--status-scan-local-subnet]\n" +
            "  SimHub.Govee.LanProbe.exe --toggle-power --confirm-hardware-change [--sku H6046 | --ip ADDRESS]\n\n" +
            "The default is read-only discovery plus devStatus. Power testing requires both flags and restores the queried initial power state.";

        public static Options Parse(string[] args)
        {
            var result = new Options { Sku = "H6046", TimeoutSeconds = 5, HoldMilliseconds = 1500, ScanTargets = new List<string>() };
            for (int i = 0; i < (args == null ? 0 : args.Length); i++)
            {
                switch (args[i])
                {
                    case "--help": case "-h": result.Help = true; break;
                    case "--toggle-power": result.TogglePower = true; break;
                    case "--confirm-hardware-change": result.ConfirmedHardwareChange = true; break;
                    case "--sku": result.Sku = RequireValue(args, ref i, "--sku"); result.TargetIp = null; break;
                    case "--ip": result.TargetIp = RequireValue(args, ref i, "--ip"); result.Sku = null; IPAddress.Parse(result.TargetIp); break;
                    case "--timeout": result.TimeoutSeconds = ParseRange(RequireValue(args, ref i, "--timeout"), 1, 30, "--timeout"); break;
                    case "--hold-ms": result.HoldMilliseconds = ParseRange(RequireValue(args, ref i, "--hold-ms"), 500, 10000, "--hold-ms"); break;
                    case "--scan-ip": result.ScanTargets.Add(IPAddress.Parse(RequireValue(args, ref i, "--scan-ip")).ToString()); break;
                    case "--scan-local-subnet": result.ScanLocalSubnet = true; break;
                    case "--status-scan": result.StatusScanLocalSubnet = true; break;
                    case "--status-scan-local-subnet": result.ScanLocalSubnet = true; result.StatusScanLocalSubnet = true; break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            if (result.ScanLocalSubnet)
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null || !unicast.IPv4Mask.Equals(IPAddress.Parse("255.255.255.0"))) continue;
                        byte[] bytes = unicast.Address.GetAddressBytes();
                        for (int host = 1; host < 255; host++)
                        {
                            if (host != bytes[3]) result.ScanTargets.Add(new IPAddress(new[] { bytes[0], bytes[1], bytes[2], (byte)host }).ToString());
                        }
                    }
                }
            }
            result.ScanTargets = result.ScanTargets.Distinct().ToList();
            return result;
        }

        private static string RequireValue(string[] args, ref int index, string name)
        {
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index])) throw new ArgumentException(name + " requires a value.");
            return args[index];
        }

        private static int ParseRange(string text, int min, int max, string name)
        {
            int value;
            if (!int.TryParse(text, out value) || value < min || value > max) throw new ArgumentException(name + " must be " + min + ".." + max + ".");
            return value;
        }
    }

    internal sealed class StatusResponse
    {
        public StatusResponse(IPAddress ipAddress, DeviceState state)
        {
            IpAddress = ipAddress;
            State = state;
        }

        public IPAddress IpAddress { get; private set; }
        public DeviceState State { get; private set; }
    }

    internal sealed class DeviceInfo
    {
        public IPAddress IpAddress { get; private set; }
        public string DeviceId { get; private set; }
        public string Sku { get; private set; }
        public string BleVersion { get; private set; }
        public string WifiVersion { get; private set; }
        public string IdentityKey { get { return !string.IsNullOrEmpty(DeviceId) ? DeviceId : IpAddress.ToString(); } }

        public static DeviceInfo Parse(byte[] packet, IPAddress source)
        {
            IDictionary<string, object> data = JsonData(packet, "scan");
            if (data == null) return null;
            IPAddress ip;
            string ipText = Text(data, "ip");
            if (!IPAddress.TryParse(ipText, out ip)) ip = source;
            return new DeviceInfo
            {
                IpAddress = ip,
                DeviceId = Text(data, "device"),
                Sku = Text(data, "sku"),
                BleVersion = Text(data, "bleVersionHard"),
                WifiVersion = Text(data, "wifiVersionHard")
            };
        }

        public string ToDisplayText()
        {
            return "IP=" + IpAddress + ", SKU=" + Safe(Sku) + ", Device=" + Mask(DeviceId) + ", BLE=" + Safe(BleVersion) + ", WiFi=" + Safe(WifiVersion);
        }

        internal static IDictionary<string, object> JsonData(byte[] packet, string expectedCommand)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(Encoding.UTF8.GetString(packet)) as IDictionary<string, object>;
                var msg = root == null ? null : Value(root, "msg") as IDictionary<string, object>;
                string command = msg == null ? null : Convert.ToString(Value(msg, "cmd"));
                return string.Equals(command, expectedCommand, StringComparison.OrdinalIgnoreCase) ? Value(msg, "data") as IDictionary<string, object> : null;
            }
            catch { return null; }
        }

        internal static object Value(IDictionary<string, object> values, string key)
        {
            if (values == null) return null;
            foreach (KeyValuePair<string, object> pair in values) if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        internal static string Text(IDictionary<string, object> values, string key)
        {
            object value = Value(values, key);
            return value == null ? null : Convert.ToString(value);
        }

        private static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            return value.Length <= 5 ? "***" : "***" + value.Substring(value.Length - 5);
        }

        private static string Safe(string value) { return string.IsNullOrEmpty(value) ? "unknown" : value; }
    }

    internal sealed class DeviceState
    {
        public bool IsOn { get; private set; }
        public int Brightness { get; private set; }
        public int Red { get; private set; }
        public int Green { get; private set; }
        public int Blue { get; private set; }
        public int ColorTemperatureKelvin { get; private set; }

        public static DeviceState Parse(byte[] packet)
        {
            IDictionary<string, object> data = DeviceInfo.JsonData(packet, "devStatus");
            if (data == null) return null;
            var color = DeviceInfo.Value(data, "color") as IDictionary<string, object>;
            return new DeviceState
            {
                IsOn = Integer(data, "onOff") == 1,
                Brightness = Integer(data, "brightness"),
                Red = Integer(color, "r"), Green = Integer(color, "g"), Blue = Integer(color, "b"),
                ColorTemperatureKelvin = Integer(data, "colorTemInKelvin")
            };
        }

        public string ToDisplayText()
        {
            return "Power=" + (IsOn ? "on" : "off") + ", Brightness=" + Brightness + ", RGB=" + Red + "," + Green + "," + Blue + ", Kelvin=" + ColorTemperatureKelvin;
        }

        private static int Integer(IDictionary<string, object> values, string key)
        {
            object value = DeviceInfo.Value(values, key);
            int parsed;
            return value != null && int.TryParse(Convert.ToString(value), out parsed) ? parsed : 0;
        }
    }
}
