using System;
using AdvancedDispatcherSystem.Core;
using UnityEngine;
using UnityModManagerNet;

namespace AdvancedDispatcherSystem.Game
{
    public static partial class Main
    {
        private static readonly ConnectionSettingsDraft connectionDraft = new ConnectionSettingsDraft();
        private static bool draftReady, advancedOpen, resetPending;
        private static float ownerCodeUntil;
        private static bool budgetEdited;
        private static float savedBudget;
        private static string settingsError = "";
        private static GUISkin styledSkin;
        private static GUIStyle cardStyle, titleStyle, hintStyle, labelStyle, primaryStyle, inputStyle, toggleStyle, foldStyle, badgeStyle;
        private static Texture2D cardTexture, accentTexture, hoverTexture;
        private sealed class SettingsView
        {
            internal bool Client, Ready, Advanced, Connected, Copied, Reset, Https, CertificateAvailable;
            internal HostSettingsState Host;
            internal string Status, Error, Address;
        }
        private static SettingsView settingsView;

        private static void Draw(UnityModManager.ModEntry entry)
        {
            EnsureSettingsStyles();
            if (!draftReady) { connectionDraft.Reset(Config); draftReady = true; }
            // Async IPC/MP state must never change GUILayout's control count
            // between Layout and Repaint (the previous source of UMM flicker).
            if (settingsView == null || Event.current.type == EventType.Layout)
            {
                bool client = Runtime?.IsMultiplayerClient == true;
                var host = Runtime?.CurrentHostSettings;
                string error = Bridge?.LastError ?? "";
                settingsView = new SettingsView {
                    Client = client, Host = host, Ready = Runtime == null || host != null,
                    Advanced = advancedOpen, Connected = Bridge?.Connected == true,
                    Reset = resetPending,
                    Https = Bridge?.RemoteLanAccess ?? Config.RemoteLanAccess,
                    CertificateAvailable = System.IO.File.Exists(CertificatePath),
                    Copied = ownerCodeUntil > Time.realtimeSinceStartup,
                    Status = Runtime?.Status ?? L("serverDisabled"), Address = Url,
                    Error = !string.IsNullOrEmpty(settingsError) ? settingsError : error.Contains("WEB_HOST_START_RETRIES_EXHAUSTED") ? L("hostFailed") : ""
                };
            }
            var view = settingsView;
            bool enabled = GUI.enabled;
            GUILayout.BeginVertical(GUILayout.MaxWidth(820));
            try
            {
                BeginSettingsCard("dispatcherPanel", view.Connected ? "serverReady" : "serverStarting");
                GUILayout.Label(L("openHelp"), hintStyle);
                GUI.enabled = enabled && view.Connected && !string.IsNullOrEmpty(secret);
                if (GUILayout.Button(L("open"), primaryStyle, GUILayout.Height(34), GUILayout.MaxWidth(310)) && Bridge?.Connected == true)
                {
                    GUIUtility.systemCopyBuffer = OwnerAccess.Create(secret);
                    ownerCodeUntil = Time.realtimeSinceStartup + 120;
                    Application.OpenURL(view.Address + "/");
                }
                GUI.enabled = enabled;
                GUILayout.Label(view.Copied ? L("ownerCode") : L("profilesHelp"), hintStyle);
                GUILayout.EndVertical();

                BeginSettingsCard("networkPanel", "hostOnlySetting");
                if (!view.Ready) GUILayout.Label(L("hostSettingsPending"), hintStyle);
                else
                {
                    GUI.enabled = enabled && !view.Client;
                    bool remote = GUILayout.Toggle(view.Client ? view.Host.remoteLanAccess : connectionDraft.Remote, L("remoteAccess"), toggleStyle);
                    if (!view.Client) connectionDraft.Remote = remote;
                    GUI.enabled = enabled;
                    GUILayout.Label(view.Client ? L("hostControlledSettings") : L("remoteHelp"), hintStyle);
                    if (!view.Client && view.Https)
                    {
                        GUILayout.Label(L("httpsFirstLogin"), hintStyle);
                        GUI.enabled = enabled && view.CertificateAvailable;
                        if (GUILayout.Button(L("openCertificate"), GUILayout.Height(28), GUILayout.MaxWidth(310)) && System.IO.File.Exists(CertificatePath))
                            Application.OpenURL(new Uri(System.IO.Path.GetFullPath(CertificatePath)).AbsoluteUri);
                        GUI.enabled = enabled;
                    }
                    if (GUILayout.Button((view.Advanced ? "▾  " : "▸  ") + L("connectionAdvanced"), foldStyle)) advancedOpen = !advancedOpen;
                    if (view.Advanced)
                    {
                        GUI.enabled = enabled && !view.Client;
                        GUILayout.Label(L("port"), labelStyle);
                        string port = GUILayout.TextField(view.Client ? view.Host.port.ToString() : connectionDraft.Port, 5, inputStyle, GUILayout.Width(120));
                        GUILayout.Label(L("publicHost"), labelStyle);
                        string host = GUILayout.TextField(view.Client ? view.Host.publicHost ?? "" : connectionDraft.PublicHost, 253, inputStyle);
                        if (!view.Client) { connectionDraft.Port = port; connectionDraft.PublicHost = host; }
                        GUI.enabled = enabled;
                        GUILayout.Label(L("publicHostHelp"), hintStyle);
                    }
                    if (!view.Client)
                    {
                        string validation = connectionDraft.Error;
                        bool changed = connectionDraft.Changed(Config);
                        GUILayout.Label(validation == null ? L(changed ? "networkPending" : "networkApplied") : L(validation), hintStyle);
                        GUI.enabled = enabled && validation == null;
                        if (GUILayout.Button(L(changed ? "applyNetwork" : "restartServer"), primaryStyle, GUILayout.Height(38), GUILayout.MinWidth(180), GUILayout.ExpandWidth(true))) ApplyConnection(entry);
                        GUI.enabled = enabled;
                    }
                }
                GUILayout.EndVertical();

                BeginSettingsCard("dispatchPanel", "hostOnlySetting");
                if (!view.Ready) GUILayout.Label(L("hostSettingsPending"), hintStyle);
                else
                {
                    GUI.enabled = enabled && !view.Client;
                    bool readOnly = GUILayout.Toggle(view.Client ? view.Host.readOnly : Config.ReadOnly, L("readonly"), toggleStyle);
                    bool controls = GUILayout.Toggle(view.Client ? view.Host.adminControls : Config.AdminControls, L("adminControls"), toggleStyle);
                    bool hidden = GUILayout.Toggle(view.Client ? view.Host.showUndiscovered : Config.ShowUndiscovered, L("hidden"), toggleStyle);
                    GUI.enabled = enabled;
                    GUILayout.Label(L(view.Client ? "hostControlledSettings" : "dispatchSettingsHelp"), hintStyle);
                    if (!view.Client && SharedSettingsEditable() && (Config.ReadOnly != readOnly || Config.AdminControls != controls || Config.ShowUndiscovered != hidden))
                    {
                        var oldReadOnly = Config.ReadOnly; var oldControls = Config.AdminControls; var oldHidden = Config.ShowUndiscovered;
                        Config.ReadOnly = readOnly; Config.AdminControls = controls; Config.ShowUndiscovered = hidden;
                        if (!SaveSettings(entry)) { Config.ReadOnly = oldReadOnly; Config.AdminControls = oldControls; Config.ShowUndiscovered = oldHidden; }
                    }
                }
                GUILayout.EndVertical();

                BeginSettingsCard("localPerformance", "thisComputer");
                GUILayout.BeginHorizontal();
                GUILayout.Label(L("budget"), labelStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(Config.CaptureBudgetMs.ToString("F1") + " " + L("milliseconds"), hintStyle, GUILayout.Width(70));
                GUILayout.EndHorizontal();
                float budget = GUILayout.HorizontalSlider(Config.CaptureBudgetMs, 0.3f, 2f);
                if (Math.Abs(budget - Config.CaptureBudgetMs) > 0.0001f)
                {
                    if (!budgetEdited) savedBudget = Config.CaptureBudgetMs;
                    budgetEdited = true;
                    Config.CaptureBudgetMs = (float)Math.Round(budget * 20) / 20f;
                }
                // Live feedback during a drag; one disk write when it ends.
                if (budgetEdited && (Event.current.rawType == EventType.MouseUp || Event.current.rawType == EventType.KeyUp))
                { if (!SaveSettings(entry)) Config.CaptureBudgetMs = savedBudget; budgetEdited = false; }
                GUILayout.Label(L("budgetHelp"), hintStyle);
                GUILayout.EndVertical();
                if (view.Reset)
                {
                    GUILayout.BeginVertical(cardStyle);
                    GUILayout.Label(L(view.Client || !view.Ready ? "restoreLocalDefaultsHelp" : "restoreDefaultsHelp"), hintStyle);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(L("confirmReset"), GUILayout.Height(29), GUILayout.MaxWidth(230))) RestoreSettings(entry, view.Client || !view.Ready);
                    if (GUILayout.Button(L("cancelReset"), GUILayout.Height(29), GUILayout.MaxWidth(130))) resetPending = false;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }
                else if (GUILayout.Button(L("restoreDefaults"), GUILayout.Height(28), GUILayout.MaxWidth(230))) resetPending = true;
                GUILayout.Label(view.Status, hintStyle);
                if (!string.IsNullOrEmpty(view.Error)) GUILayout.Label(view.Error, hintStyle);
            }
            finally { GUI.enabled = enabled; GUILayout.EndVertical(); }
        }

