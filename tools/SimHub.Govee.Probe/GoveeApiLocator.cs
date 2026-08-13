using System;
using System.Collections.Generic;
using System.IO;

namespace SimHub.Govee.Probe
{
    internal static class GoveeApiLocator
    {
        public const string DefaultDirectory = @"C:\Program Files\Govee\Govee Desktop\GoveeAPI";

        public static string FindDll(string requestedDirectory)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(requestedDirectory))
            {
                candidates.Add(requestedDirectory);
            }

            candidates.Add(DefaultDirectory);
            candidates.Add(@"C:\Program Files (x86)\Govee\Govee Desktop\GoveeAPI");

            foreach (string candidate in candidates)
            {
                string fullPath = Path.Combine(candidate, "GoveeAPI.dll");
                if (File.Exists(fullPath)) return Path.GetFullPath(fullPath);
            }

            throw new FileNotFoundException("GoveeAPI.dll was not found. Use --govee-dir to select the directory containing it.");
        }
    }
}
