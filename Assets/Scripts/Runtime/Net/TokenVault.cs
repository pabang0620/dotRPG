using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [SERVER] Keeps the refresh token in PlayerPrefs encrypted with Windows DPAPI (CurrentUser scope).
    /// Outside a Windows player, or when DPAPI fails, it falls back to the plain key as before.
    /// A plain value from an older build is read once, re-stored encrypted, and the plain key is deleted.
    /// </summary>
    static class TokenVault
    {
        const string PlainKey = "dotrpg.refresh", SealedKey = "dotrpg.refresh.dpapi";
        const int UiForbidden = 0x1;
        static bool warned;

        [StructLayout(LayoutKind.Sequential)]
        struct DataBlob { public int cbData; public IntPtr pbData; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref DataBlob dataIn, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr mem);

        static bool Available => Application.platform == RuntimePlatform.WindowsPlayer;

        public static string Load()
        {
            try
            {
                string sealedText = PlayerPrefs.GetString(SealedKey, "");
                if (!string.IsNullOrEmpty(sealedText))
                {
                    string plain = Available ? Unseal(sealedText) : null;
                    if (plain == null) Warn("refresh token could not be decrypted (another Windows user or a damaged value); log in again.");
                    return plain ?? "";
                }
                string legacy = PlayerPrefs.GetString(PlainKey, "");
                if (!string.IsNullOrEmpty(legacy) && Available) Store(legacy); // move it to the encrypted key, drop the plain one
                return legacy;
            }
            catch (Exception e) { Warn("refresh token read failed: " + e.Message); return ""; }
        }

        public static void Save(string value)
        {
            try
            {
                if (string.IsNullOrEmpty(value))
                {
                    PlayerPrefs.DeleteKey(SealedKey);
                    PlayerPrefs.DeleteKey(PlainKey);
                    PlayerPrefs.Save();
                    return;
                }
                if (Available && Store(value)) return;
                PlayerPrefs.DeleteKey(SealedKey);
                PlayerPrefs.SetString(PlainKey, value);
                PlayerPrefs.Save();
            }
            catch (Exception e) { Warn("refresh token write failed: " + e.Message); }
        }

        /// <summary>Encrypts and stores the value, then deletes the plain key. False = nothing changed (caller falls back).</summary>
        static bool Store(string value)
        {
            string sealedText = Seal(value);
            if (sealedText == null) return false;
            PlayerPrefs.SetString(SealedKey, sealedText);
            PlayerPrefs.DeleteKey(PlainKey);
            PlayerPrefs.Save();
            return true;
        }

        static string Seal(string value)
        {
            var input = new DataBlob();
            var output = new DataBlob();
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            input.cbData = bytes.Length;
            input.pbData = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, input.pbData, bytes.Length);
                if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output))
                {
                    Warn("DPAPI encrypt failed (error " + Marshal.GetLastWin32Error() + "); keeping the plain key.");
                    return null;
                }
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return Convert.ToBase64String(result);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is SEHException)
            {
                Warn("DPAPI unavailable: " + e.Message);
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }

        static string Unseal(string base64)
        {
            var input = new DataBlob();
            var output = new DataBlob();
            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch (FormatException) { return null; }
            input.cbData = bytes.Length;
            input.pbData = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, input.pbData, bytes.Length);
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)) return null;
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return System.Text.Encoding.UTF8.GetString(result);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is SEHException)
            {
                Warn("DPAPI unavailable: " + e.Message);
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }

        static void Warn(string message)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[Online] " + message);
        }
    }
}