        private static bool SaveSettings(UnityModManager.ModEntry entry)
        {
            try { Config.Save(entry); settingsError = ""; budgetEdited = false; savedBudget = Config.CaptureBudgetMs; return true; }
            catch (Exception error) { settingsError = L("settingsSaveFailed"); Log("SETTINGS_SAVE_FAILED", error); return false; }
        }
        private static void ApplyConnection(UnityModManager.ModEntry entry)
        {
            if (!SharedSettingsEditable()) return;
            int port = Config.Port; string host = Config.PublicHost; bool remote = Config.RemoteLanAccess;
            if (!connectionDraft.Apply(Config)) return;
            if (!SaveSettings(entry)) { Config.Port = port; Config.PublicHost = host; Config.RemoteLanAccess = remote; return; }
            ownerCodeUntil = 0;
            bool wasRunning = Bridge != null;
            Stop(); if (wasRunning) Toggle(entry, true);
        }
        private static void RestoreSettings(UnityModManager.ModEntry entry, bool localOnly)
        {
            localOnly |= !SharedSettingsEditable();
            var previous = Config;
            var next = new Settings { Port = previous.Port, PublicHost = previous.PublicHost, RemoteLanAccess = previous.RemoteLanAccess,
                ReadOnly = previous.ReadOnly, AdminControls = previous.AdminControls, ShowUndiscovered = previous.ShowUndiscovered, CaptureBudgetMs = previous.CaptureBudgetMs };
            next.RestoreDefaults(localOnly); Config = next;
            if (!SaveSettings(entry)) { Config = previous; return; }
            connectionDraft.Reset(Config); resetPending = false; ownerCodeUntil = 0;
            budgetEdited = false;
            if (!localOnly && Bridge != null) { Stop(); Toggle(entry, true); }
        }
        private static bool SharedSettingsEditable() => Runtime == null || (!Runtime.IsMultiplayerClient && Runtime.CurrentHostSettings != null);
        private static string CertificatePath => System.IO.Path.Combine(Entry.Path, "Host", "data", "dispatcher-ca.cer");
        private static void BeginSettingsCard(string title, string badge)
        {
            GUILayout.BeginVertical(cardStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(L(title), titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(L(badge), badgeStyle);
            GUILayout.EndHorizontal();
        }
        private static Texture2D SettingsTexture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color); texture.Apply(); return texture;
        }
        private static void EnsureSettingsStyles()
        {
            if (styledSkin == GUI.skin && cardStyle != null) return;
            ReleaseSettingsStyles(); styledSkin = GUI.skin;
            int font = Math.Max(12, GUI.skin.label.fontSize);
            var ink = new Color(0.87f, 0.92f, 0.94f); var muted = new Color(0.66f, 0.74f, 0.77f);
            cardTexture = SettingsTexture(new Color(0.095f, 0.14f, 0.17f));
            accentTexture = SettingsTexture(new Color(0.38f, 0.85f, 0.83f)); hoverTexture = SettingsTexture(new Color(0.49f, 0.92f, 0.89f));
            cardStyle = new GUIStyle(GUI.skin.box) { border = new RectOffset(), padding = new RectOffset(14, 14, 10, 10), margin = new RectOffset(0, 0, 0, 8) };
            cardStyle.normal.background = cardTexture;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = font + 2, fontStyle = FontStyle.Bold, padding = new RectOffset(0, 0, 0, 6) }; titleStyle.normal.textColor = ink;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = font, wordWrap = true }; labelStyle.normal.textColor = ink;
            hintStyle = new GUIStyle(labelStyle) { fontSize = font - 1, margin = new RectOffset(0, 0, 3, 3) }; hintStyle.normal.textColor = muted;
            badgeStyle = new GUIStyle(hintStyle) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = font, wordWrap = true, margin = new RectOffset(0, 0, 4, 4) }; toggleStyle.normal.textColor = ink; toggleStyle.onNormal.textColor = ink;
            inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = font, padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(0, 0, 2, 5) };
            foldStyle = new GUIStyle(GUI.skin.label) { fontSize = font, fontStyle = FontStyle.Bold, padding = new RectOffset(0, 0, 6, 6) }; foldStyle.normal.textColor = muted; foldStyle.hover.textColor = ink;
            primaryStyle = new GUIStyle(GUI.skin.button) { fontSize = font, fontStyle = FontStyle.Bold, wordWrap = true, alignment = TextAnchor.MiddleCenter, border = new RectOffset(), padding = new RectOffset(14, 14, 6, 6) };
            primaryStyle.normal.background = accentTexture; primaryStyle.hover.background = hoverTexture; primaryStyle.active.background = accentTexture;
            primaryStyle.normal.textColor = primaryStyle.hover.textColor = primaryStyle.active.textColor = new Color(0.04f, 0.14f, 0.16f);
        }
        private static void ReleaseSettingsStyles()
        {
            if (cardTexture != null) UnityEngine.Object.DestroyImmediate(cardTexture);
            if (accentTexture != null) UnityEngine.Object.DestroyImmediate(accentTexture);
            if (hoverTexture != null) UnityEngine.Object.DestroyImmediate(hoverTexture);
            cardTexture = accentTexture = hoverTexture = null; cardStyle = null; styledSkin = null; settingsView = null;
        }
    }
}
