using Microsoft.Win32;
using System;
using System.Security.Cryptography;
using System.Text;

namespace subphimv1.Services
{
    public static class CredentialService
    {
        private const string RegistryKeyPath = @"Software\LauncherAIO";
        private const string UsernameValueName = "Username";
        private const string PasswordValueName = "Password";
        // --- BẮT ĐẦU CODE MỚI ---
        private const string ServerUrlValueName = "ServerUrl";
        // --- KẾT THÚC CODE MỚI ---

        private static readonly byte[] s_entropy = { 1, 8, 3, 2, 5, 4, 7, 6 };

        public static void SaveCredentials(string username, string password)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                if (key == null) return;
                key.SetValue(UsernameValueName, username);
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[] encryptedPasswordBytes = ProtectedData.Protect(passwordBytes, s_entropy, DataProtectionScope.CurrentUser);
                string encryptedPasswordBase64 = Convert.ToBase64String(encryptedPasswordBytes);
                key.SetValue(PasswordValueName, encryptedPasswordBase64);
            }
            catch (Exception ex)
            {
            }
        }

        public static (string Username, string Password) LoadCredentials()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key == null) return (null, null);
                string username = key.GetValue(UsernameValueName) as string;
                string encryptedPasswordBase64 = key.GetValue(PasswordValueName) as string;

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(encryptedPasswordBase64))
                {
                    return (null, null);
                }

                byte[] encryptedPasswordBytes = Convert.FromBase64String(encryptedPasswordBase64);
                byte[] passwordBytes = ProtectedData.Unprotect(encryptedPasswordBytes, s_entropy, DataProtectionScope.CurrentUser);
                string password = Encoding.UTF8.GetString(passwordBytes);

                return (username, password);
            }
            catch (Exception ex)
            {
                ClearCredentials();
                return (null, null);
            }
        }

        public static void ClearCredentials()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key == null) return;

                if (key.GetValue(UsernameValueName) != null)
                {
                    key.DeleteValue(UsernameValueName);
                }
                if (key.GetValue(PasswordValueName) != null)
                {
                    key.DeleteValue(PasswordValueName);
                }
            }
            catch (Exception ex)
            {
            }
        }

        // --- BẮT ĐẦU CODE MỚI ---
        public static void SaveServerUrl(string url)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                key?.SetValue(ServerUrlValueName, url);
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nếu cần
            }
        }

        public static string LoadServerUrl()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                // Mặc định là Server 1 nếu không tìm thấy key
                return key?.GetValue(ServerUrlValueName) as string ?? "http://34.135.192.1:5000/";
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nếu cần
                return "http://34.135.192.1:5000/"; // Trả về server mặc định nếu có lỗi
            }
        }
        // --- KẾT THÚC CODE MỚI ---
    }
}