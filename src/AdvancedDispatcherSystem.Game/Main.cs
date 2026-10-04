using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityModManagerNet;
using I2.Loc;

namespace AdvancedDispatcherSystem.Game
{
    public static partial class Main
    {
        public static UnityModManager.ModEntry Entry;
        public static Settings Config;
        internal static Dispatcher Runtime;
        internal static IpcBridge Bridge;
        private static GameObject root;
        private static string secret;
        // Public I2 getters call InitializeIfNeeded, which reads CurrentUser before
        // Derail Valley has created it during bootstrap. Observe the initialized
        // backing fields instead; choosing the language remains the game's job.
        private static readonly FieldInfo languageCodeField = typeof(LocalizationManager).GetField("mLanguageCode", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo languageNameField = typeof(LocalizationManager).GetField("mCurrentLanguage", BindingFlags.NonPublic | BindingFlags.Static);
        public static string LanguageCode => UseRussian ? "ru" : "en";
        public static bool Load(UnityModManager.ModEntry entry)
        {
            Entry = entry; Config = UnityModManager.ModSettings.Load<Settings>(entry);
            Config.MigrateRemoteAccess(entry.Path);
            connectionDraft.Reset(Config); draftReady = true; settingsView = null;
            budgetEdited = false; ownerCodeUntil = 0; resetPending = false;
            entry.OnToggle = Toggle; entry.OnGUI = Draw; entry.OnSaveGUI = e => { SaveSettings(e); };
            entry.OnUnload = e => { Stop(); ReleaseSettingsStyles(); draftReady = false; budgetEdited = false; return true; };
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
                    Bridge.Start(); settingsError = "";
                }
                catch (Exception e) { Entry.Logger.Error("START_FAILED " + e); Stop(); settingsError = L("hostFailed"); return false; }
            }
            else if (!active) Stop();
            return true;
        }
        private static void Stop()
        {
            ownerCodeUntil = 0;
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
            ["dispatcherPanel"] = new[] { "Web Dispatcher", "Веб-диспетчерская" },
            ["openHelp"] = new[] { "Open the map, routes and access profiles in your browser.", "Карта, маршруты и профили доступа открываются в браузере." },
            ["profilesHelp"] = new[] { "The button also copies a one-use owner code for signing in.", "Кнопка также копирует одноразовый код для входа владельца." },
            ["httpsFirstLogin"] = new[] { "First HTTPS visit: trust this host's public certificate on your device. Opening it does not install trust automatically.", "Первый вход по HTTPS: установите доверие к публичному сертификату этого хоста на своём устройстве. Открытие сертификата не устанавливает доверие автоматически." },
            ["openCertificate"] = new[] { "Open public certificate", "Открыть публичный сертификат" },
            ["serverReady"] = new[] { "Available", "Доступна" },
            ["serverStarting"] = new[] { "Not connected", "Нет подключения" },
            ["serverDisabled"] = new[] { "Web Host is not running. Enable the mod or restart the server.", "Сервер не запущен. Включите мод или перезапустите сервер." },
            ["networkPanel"] = new[] { "Connection", "Подключение" },
            ["hostOnlySetting"] = new[] { "HOST", "ХОСТ" },
            ["connectionAdvanced"] = new[] { "Port and additional address", "Порт и дополнительный адрес" },
            ["publicHostHelp"] = new[] { "Usually leave this empty. If needed, enter a DNS name or IP without https:// or a port.", "Обычно это поле можно оставить пустым. При необходимости укажите DNS-имя или IP без https:// и порта." },
            ["networkPending"] = new[] { "Unsaved changes. Applying them restarts Web Host.", "Есть несохранённые изменения. Применение перезапустит веб-сервер." },
            ["networkApplied"] = new[] { "Connection settings are saved.", "Настройки подключения сохранены." },
            ["applyNetwork"] = new[] { "Apply and restart", "Применить и перезапустить" },
            ["restartServer"] = new[] { "Restart Web Host", "Перезапустить веб-сервер" },
            ["dispatchPanel"] = new[] { "Dispatcher permissions", "Права диспетчера" },
            ["dispatchSettingsHelp"] = new[] { "Changes apply and save immediately. Read-only mode prevents all game commands. Locomotive controls are available only to the local owner.", "Изменения применяются и сохраняются сразу. Режим просмотра запрещает все игровые команды. Управление локомотивами доступно только локальному владельцу." },
            ["localPerformance"] = new[] { "Performance", "Производительность" },
            ["thisComputer"] = new[] { "THIS COMPUTER", "ЭТОТ КОМПЬЮТЕР" },
            ["budgetHelp"] = new[] { "Higher values speed up data collection but use more game-frame time. This setting affects only this computer.", "Большее значение ускоряет сбор данных, но занимает больше времени игрового кадра. Настройка действует только на этом компьютере." },
            ["invalidPort"] = new[] { "Enter a port from 1024 to 65535.", "Укажите порт от 1024 до 65535." },
            ["invalidHost"] = new[] { "Enter a valid DNS name or IP address without a protocol or port.", "Укажите корректное DNS-имя или IP без протокола и порта." },
            ["settingsSaveFailed"] = new[] { "Settings could not be saved. Check access to the mod folder and try again.", "Не удалось сохранить настройки. Проверьте доступ к папке мода и повторите попытку." },
            ["restoreDefaults"] = new[] { "Restore defaults", "Вернуть по умолчанию" },
            ["restoreDefaultsHelp"] = new[] { "Reset all mod settings and restart Web Host? Connection settings and dispatcher permissions will use their original defaults.", "Сбросить все настройки мода и перезапустить веб-сервер? Параметры подключения и права диспетчера вернутся к исходным значениям." },
            ["restoreLocalDefaultsHelp"] = new[] { "Reset this computer’s performance settings? Host settings remain read-only.", "Сбросить настройки производительности этого компьютера? Настройки хоста доступны только для чтения." },
            ["confirmReset"] = new[] { "Reset settings", "Сбросить настройки" },
            ["cancelReset"] = new[] { "Cancel", "Отмена" },
            ["open"] = new[] { "Open dispatcher", "Открыть диспетчерскую" },
            ["ownerCode"] = new[] { "Code copied. Select Owner access in the browser and paste it within 2 minutes.", "Код скопирован. Выберите «Вход владельца» в браузере и вставьте его в течение 2 минут." },
            ["hostFailed"] = new[] { "Web Host could not start. Check the port and certificate, then apply/restart. Automatic restarts have stopped.", "Web Host не удалось запустить. Проверьте порт и сертификат, затем примените настройки/перезапустите. Автоматические повторы остановлены." },
            ["port"] = new[] { "Port", "Порт" },
            ["remoteAccess"] = new[] { "Allow Remote LAN Access", "Разрешить удалённый доступ по LAN" },
            ["publicHost"] = new[] { "Public host/IP for remote access (optional)", "Внешнее имя/IP для удалённого доступа (необязательно)" },
            ["remoteHelp"] = new[] { "Allow other computers and phones on your LAN or Radmin VPN. Connection details and HTTPS help are in Web Settings → Access.", "Подключение других компьютеров и телефонов по LAN или Radmin VPN. Адреса и помощь с HTTPS — в Настройках сайта → Доступ." },
            ["adminControls"] = new[] { "Allow owner locomotive controls", "Разрешить владельцу управление локомотивами" },
            ["hostSettingsPending"] = new[] { "Waiting for host settings…", "Ожидание настроек хоста…" },
            ["hostControlledSettings"] = new[] { "These settings are controlled by the multiplayer host.", "Эти настройки управляются хостом Multiplayer." },
            ["readonly"] = new[] { "Read-only dispatcher", "Только просмотр" },
            ["hidden"] = new[] { "Show undiscovered locomotives", "Показывать необнаруженные локомотивы" },
            ["waiting"] = new[] { "Waiting for game world", "Ожидание игрового мира" },
            ["budget"] = new[] { "Data collection per frame", "Сбор данных за кадр" }
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
        // Owner pairing is deliberately loopback-only, even if a public host is configured.
        private static string Url => Bridge?.Address ?? (Config.RemoteLanAccess ? "https" : "http") + "://localhost:" + Config.Port;
    }
    public sealed class Settings : UnityModManager.ModSettings
    {
        public int Port = 7246;
        public string PublicHost = "";
        public bool RemoteLanAccess = true;
        public bool ReadOnly, ShowUndiscovered;
        public bool AdminControls = true;
        public float CaptureBudgetMs = 0.8f;
        internal void RestoreDefaults(bool localOnly)
        {
            var defaults = new Settings();
            CaptureBudgetMs = defaults.CaptureBudgetMs;
            if (localOnly) return;
            Port = defaults.Port; PublicHost = defaults.PublicHost; RemoteLanAccess = defaults.RemoteLanAccess;
            ReadOnly = defaults.ReadOnly; ShowUndiscovered = defaults.ShowUndiscovered; AdminControls = defaults.AdminControls;
        }
        internal void MigrateRemoteAccess(string directory)
        {
            string path = Path.Combine(directory, "Settings.xml");
            if (!File.Exists(path)) return;
            try
            {
                var document = new System.Xml.XmlDocument { XmlResolver = null };
                using (var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit })) document.Load(reader);
                // One-time migration only: preserve an explicitly disabled old LAN setting.
                // Old Lan/Https fields are never written back or used by the listener.
                if (document.SelectSingleNode("/Settings/RemoteLanAccess") == null && bool.TryParse(document.SelectSingleNode("/Settings/Lan")?.InnerText, out var oldLan)) RemoteLanAccess = oldLan;
            }
            catch (Exception e) when (e is System.Xml.XmlException || e is IOException) { Main.Log("SETTINGS_MIGRATION_FAILED", e); }
        }
        public override void Save(UnityModManager.ModEntry modEntry) => SaveTo(GetPath(modEntry));
        internal void SaveTo(string path)
        {
            // UMM's generic Save logs and swallows write failures. The panel
            // needs a throwing save to roll back live edits and skip restart.
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var options = new System.Xml.XmlWriterSettings {
                        Encoding = new System.Text.UTF8Encoding(false), Indent = true, CloseOutput = false, CheckCharacters = true
                    };
                    using (var writer = System.Xml.XmlWriter.Create(output, options))
                        new System.Xml.Serialization.XmlSerializer(typeof(Settings)).Serialize(writer, this);
                    output.Flush(true);
                }
                // The prior settings survive serialization errors, denied
                // access and replacement failures. Never delete them first.
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
