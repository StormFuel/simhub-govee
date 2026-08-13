using System;
using SimHub.Govee.Probe;

namespace SimHub.Govee.Probe.Tests
{
    internal static class Program
    {
        private static int _assertions;

        private static int Main()
        {
            try
            {
                DefaultsAreSafe();
                HardwareConfirmationIsParsed();
                InvalidHardwareValuesAreRejected();
                CredentialsCannotBePassedAsArguments();
                GuidsAreRedacted();
                CredentialFingerprintsAreStable();
                DiscoveryJsonIsNormalized();
                ResultCodesAreMapped();
                Console.WriteLine("PASS: " + _assertions + " assertions");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
        }

        private static void DefaultsAreSafe()
        {
            ProbeOptions options = ProbeOptions.Parse(new string[0]);
            Equal(ProbeCommand.Inspect, options.Command, "default command");
            Equal(false, options.ConfirmedHardwareChange, "default hardware confirmation");
        }

        private static void HardwareConfirmationIsParsed()
        {
            ProbeOptions options = ProbeOptions.Parse(new[]
            {
                "power", "--device", "Test Bar", "--state", "off", "--confirm-hardware-change"
            });
            Equal(ProbeCommand.Power, options.Command, "power command");
            Equal("Test Bar", options.DeviceName, "device name");
            Equal(false, options.PowerOn.Value, "power state");
            Equal(true, options.ConfirmedHardwareChange, "hardware confirmation");
        }

        private static void InvalidHardwareValuesAreRejected()
        {
            Throws(() => ProbeOptions.Parse(new[] { "brightness", "--device", "Bar", "--value", "0" }), "brightness lower bound");
            Throws(() => ProbeOptions.Parse(new[] { "color", "--device", "Bar", "--rgb", "0,0,256" }), "RGB upper bound");
            Throws(() => ProbeOptions.Parse(new[] { "segments", "--device", "Bar", "--colors", "red", "--gradient", "on" }), "segment hex validation");
        }

        private static void CredentialsCannotBePassedAsArguments()
        {
            Throws(() => ProbeOptions.Parse(new[] { "discover", "--guid", "00000000-0000-0000-0000-000000000000" }), "credential CLI rejection");
        }

        private static void GuidsAreRedacted()
        {
            Equal("bad [REDACTED-GUID] value", Redactor.Sanitize("bad 01234567-89ab-cdef-0123-456789abcdef value"), "GUID redaction");
        }

        private static void CredentialFingerprintsAreStable()
        {
            string first = CredentialReader.Fingerprint("01234567-89ab-cdef-0123-456789abcdef");
            string second = CredentialReader.Fingerprint("01234567-89ab-cdef-0123-456789abcdef");
            Equal(first, second, "credential fingerprint stability");
            Equal(8, first.Length, "credential fingerprint length");
        }

        private static void DiscoveryJsonIsNormalized()
        {
            DiscoveryReport report = DiscoveryReport.Parse(
                "{\"Data\":[{\"Name\":\"Bars\",\"SegmentNums\":10,\"SkuType\":\"H6046\",\"IsLANOn\":1,\"Unexpected\":true}]}",
                TimeSpan.FromMilliseconds(12));
            Equal(true, report.IsSuccess, "discovery success");
            Equal(1, report.Devices.Count, "device count");
            Equal("H6046", report.Devices[0].SkuType, "SKU parsing");
            Equal(10, report.Devices[0].SegmentCount.Value, "segment parsing");
            Equal("Unexpected", report.ExtraFields[0], "undocumented field capture");
        }

        private static void ResultCodesAreMapped()
        {
            Equal("1001 - API GUID error", ResultCodes.Describe(1001), "known result mapping");
            Equal(true, ResultCodes.IsSuccess("0"), "success parsing");
            Equal(false, ResultCodes.IsSuccess("4000"), "failure parsing");
        }

        private static void Equal<T>(T expected, T actual, string name)
        {
            _assertions++;
            if (!object.Equals(expected, actual))
            {
                throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual);
            }
        }

        private static void Throws(Action action, string name)
        {
            _assertions++;
            try
            {
                action();
            }
            catch (ProbeUsageException)
            {
                return;
            }

            throw new InvalidOperationException(name + ": expected ProbeUsageException");
        }
    }
}
