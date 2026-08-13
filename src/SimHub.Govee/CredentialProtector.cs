using System;
using System.Security.Cryptography;
using System.Text;

namespace SimHub.Govee
{
    public interface ICredentialProtector
    {
        string Protect(string plainText);
        string Unprotect(string protectedText);
    }

    public sealed class DpapiCredentialProtector : ICredentialProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SimHub.Govee.ApiKey.v1");
        public string Protect(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText)) return null;
            byte[] bytes = Encoding.UTF8.GetBytes(plainText.Trim());
            try { return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        public string Unprotect(string protectedText)
        {
            if (string.IsNullOrWhiteSpace(protectedText)) return null;
            byte[] encrypted = Convert.FromBase64String(protectedText);
            byte[] clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(clear); }
            finally { Array.Clear(clear, 0, clear.Length); }
        }
    }
}
