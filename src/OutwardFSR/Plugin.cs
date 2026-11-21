using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FidelityFX.FSR3;
using UnityEngine;

namespace OutwardFSR
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.outwardfsr.mod";
        public const string Name = "Outward FSR";
        public const string Version = "1.0.3";

        internal static ManualLogSource Log;

        // --- 1. Generale ---
        internal static ConfigEntry<bool> ScalingEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<UpscalerType> Upscaler;
        internal static ConfigEntry<QualityPreset> FsrQuality;
        internal static ConfigEntry<float> FsrSharpness;
        internal static ConfigEntry<string> ActiveInfo;
        internal static ConfigEntry<bool> ShowToast;

        // --- 2. Avanzate ---
        internal static ConfigEntry<bool> FullResPostFx;
        internal static ConfigEntry<bool> FsrSharpen;
        internal static ConfigEntry<float> RenderScale;
        internal static ConfigEntry<DepthSource> FsrDepthSource;
        internal static ConfigEntry<bool> Fsr4UseDepth;
        internal static ConfigEntry<bool> AtmosphereStab;
        internal static ConfigEntry<bool> FsrReactive;
        internal static ConfigEntry<bool> FsrAutoTcr;
        internal static ConfigEntry<MipBiasMode> MipBiasSetting;
        internal static ConfigEntry<bool> FrameStatsEnabled;

        // --- 3. Diagnostica ---
        internal static ConfigEntry<bool> FsrDebugView;
        internal static ConfigEntry<bool> VerboseStats;
        internal static ConfigEntry<KeyboardShortcut> DumpKey;
        internal static ConfigEntry<KeyboardShortcut> SnapshotKey;
        internal static ConfigEntry<KeyboardShortcut> CaptureKey;

        internal static ConfigEntry<MenuLanguage> Language;

        // Le chiavi nel file .cfg sono in inglese (stabili); il menu F5 mostra nomi e descrizioni nella lingua scelta.
        private const string General = "1. General";
        private const string Advanced = "2. Advanced";
        private const string Diagnostics = "3. Diagnostics";

        private sealed class LocalizedEntry
        {
            public ConfigurationManagerAttributes Attrs;
            public string NameEn, NameIt, DescEn, DescIt, CatEn, CatIt;
        }
        private static readonly System.Collections.Generic.List<LocalizedEntry> Localized = new System.Collections.Generic.List<LocalizedEntry>();

        /// <summary>true se il menu e i messaggi a schermo vanno mostrati in italiano.</summary>
        internal static bool It
        {
            get
            {
                var l = Language != null ? Language.Value : MenuLanguage.Auto;
                if (l == MenuLanguage.Auto)
                    return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it";
                return l == MenuLanguage.Italiano;
            }
        }

        internal static string L(string en, string it) => It ? it : en;

        private static string CategoryIt(string section) =>
            section == General ? "1. Generale" : section == Advanced ? "2. Avanzate" : "3. Diagnostica";

        private ConfigEntry<T> Bind<T>(string section, string key, string nameIt, T def, string descEn, string descIt,
            int order, AcceptableValueBase range = null, bool advanced = false, System.Action<ConfigEntryBase> drawer = null)
        {
            var attrs = new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced };
            if (drawer != null) { attrs.CustomDrawer = drawer; attrs.HideDefaultButton = true; }
            Localized.Add(new LocalizedEntry { Attrs = attrs, NameEn = key, NameIt = nameIt, DescEn = descEn, DescIt = descIt, CatEn = section, CatIt = CategoryIt(section) });
            return Config.Bind(section, key, def, new ConfigDescription(descEn, range, attrs));
        }

        /// <summary>Aggiorna nomi, descrizioni e sezioni del menu F5 (basta riaprire il menu).</summary>
        private static void ApplyLanguage()
        {
            bool it = It;
            foreach (var e in Localized)
            {
                e.Attrs.DispName = it ? e.NameIt : e.NameEn;
                e.Attrs.Description = it ? e.DescIt : e.DescEn;
                e.Attrs.Category = it ? e.CatIt : e.CatEn;
            }
        }

        /// <summary>
        /// Il Config Manager (F5) legge nomi e descrizioni solo quando costruisce la sua lista:
        /// gli chiediamo di ricostruirla, cosi' il cambio di lingua si vede subito.
        /// </summary>
        private static bool _refreshMenu;

        private static void RefreshConfigManager()
        {
            try
            {
                foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                {
                    var inst = info.Instance;
                    if (inst == null || inst.GetType().FullName != "ConfigurationManager.ConfigurationManager") continue;
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    var build = inst.GetType().GetMethod("BuildSettingList", flags);
                    if (build != null && build.GetParameters().Length == 0)
                    {
                        build.Invoke(inst, null);
                        return;
                    }
                    // In alternativa: chiude e riapre la finestra, che ricostruisce la lista
                    var display = inst.GetType().GetProperty("DisplayingWindow", flags);
                    if (display != null && display.CanWrite && (bool)display.GetValue(inst, null))
                    {
                        display.SetValue(inst, false, null);
                        display.SetValue(inst, true, null);
                    }
                    return;
                }
            }
            catch (System.Exception e)
            {
                Log.LogWarning("Impossibile aggiornare il menu F5 dopo il cambio di lingua: " + e.Message);
            }
        }

        private void Awake()
        {
            Log = Logger;

            // ---------------- 1. General ----------------
            Language = Bind(General, "Menu language", "Lingua del menu", MenuLanguage.Auto,
                "Language of this menu and of the on-screen notice. Auto = Windows language. If the menu does not update, reopen it (F5) or restart the game.",
                "Lingua di questo menu e dell'avviso a schermo. Auto = lingua di Windows. Se il menu non si aggiorna, riaprilo (F5) o riavvia il gioco.", 120);

            ActiveInfo = Bind(General, "Active upscaler", "Upscaler attivo", "",
                "Information only: the upscaler in use, its version and the resolutions. For FSR 4 the version is the one chosen by the AMD DLL (or driver).",
                "Solo informativo: l'upscaler in uso, la sua versione e le risoluzioni. Per FSR 4 la versione e' quella scelta dalla DLL AMD (o dal driver).",
                110, null, false, DrawActiveInfo);

            ScalingEnabled = Bind(General, "Enabled", "Attivo", true,
                "Turns upscaling on or off. It can also be toggled in game with the key below.",
                "Attiva o disattiva l'upscaling. Si puo' cambiare anche in gioco con il tasto qui sotto.", 100);

            ToggleKey = Bind(General, "Toggle key", "Tasto attiva/disattiva", new KeyboardShortcut(KeyCode.F9),
                "Key to turn upscaling on and off while playing (useful to compare with native rendering).",
                "Tasto per accendere e spegnere l'upscaling al volo (utile per confrontare con il nativo).", 90);

            ShowToast = Bind(General, "On-screen notice with F9", "Avviso a schermo con F9", true,
                "When you press the toggle key, shows the active upscaler (or native) in the top-right corner for a few seconds.",
                "Quando premi il tasto attiva/disattiva, mostra per qualche secondo in alto a destra l'upscaler attivo (o \"nativo\" se spento).", 85);

            Upscaler = Bind(General, "Upscaler", "Upscaler", UpscalerType.FSR4,
                "FSR 4 = AMD FSR 4 with AMD's official DLLs (machine learning on Radeon RX 9000; on other GPUs the DLL uses FSR 3.1). " +
                "FSR 3.1 = FidelityFX Super Resolution 3.1 built into the mod (any GPU). Bilinear = simple stretch, for comparison only. " +
                "If FSR 4 cannot start, the mod switches to FSR 3.1.",
                "FSR 4 = AMD FSR 4 con le DLL ufficiali AMD (machine learning su Radeon RX 9000; sulle altre schede la DLL usa FSR 3.1). " +
                "FSR 3.1 = FidelityFX Super Resolution 3.1 integrato nella mod (qualsiasi scheda video). Bilinear = semplice ridimensionamento, solo per confronto. " +
                "Se FSR 4 non parte, la mod passa a FSR 3.1.", 80);

            FsrQuality = Bind(General, "Quality", "Qualita'", QualityPreset.Quality,
                "Internal resolution compared to the screen: NativeAA 100% (anti-aliasing only), UltraQuality 77%, Quality 67%, Balanced 59%, Performance 50%, UltraPerformance 33%.",
                "Risoluzione interna rispetto allo schermo: NativeAA 100% (solo antialiasing), UltraQuality 77%, Quality 67%, Balanced 59%, Performance 50%, UltraPerformance 33%.", 70);

            FsrSharpness = Bind(General, "Sharpness", "Nitidezza", 0.6f,
                "Strength of the RCAS sharpening filter applied after upscaling (0 = none).",
                "Intensita' del filtro di nitidezza RCAS applicato dopo l'upscaling (0 = nessuno).", 60,
                new AcceptableValueRange<float>(0f, 1f));

            MipBiasSetting = Bind(General, "Texture sharpness (mipmap bias)", "Nitidezza texture (mipmap bias)", MipBiasMode.Moderate,
                "Keeps textures sharp despite the lower internal resolution. Moderate = balanced (recommended); AMD = AMD reference value, sharper but with more shimmering on fine surfaces.",
                "Mantiene le texture nitide nonostante la risoluzione ridotta. Moderate = equilibrato (consigliato); AMD = valore di riferimento AMD, piu' nitido ma con piu' sfarfallio sulle superfici fini.", 55);

            // ---------------- 2. Advanced ----------------
            FullResPostFx = Bind(Advanced, "Full-resolution post-processing", "Post-processing a piena risoluzione", false,
                "Off (recommended): the whole image, post-processing, atmosphere and sun included, is rendered in the original game's order and then upscaled; this is the most stable and faithful result. On: bloom, color grading, atmosphere and sun rays run AFTER upscaling at full resolution; slightly sharper effects, but edges against the sky shimmer more.",
                "Spento (consigliato): l'intera immagine, post-processing, atmosfera e sole compresi, viene calcolata nell'ordine del gioco originale e poi ingrandita; e' il risultato piu' stabile e fedele. Acceso: bloom, color grading, atmosfera e raggi del sole vengono applicati DOPO l'upscaling a risoluzione piena; effetti un po' piu' definiti, ma i bordi contro il cielo tremolano di piu'.",
                100, null, true);

            FsrReactive = Bind(Advanced, "Automatic reactive mask (FSR 3.1 only)", "Maschera reattiva automatica (solo FSR 3.1)", false,
                "Reduces trails behind fire, particles and transparent objects, but can increase shimmering on glass and reflections. FSR 4 handles these cases on its own.",
                "Riduce le scie dietro fuoco, particelle e oggetti trasparenti, ma puo' aumentare lo sfarfallio su vetri e riflessi. FSR 4 gestisce questi casi da solo.",
                95, null, true);

            FsrAutoTcr = Bind(Advanced, "Reflection flicker reduction (FSR 3.1 only)", "Riduzione sfarfallio riflessi (solo FSR 3.1)", false,
                "Experimental. Not needed with FSR 4. Detects problematic reflections and transparencies automatically. Requires the reactive mask.",
                "Sperimentale. Non serve con FSR 4. Individua da sola riflessi e trasparenze problematiche. Richiede la maschera reattiva.",
                94, null, true);

            FsrSharpen = Bind(Advanced, "Sharpening filter", "Filtro nitidezza attivo", true,
                "Enables the RCAS filter. When off, the Sharpness slider has no effect.",
                "Abilita il filtro RCAS. Se disattivato, il cursore Nitidezza non ha effetto.", 90, null, true);

            RenderScale = Bind(Advanced, "Bilinear upscaler scale", "Scala upscaler bilineare", 0.67f,
                "Internal resolution used only by the Bilinear upscaler.",
                "Risoluzione interna usata solo dall'upscaler Bilinear.", 80,
                new AcceptableValueRange<float>(0.33f, 1f), true);

            FsrDepthSource = Bind(Advanced, "Depth source", "Sorgente profondita'", DepthSource.CameraDepthTexture,
                "Depth buffer passed to FSR. Change it only if you see trails or strange artifacts.",
                "Buffer di profondita' passato a FSR. Cambialo solo se compaiono scie o artefatti strani.", 70, null, true);

            Fsr4UseDepth = Bind(Advanced, "Scene depth for FSR 4", "Profondita' scena per FSR 4", false,
                "Experimental. Also gives the scene depth to FSR 4. It can reduce trails around moving objects, but thin foliage looks less sharp. The atmosphere and sun glow always receive the depth.",
                "Sperimentale. Passa la profondita' della scena anche a FSR 4. Puo' ridurre le scie attorno agli oggetti in movimento, ma il fogliame sottile risulta meno nitido. Atmosfera e alone del sole la ricevono sempre.",
                69, null, true);

            AtmosphereStab = Bind(Advanced, "Stable sky edges", "Bordi contro il cielo stabili", true,
                "With full-resolution post-processing: stabilises over time the edge between objects and sky drawn by the atmosphere, so thin details against the sky do not shimmer.",
                "Con il post-processing a piena risoluzione: stabilizza nel tempo il confine tra oggetti e cielo disegnato dall'atmosfera, cosi' i dettagli sottili contro il cielo non tremolano.",
                69, null, true);

            // ---------------- 3. Diagnostics ----------------
            FsrDebugView = Bind(Diagnostics, "Debug view (FSR 3.1 only)", "Vista di debug (solo FSR 3.1)", false,
                "Shows the FSR debug view (motion vectors, depth, reactivity).",
                "Mostra la vista di debug di FSR (vettori di movimento, profondita', reattivita').", 100, null, true);

            FrameStatsEnabled = Bind(Diagnostics, "Measure frame time", "Misura frame time", false,
                "Every 10 seconds writes average FPS, 1% low and stutters to the log, noting whether upscaling was on. Useful to compare smoothness with and without the mod (F9).",
                "Ogni 10 secondi scrive nel log FPS medi, 1% low e scatti, indicando se l'upscaling era attivo. Utile per confrontare la fluidita' con e senza la mod (F9).",
                95, null, true);

            VerboseStats = Bind(Diagnostics, "Statistics in log", "Statistiche nel log", false,
                "Writes the mod's internal counters to the BepInEx log every 2 seconds.",
                "Scrive nel log di BepInEx ogni 2 secondi i contatori interni della mod.", 90, null, true);

            DumpKey = Bind(Diagnostics, "Camera report key", "Tasto report telecamere", new KeyboardShortcut(KeyCode.None),
                "Writes a report of cameras, post-processing and UI canvases to the log. No key by default.",
                "Scrive nel log un report di telecamere, post-processing e interfacce. Nessun tasto di default.", 80, null, true);

            SnapshotKey = Bind(Diagnostics, "Save images key", "Tasto salva immagini", new KeyboardShortcut(KeyCode.None),
                "Saves the low-resolution and the upscaled image to the BepInEx folder. No key by default.",
                "Salva nella cartella BepInEx l'immagine a bassa risoluzione e quella upscalata. Nessun tasto di default.", 70, null, true);

            CaptureKey = Bind(Diagnostics, "Frame sequence key", "Tasto sequenza fotogrammi", new KeyboardShortcut(KeyCode.None),
                $"Saves {FrameCapture.FrameCount} consecutive uncompressed frames to BepInEx/OutwardFSR_captures, to analyse shimmering. Keep the camera still.",
                $"Salva {FrameCapture.FrameCount} fotogrammi consecutivi non compressi in BepInEx/OutwardFSR_captures, per analizzare il tremolio. Tieni ferma la telecamera.", 60, null, true);

            // Migrazione una tantum: dalla 1.0.3 il post-processing a piena risoluzione e' spento di default
            // (misure: circa 2,5 volte meno tremolio). Le configurazioni esistenti lo avevano acceso.
            var revision = Config.Bind("Internal", "Settings revision", 0,
                new ConfigDescription("", null, new ConfigurationManagerAttributes { Browsable = false }));
            if (revision.Value < 1)
            {
                FullResPostFx.Value = false;
                revision.Value = 1;
            }

            ApplyLanguage();
            Language.SettingChanged += (_, __) => { ApplyLanguage(); _refreshMenu = true; };

            PostFxSplit.InstallPatch(new HarmonyLib.Harmony(Guid));

            Log.LogInfo($"{Name} {Version} caricato. Upscaling {(ScalingEnabled.Value ? "attivo" : "disattivato")}, {ToggleKey.Value} per cambiare.");
        }

        // --- Stato attivo (menu F5 e avviso a schermo) ---
        private static float _toastUntil;
        private static GUIStyle _toastStyle;

        internal static string CurrentStatus()
        {
            if (!ScalingEnabled.Value) return L("Off: native rendering", "Spento: rendering nativo");
            var r = LowResController.Active;
            return r != null ? r.StatusText : L("Waiting for the game camera", "In attesa della telecamera di gioco");
        }

        private static void DrawActiveInfo(ConfigEntryBase entry)
        {
            GUILayout.Label(CurrentStatus(), GUILayout.ExpandWidth(true));
        }

        internal static string NoticeText;
        internal static bool HideNotice;

        /// <summary>Mostra un avviso a schermo al posto dello stato (usato dalla registrazione fotogrammi).</summary>
        internal static void ShowNotice(string text, float seconds)
        {
            NoticeText = text;
            _toastUntil = Time.unscaledTime + seconds;
        }

        private void OnGUI()
        {
            if (HideNotice || Time.unscaledTime > _toastUntil) return;
            if (!ShowToast.Value && NoticeText == null) return;

            if (_toastStyle == null)
            {
                _toastStyle = new GUIStyle(GUI.skin.box) { wordWrap = false };
                _toastStyle.normal.textColor = Color.white;
            }
            _toastStyle.fontSize = Mathf.Clamp(Screen.height / 50, 12, 40);
            int pad = _toastStyle.fontSize / 2;
            _toastStyle.padding = new RectOffset(pad, pad, pad / 2, pad / 2);

            var text = new GUIContent("Outward FSR: " + (NoticeText ?? CurrentStatus()));
            Vector2 size = _toastStyle.CalcSize(text);
            float margin = Screen.height / 40f;
            GUI.Box(new Rect(Screen.width - size.x - margin, margin, size.x, size.y), text, _toastStyle);
        }

        private void Update()
        {
            // Fuori dal disegno del menu, per non modificarne la lista mentre la sta disegnando
            if (_refreshMenu)
            {
                _refreshMenu = false;
                RefreshConfigManager();
            }

            if (ToggleKey.Value.IsDown())
            {
                NoticeText = null;
                _toastUntil = Time.unscaledTime + 3f;
                ScalingEnabled.Value = !ScalingEnabled.Value;
                Log.LogInfo($"Upscaling {(ScalingEnabled.Value ? "ATTIVO" : "disattivato")} ({Upscaler.Value}, {FsrQuality.Value})");
            }

            if (DumpKey.Value.MainKey != KeyCode.None && DumpKey.Value.IsDown())
                CameraDiagnostics.Dump();

            Fsr4Native.Pump();
            LowResController.Tick();
            FrameStats.Tick(FrameStatsEnabled.Value, 10f, LowResController.Active != null);

            if (CaptureKey.Value.MainKey != KeyCode.None && CaptureKey.Value.IsDown())
                FrameCapture.Begin(this);

            if (SnapshotKey.Value.MainKey != KeyCode.None && SnapshotKey.Value.IsDown())
            {
                if (LowResController.Active != null) LowResController.Active.SaveSnapshot();
                else Log.LogWarning("Attiva prima l'upscaling.");
            }
        }

        private void OnDestroy()
        {
            LowResController.Detach();
        }
    }

    /// <summary>
    /// Preset di qualita' (stessi valori di Fsr3Upscaler.QualityMode). Esiste solo per avere nomi leggibili
    /// nel menu F5: senza [Description] il Config Manager scriverebbe "Native A A".
    /// </summary>
    internal enum QualityPreset
    {
        [System.ComponentModel.Description("Native AA (100%)")] NativeAA = 0,
        [System.ComponentModel.Description("Ultra Quality (77%)")] UltraQuality = 1,
        [System.ComponentModel.Description("Quality (67%)")] Quality = 2,
        [System.ComponentModel.Description("Balanced (59%)")] Balanced = 3,
        [System.ComponentModel.Description("Performance (50%)")] Performance = 4,
        [System.ComponentModel.Description("Ultra Performance (33%)")] UltraPerformance = 5,
    }

    internal enum MenuLanguage
    {
        Auto,
        English,
        Italiano
    }

    /// <summary>
    /// Classe riconosciuta per nome da BepInEx.ConfigurationManager (pattern standard: si copia nel proprio plugin).
    /// </summary>
#pragma warning disable 0649, 0169
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? ShowRangeAsPercent;
        public System.Action<ConfigEntryBase> CustomDrawer;
        public bool? Browsable;
        public string Category;
        public object DefaultValue;
        public bool? HideDefaultButton;
        public bool? HideSettingName;
        public string Description;
        public string DispName;
        public int? Order;
        public bool? ReadOnly;
        public bool? IsAdvanced;
        public System.Func<object, string> ObjToStr;
        public System.Func<string, object> StrToObj;
    }
#pragma warning restore 0649, 0169
}
