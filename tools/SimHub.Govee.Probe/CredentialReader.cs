using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace SimHub.Govee.Probe
{
    internal static class CredentialReader
    {
        public const string EnvironmentVariable = "GOVEE_DESKTOP_API_GUID";

        public static string ReadGuid()
        {
            string value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (Console.IsInputRedirected)
                {
                    throw new ProbeUsageException("No credential is available. Set " + EnvironmentVariable + " or run interactively for a masked prompt.");
                }

                Console.Write("Govee Desktop API GUID (input hidden): ");
                value = ReadMasked();
                Console.WriteLine();
            }

            Guid parsed;
            if (!Guid.TryParse(value == null ? null : value.Trim(), out parsed))
            {
                throw new ProbeUsageException("The Govee Desktop API GUID is not a valid GUID.");
            }

            return parsed.ToString("D");
        }

        public static string Fingerprint(string value)
        {
            if (string.IsNullOrEmpty(value)) return "none";
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToString(digest, 0, 4).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static void Clear(ref string value)
        {
            value = null;
        }

        private static string ReadMasked()
        {
            var builder = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Escape)
                {
                    builder.Clear();
                    throw new ProbeUsageException("Credential entry was cancelled.");
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (builder.Length > 0) builder.Length--;
                    continue;
                }

                if (!char.IsControl(key.KeyChar)) builder.Append(key.KeyChar);
            }

            return builder.ToString();
        }
    }

    internal static class Redactor
    {
        private static readonly Regex GuidPattern = new Regex(
            "(?i)\\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\b",
            RegexOptions.CultureInvariant);

        public static string Sanitize(string value)
        {
            return GuidPattern.Replace(value ?? string.Empty, "[REDACTED-GUID]");
        }
    }
}
