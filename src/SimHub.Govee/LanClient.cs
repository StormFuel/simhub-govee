using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SimHub.Govee
{
    public interface ILanClient
    {
        void SendPower(string ipAddress, bool on);
        void SendBrightness(string ipAddress, int brightness);
        void SendColor(string ipAddress, int red, int green, int blue);
    }

    public sealed class GoveeLanClient : ILanClient
    {
        public void SendPower(string ipAddress, bool on) => Send(ipAddress, "{\"msg\":{\"cmd\":\"turn\",\"data\":{\"value\":" + (on ? "1" : "0") + "}}}");
        public void SendBrightness(string ipAddress, int brightness)
        {
            if (brightness < 0 || brightness > 100) throw new ArgumentOutOfRangeException(nameof(brightness));
            Send(ipAddress, "{\"msg\":{\"cmd\":\"brightness\",\"data\":{\"value\":" + brightness + "}}}");
        }
        public void SendColor(string ipAddress, int red, int green, int blue)
        {
            ValidateByte(red); ValidateByte(green); ValidateByte(blue);
            Send(ipAddress, "{\"msg\":{\"cmd\":\"colorwc\",\"data\":{\"color\":{\"r\":" + red + ",\"g\":" + green + ",\"b\":" + blue + "},\"colorTemInKelvin\":0}}}");
        }
        private static void ValidateByte(int value) { if (value < 0 || value > 255) throw new ArgumentOutOfRangeException(nameof(value)); }
        private static void Send(string ipAddress, string json)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(ipAddress, out ip) || ip.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("A valid IPv4 address is required.", nameof(ipAddress));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var udp = new UdpClient(AddressFamily.InterNetwork)) udp.Send(bytes, bytes.Length, new IPEndPoint(ip, 4003));
        }
    }
}
