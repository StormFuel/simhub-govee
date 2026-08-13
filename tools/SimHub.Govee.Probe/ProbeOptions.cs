using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SimHub.Govee.Probe
{
    internal enum ProbeCommand
    {
        Inspect,
        Discover,
        Power,
        Brightness,
        Color,
        Segments
    }

    internal sealed class ProbeOptions
    {
        private static readonly Regex HexColor = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

        public ProbeCommand Command { get; private set; }
        public string GoveeDirectory { get; private set; }
        public string DeviceName { get; private set; }
        public bool? PowerOn { get; private set; }
        public int? Brightness { get; private set; }
        public int? Red { get; private set; }
        public int? Green { get; private set; }
        public int? Blue { get; private set; }
        public string SegmentColors { get; private set; }
        public bool? GradientEnabled { get; private set; }
        public bool ConfirmedHardwareChange { get; private set; }
        public bool ShowHelp { get; private set; }

        public static readonly string HelpText =
            "SimHub Govee compatibility probe\n\n" +
            "Usage:\n" +
            "  SimHub.Govee.Probe.exe inspect [--govee-dir PATH]\n" +
            "  SimHub.Govee.Probe.exe discover [--govee-dir PATH]\n" +
            "  SimHub.Govee.Probe.exe power --device NAME --state on|off --confirm-hardware-change\n" +
            "  SimHub.Govee.Probe.exe brightness --device NAME --value 1..100 --confirm-hardware-change\n" +
            "  SimHub.Govee.Probe.exe color --device NAME --rgb R,G,B --confirm-hardware-change\n" +
            "  SimHub.Govee.Probe.exe segments --device NAME --colors #RRGGBB,... --gradient on|off --confirm-hardware-change\n\n" +
            "Credential input:\n" +
            "  Set GOVEE_DESKTOP_API_GUID for automation, or enter it at the masked prompt.\n" +
            "  The GUID is intentionally not accepted as a command-line option.\n\n" +
            "Safe default:\n" +
            "  With no command, the probe only inspects the installed DLL and changes no lights.";

        public static ProbeOptions Parse(string[] args)
        {
            var result = new ProbeOptions
            {
                Command = ProbeCommand.Inspect,
                GoveeDirectory = GoveeApiLocator.DefaultDirectory
            };

            if (args == null || args.Length == 0)
            {
                return result;
            }

            if (args[0] == "--help" || args[0] == "-h" || args[0] == "/?")
            {
                result.ShowHelp = true;
                return result;
            }

            ProbeCommand command;
            if (!Enum.TryParse(args[0], true, out command))
            {
                throw new ProbeUsageException("Unknown command: " + args[0]);
            }

            result.Command = command;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < args.Length; i++)
            {
                string name = args[i];
                if (name == "--confirm-hardware-change")
                {
                    result.ConfirmedHardwareChange = true;
                    continue;
                }

                if (!name.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                {
                    throw new ProbeUsageException("Expected --name value but found: " + name);
                }

                if (values.ContainsKey(name))
                {
                    throw new ProbeUsageException("Duplicate option: " + name);
                }

                values.Add(name, args[++i]);
            }

            string value;
            if (values.TryGetValue("--govee-dir", out value))
            {
                result.GoveeDirectory = Path.GetFullPath(value);
                values.Remove("--govee-dir");
            }

            if (values.TryGetValue("--device", out value))
            {
                result.DeviceName = RequireText(value, "--device");
                values.Remove("--device");
            }

            switch (result.Command)
            {
                case ProbeCommand.Inspect:
                case ProbeCommand.Discover:
                    break;
                case ProbeCommand.Power:
                    result.RequireDevice();
                    result.PowerOn = ParseOnOff(Take(values, "--state"), "--state");
                    break;
                case ProbeCommand.Brightness:
                    result.RequireDevice();
                    result.Brightness = ParseRange(Take(values, "--value"), 1, 100, "--value");
                    break;
                case ProbeCommand.Color:
                    result.RequireDevice();
                    int[] rgb = ParseRgb(Take(values, "--rgb"));
                    result.Red = rgb[0];
                    result.Green = rgb[1];
                    result.Blue = rgb[2];
                    break;
                case ProbeCommand.Segments:
                    result.RequireDevice();
                    result.SegmentColors = ParseColors(Take(values, "--colors"));
                    result.GradientEnabled = ParseOnOff(Take(values, "--gradient"), "--gradient");
                    break;
            }

            if (values.Count > 0)
            {
                foreach (string unknown in values.Keys)
                {
                    throw new ProbeUsageException("Unknown or inapplicable option: " + unknown);
                }
            }

            return result;
        }

        private void RequireDevice()
        {
            if (string.IsNullOrWhiteSpace(DeviceName))
            {
                throw new ProbeUsageException("This command requires --device NAME.");
            }
        }

        private static string Take(IDictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value))
            {
                throw new ProbeUsageException("Missing required option " + key + ".");
            }

            values.Remove(key);
            return value;
        }

        private static string RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ProbeUsageException(name + " cannot be empty.");
            }

            return value.Trim();
        }

        private static bool ParseOnOff(string value, string name)
        {
            if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)) return false;
            throw new ProbeUsageException(name + " must be on or off.");
        }

        private static int ParseRange(string value, int minimum, int maximum, string name)
        {
            int parsed;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed < minimum || parsed > maximum)
            {
                throw new ProbeUsageException(name + " must be between " + minimum + " and " + maximum + ".");
            }

            return parsed;
        }

        private static int[] ParseRgb(string value)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            if (parts.Length != 3)
            {
                throw new ProbeUsageException("--rgb must contain R,G,B.");
            }

            return new[]
            {
                ParseRange(parts[0], 0, 255, "red"),
                ParseRange(parts[1], 0, 255, "green"),
                ParseRange(parts[2], 0, 255, "blue")
            };
        }

        private static string ParseColors(string value)
        {
            string[] colors = (value ?? string.Empty).Split(',');
            if (colors.Length == 0)
            {
                throw new ProbeUsageException("--colors requires at least one #RRGGBB value.");
            }

            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = colors[i].Trim().ToUpperInvariant();
                if (!HexColor.IsMatch(colors[i]))
                {
                    throw new ProbeUsageException("Invalid segment color: " + colors[i]);
                }
            }

            return string.Join(",", colors);
        }
    }

    internal sealed class ProbeUsageException : Exception
    {
        public ProbeUsageException(string message) : base(message) { }
    }
}
