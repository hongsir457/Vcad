using System;
using System.Security.Cryptography;
using System.Text;

namespace Vcad.Plugin.Config
{
    /// <summary>
    /// Encrypts secrets using Windows DPAPI (CurrentUser scope).
    /// On non-Windows platforms (e.g. CI test runs) the value is base64-wrapped only;
    /// production builds always target Windows.
    /// </summary>
    internal static class SecretProtector
    {
        private const string Prefix = "dpapi:";
        private const string PlainPrefix = "plain:";

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            try
            {
#if NET8_0_OR_GREATER
                if (!OperatingSystem.IsWindows())
                {
                    return PlainPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
                }
#endif
                byte[] data = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(enc);
            }
            catch
            {
                return PlainPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return null;
            try
            {
                if (stored.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    var raw = stored.Substring(Prefix.Length);
                    byte[] enc = Convert.FromBase64String(raw);
                    byte[] dec = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(dec);
                }
                if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
                {
                    var raw = stored.Substring(PlainPrefix.Length);
                    return Encoding.UTF8.GetString(Convert.FromBase64String(raw));
                }
                // legacy plain text
                return stored;
            }
            catch
            {
                return null;
            }
        }
    }
}
