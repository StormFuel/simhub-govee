using System;

namespace SimHub.Govee.Probe
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                ProbeOptions options = ProbeOptions.Parse(args);
                if (options.ShowHelp)
                {
                    Console.WriteLine(ProbeOptions.HelpText);
                    return 0;
                }

                using (GoveeApiClient client = GoveeApiClient.Load(options.GoveeDirectory))
                {
                    CompatibilityReport compatibility = client.InspectCompatibility();
                    Console.WriteLine(compatibility.ToDisplayText());
                    if (!compatibility.IsCompatible)
                    {
                        return ExitCodes.IncompatibleApi;
                    }

                    if (options.Command == ProbeCommand.Inspect)
                    {
                        return 0;
                    }

                    ValidateHardwareConfirmation(options);

                    string guid = CredentialReader.ReadGuid();
                    try
                    {
                        Console.WriteLine("Credential fingerprint: " + CredentialReader.Fingerprint(guid) + " (non-secret; use only to compare entries)");
                        int initResult = client.Initialize(guid);
                        Console.WriteLine("Initialization: " + ResultCodes.Describe(initResult));
                        if (initResult != 0)
                        {
                            return ExitCodes.InitializationFailed;
                        }

                        if (options.Command == ProbeCommand.Discover)
                        {
                            DiscoveryReport report = client.Discover();
                            Console.WriteLine(report.ToDisplayText());
                            return report.IsSuccess ? 0 : ExitCodes.CommandFailed;
                        }

                        string result = ExecuteHardwareCommand(client, options);
                        Console.WriteLine("Command result: " + ResultCodes.Describe(result));
                        return ResultCodes.IsSuccess(result) ? 0 : ExitCodes.CommandFailed;
                    }
                    finally
                    {
                        CredentialReader.Clear(ref guid);
                    }
                }
            }
            catch (ProbeUsageException ex)
            {
                Console.Error.WriteLine("Usage error: " + ex.Message);
                Console.Error.WriteLine();
                Console.Error.WriteLine(ProbeOptions.HelpText);
                return ExitCodes.Usage;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Probe failed: " + Redactor.Sanitize(ex.Message));
                return ExitCodes.Unexpected;
            }
        }

        private static string ExecuteHardwareCommand(GoveeApiClient client, ProbeOptions options)
        {
            switch (options.Command)
            {
                case ProbeCommand.Power:
                    return client.SetPower(options.DeviceName, options.PowerOn.Value);
                case ProbeCommand.Brightness:
                    return client.SetBrightness(options.DeviceName, options.Brightness.Value);
                case ProbeCommand.Color:
                    return client.SetColor(options.DeviceName, options.Red.Value, options.Green.Value, options.Blue.Value);
                case ProbeCommand.Segments:
                    return client.SetSegments(options.DeviceName, options.SegmentColors, options.GradientEnabled.Value);
                default:
                    throw new ProbeUsageException("The selected command is not a hardware command.");
            }
        }

        private static void ValidateHardwareConfirmation(ProbeOptions options)
        {
            bool changesHardware = options.Command == ProbeCommand.Power ||
                                   options.Command == ProbeCommand.Brightness ||
                                   options.Command == ProbeCommand.Color ||
                                   options.Command == ProbeCommand.Segments;
            if (changesHardware && !options.ConfirmedHardwareChange)
            {
                throw new ProbeUsageException("Hardware-changing commands require --confirm-hardware-change.");
            }
        }
    }

    internal static class ExitCodes
    {
        public const int Usage = 2;
        public const int IncompatibleApi = 3;
        public const int InitializationFailed = 4;
        public const int CommandFailed = 5;
        public const int Unexpected = 10;
    }
}
