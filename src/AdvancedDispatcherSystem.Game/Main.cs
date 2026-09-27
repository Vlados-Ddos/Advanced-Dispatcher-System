using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityModManagerNet;
using I2.Loc;

namespace AdvancedDispatcherSystem.Game
{
    public static class Main
    {
        public static UnityModManager.ModEntry Entry;
        public static Settings Config;
        internal static Dispatcher Runtime;
        internal static IpcBridge Bridge;
        private static GameObject root;
        private static string secret, status = "";
        private static float nextStatus;
        private static string statusLanguage;
        // Public I2 getters call InitializeIfNeeded, which reads CurrentUser before
        // Derail Valley has created it during bootstrap. Observe the initialized
        // backing fields instead; choosing the language remains the game's job.
        private static readonly FieldInfo languageCodeField = typeof(LocalizationManager).GetField("mLanguageCode", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo languageNameField = typeof(LocalizationManager).GetField("mCurrentLanguage", BindingFlags.NonPublic | BindingFlags.Static);
        public static string LanguageCode => UseRussian ? "ru" : "en";
        public static bool Load(UnityModManager.ModEntry entry)
        {
            Entry = entry; Config = UnityModManager.ModSettings.Load<Settings>(entry);
            entry.OnToggle = Toggle; entry.OnGUI = Draw; entry.OnSaveGUI = e => Config.Save(e);
            entry.OnUnload = e => { Stop(); return true; };
            return true;
        }
        private static bool Toggle(UnityModManager.ModEntry entry, bool active)
        {
            if (active && root == null)
            {
                try
                {
                    Config.Port = Math.Max(1024, Math.Min(65535, Config.Port));
                    Config.CaptureBudgetMs = float.IsNaN(Config.CaptureBudgetMs) || float.IsInfinity(Config.CaptureBudgetMs) ? 0.8f : Math.Max(0.3f, Math.Min(2, Config.CaptureBudgetMs));
                    var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); secret = BitConverter.ToString(bytes).Replace("-", "");
                    Bridge = new IpcBridge(entry.Path, Config, secret);
                    root = new GameObject("Advanced Dispatcher System"); UnityEngine.Object.DontDestroyOnLoad(root);
                    Runtime = root.AddComponent<Dispatcher>();
                    Bridge.Start(); status = L("running");
                }
                catch (Exception e) { Entry.Logger.Error("START_FAILED " + e); Stop(); status = "START_FAILED"; return false; }
            }
            else if (!active) Stop();
            return true;
        }
        private static void Stop()
        {
            try { if (root != null) UnityEngine.Object.DestroyImmediate(root); }
            finally { root = null; Runtime = null; Bridge?.Dispose(); Bridge = null; }
        }
        private static readonly System.Collections.Generic.Dictionary<string, long> errorTimes = new System.Collections.Generic.Dictionary<string, long>();
        public static void Log(string code, Exception error)
        {
            long now = DateTime.UtcNow.Ticks;
            lock (errorTimes) { if (errorTimes.TryGetValue(code, out var last) && now - last < TimeSpan.TicksPerSecond * 10) return; errorTimes[code] = now; }
            Entry.Logger.Error(code + " " + error.GetType().Name + ": " + error.Message);
            Runtime?.ReportEvent("captureError", code, error.GetType().Name, "system", "error");
        }
        internal static object LoadAdapter(string file, string type)
        {
            string path = Path.Combine(Entry.Path, file);
            return Activator.CreateInstance(Assembly.LoadFrom(path).GetType(type, true));
        }
        public static string L(string key)
        {
            string[] value;
            return Text.TryGetValue(key, out value) ? value[UseRussian ? 1 : 0] : key;
        }
        // Called only by Unity-side UI/status code. I2 is the game's own language source;
        // no separate preference can diverge from a live language change.
        private static bool UseRussian
        {
            get
            {
                string code = languageCodeField?.GetValue(null) as string;
                if (string.IsNullOrEmpty(code))
                    return string.Equals(languageNameField?.GetValue(null) as string, "Russian", StringComparison.OrdinalIgnoreCase);
                return string.Equals(code, "ru", StringComparison.OrdinalIgnoreCase)
                    || code.StartsWith("ru-", StringComparison.OrdinalIgnoreCase)
                    || code.StartsWith("ru_", StringComparison.OrdinalIgnoreCase);
            }
        }
        private static readonly System.Collections.Generic.Dictionary<string, string[]> Text = new System.Collections.Generic.Dictionary<string, string[]>
        {
            ["open"] = new[] { "Open dispatcher", "Открыть диспетчерскую" },
            ["running"] = new[] { "Server starting / running", "Сервер запускается / работает" },
            ["port"] = new[] { "Port", "Порт" },
            ["lan"] = new[] { "Allow LAN access", "Разрешить доступ по LAN" },
            ["https"] = new[] { "HTTPS (LAN recommended; trust server certificate on clients)", "HTTPS (рекомендуется для LAN; доверие сертификату на клиентах)" },
            ["adminControls"] = new[] { "Enable host administrator locomotive controls", "Разрешить администратору хоста управление подвижным составом" },
            ["readonly"] = new[] { "Read-only dispatcher", "Только просмотр" },
            ["restart"] = new[] { "Apply server settings / restart", "Применить настройки / перезапустить сервер" },
            ["hidden"] = new[] { "Show undiscovered locomotives", "Показывать необнаруженные локомотивы" },
            ["advisory"] = new[] { "Normal reservations permit manual switches. Protected reservations lock their switches.", "Обычные резервы допускают ручной перевод. Защищённые блокируют стрелки." },
            ["password"] = new[] { "Accounts and passwords: web Settings. Initial login: Host/data/FIRST-LOGIN.txt", "Учётные записи и пароли: Настройки сайта. Первый вход: Host/data/FIRST-LOGIN.txt" },
            ["waiting"] = new[] { "Waiting for game world", "Ожидание игрового мира" },
            ["budget"] = new[] { "Capture budget (ms/frame)", "Бюджет чтения (мс/кадр)" }
            ,
            ["tracks"] = new[] { "Tracks", "Пути" },
            ["cars"] = new[] { "Cars", "Вагоны" },
            ["signals"] = new[] { "Signals", "Сигналы" },
            ["loading"] = new[] { "Loading", "Загрузка" }
            ,
            ["singleplayer"] = new[] { "Singleplayer", "Одиночная игра" },
            ["host"] = new[] { "Host", "Хост" },
            ["client"] = new[] { "Client", "Клиент" },
            ["multiplayer-unavailable"] = new[] { "Multiplayer unavailable", "Multiplayer недоступен" },
            ["milliseconds"] = new[] { "ms", "мс" }
        };
        private static void Draw(UnityModManager.ModEntry entry)
        {
            GUILayout.Label(L("advisory"));
            if (GUILayout.Button(L("open"))) Application.OpenURL(Url + "/#token=" + secret);
            GUILayout.Label(Url); GUILayout.Label(L("password"));
            GUILayout.BeginHorizontal(); GUILayout.Label(L("port")); int port; if (int.TryParse(GUILayout.TextField(Config.Port.ToString(), 5), out port)) Config.Port = port; GUILayout.EndHorizontal();
            Config.Lan = GUILayout.Toggle(Config.Lan, L("lan")); Config.Https = GUILayout.Toggle(Config.Https, L("https"));
            Config.ReadOnly = GUILayout.Toggle(Config.ReadOnly, L("readonly")); Config.ShowUndiscovered = GUILayout.Toggle(Config.ShowUndiscovered, L("hidden"));
            Config.AdminControls = GUILayout.Toggle(Config.AdminControls, L("adminControls"));
            GUILayout.Label(L("budget") + ": " + Config.CaptureBudgetMs.ToString("F1")); Config.CaptureBudgetMs = GUILayout.HorizontalSlider(Config.CaptureBudgetMs, 0.3f, 2f);
            if (GUILayout.Button(L("restart"))) { Stop(); Config.Save(entry); Toggle(entry, true); }
            if (statusLanguage != LanguageCode || Time.realtimeSinceStartup >= nextStatus) { statusLanguage = LanguageCode; nextStatus = Time.realtimeSinceStartup + 1; status = Runtime == null ? L("waiting") : Runtime.Status; }
            GUILayout.Label(status); if (Bridge != null && !string.IsNullOrEmpty(Bridge.LastError)) GUILayout.Label(Bridge.LastError);
        }
        private static string Url => Bridge?.Address ?? (Config.Https ? "https" : "http") + "://127.0.0.1:" + Config.Port;
    }
    public sealed class Settings : UnityModManager.ModSettings
    {
        public int Port = 7246;
        public bool Lan, Https, ReadOnly, ShowUndiscovered;
        public bool AdminControls = true;
        public float CaptureBudgetMs = 0.8f;
        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);
    }
}
