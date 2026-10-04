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
    // A purpose-bound, short-lived pairing code. The IPC secret never leaves
    // the native process/Host environment or becomes a browser credential.
    public static class OwnerAccess
    {
        public const long LifetimeMs=120000;
        public static string Create(string secret,long now=0) {
            if(now==0)now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string payload=now.ToString("X",System.Globalization.CultureInfo.InvariantCulture)+"."+IpcAuth.Nonce().Substring(0,16);
            return payload+"."+IpcAuth.Proof(secret,"owner-access-v1",payload);
        }
        public static bool Validate(string secret,string code,long now=0,long notBefore=0) {
            if(string.IsNullOrEmpty(secret)||code==null||code.Length>110)return false;
            var parts=code.Split('.');long issued;
            if(parts.Length!=3||parts[1].Length!=16||!long.TryParse(parts[0],System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture,out issued))return false;
            if(now==0)now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(issued<notBefore||issued<now-LifetimeMs||issued>now+5000)return false;
            return IpcAuth.Equal(parts[2],IpcAuth.Proof(secret,"owner-access-v1",parts[0]+"."+parts[1]));
        }
    }
}
