using System;
using System.Security.Cryptography;
using System.Text;
namespace AdvancedDispatcherSystem.Core
{
    public static class IpcAuth
    {
        public static string Nonce() { var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return BitConverter.ToString(bytes).Replace("-", ""); }
        public static string Proof(string secret, string role, string nonce)
        { using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret))) return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(role + ":" + nonce))).Replace("-", ""); }
        public static bool Equal(string left, string right)
        { if (left == null || right == null || left.Length != 64 || right.Length != 64) return false; int difference = 0; for (int i = 0; i < 64; i++) difference |= left[i] ^ right[i]; return difference == 0; }
    }
}
