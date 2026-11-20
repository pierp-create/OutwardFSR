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
        public const string Version = "1.0.1";

        internal static ManualLogSource Log;

        // --- 1. Generale ---
        internal static ConfigEntry<bool> ScalingEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<UpscalerType> Upscaler;
        internal static ConfigEntry<Fsr3Upscaler.QualityMode> FsrQuality;
        internal static ConfigEntry<float> FsrSharpness;

        // --- 2. Avanzate ---
        internal static ConfigEntry<bool> FullResPostFx;
        internal static ConfigEntry<bool> FsrSharpen;
        internal static ConfigEntry<float> RenderScale;
        internal static ConfigEntry<DepthSource> FsrDepthSource;
        internal static ConfigEntry<bool> FsrReactive;
        internal static ConfigEntry<bool> FsrAutoTcr;
        internal static ConfigEntry<MipBiasMode> MipBiasSetting;
        internal static ConfigEntry<bool> FrameStatsEnabled;

        // --- 3. Diagnostica ---
        internal static ConfigEntry<bool> FsrDebugView;
        internal static ConfigEntry<bool> VerboseStats;
        internal static ConfigEntry<KeyboardShortcut> DumpKey;
        internal static ConfigEntry<KeyboardShortcut> SnapshotKey;

        private const string General = "1. Generale";
        private const string Advanced = "2. Avanzate";
        private const string Diagnostics = "3. Diagnostica";

        private void Awake()
        {
            Log = Logger;

            // ---------------- 1. Generale ----------------
            ScalingEnabled = Config.Bind(General, "Attivo", true,
                Desc("Attiva o disattiva l'upscaling. Si puo' cambiare anche in gioco con il tasto qui sotto.", 100));

            ToggleKey = Config.Bind(General, "Tasto attiva/disattiva", new KeyboardShortcut(KeyCode.F9),
                Desc("Tasto per accendere e spegnere l'upscaling al volo (utile per confrontare con il nativo).", 90));

            Upscaler = Config.Bind(General, "Upscaler", UpscalerType.FSR4,
                Desc("FSR4 = AMD FSR 4 con le DLL ufficiali AMD (ML su Radeon RX 9000; sulle altre schede la DLL usa FSR 3.1). " +
                     "FSR3 = FidelityFX Super Resolution 3.1 integrato nella mod (qualsiasi scheda video). " +
                     "Bilineare = semplice ridimensionamento, solo per confronto. Se FSR 4 non parte, la mod torna a FSR 3.1.", 80));

            FsrQuality = Config.Bind(General, "Qualita", Fsr3Upscaler.QualityMode.Quality,
                Desc("Risoluzione interna rispetto allo schermo: NativeAA 100% (solo antialiasing), UltraQuality 77%, " +
                     "Quality 67%, Balanced 59%, Performance 50%, UltraPerformance 33%.", 70));

            FsrSharpness = Config.Bind(General, "Nitidezza", 0.6f,
                Desc("Intensita' del filtro di nitidezza RCAS applicato dopo l'upscaling (0 = nessuno).", 60,
                    new AcceptableValueRange<float>(0f, 1f)));

            MipBiasSetting = Config.Bind(General, "Nitidezza texture (mipmap bias)", MipBiasMode.Moderato,
                Desc("Mantiene le texture nitide nonostante la risoluzione ridotta. Moderato = equilibrato (consigliato); " +
                     "AMD = valore di riferimento AMD, piu' nitido ma con piu' sfarfallio sulle superfici fini.", 55));

            // ---------------- 2. Avanzate ----------------
            FsrReactive = Config.Bind(Advanced, "Maschera reattiva automatica", true,
                Desc("Riduce le scie su fuoco, particelle, luci e oggetti trasparenti. Consigliata.", 95, null, true));

            FsrAutoTcr = Config.Bind(Advanced, "Riduzione sfarfallio riflessi (sperimentale)", false,
                Desc("Funzione sperimentale di FSR 3.1 che individua da sola riflessi e trasparenze problematiche. " +
                     "Puo' ridurre lo sfarfallio sulle superfici lucide, a volte a scapito di un po' di stabilita'. Richiede la maschera reattiva.", 94, null, true));

            FullResPostFx = Config.Bind(Advanced, "Post-processing a piena risoluzione", true,
                Desc("Consigliato. Esegue bloom, color grading e vignettatura DOPO l'upscaling, a risoluzione piena, " +
                     "e fornisce a FSR l'immagine HDR come raccomanda AMD. Disattivalo solo in caso di problemi con altre mod grafiche.", 100, null, true));

            FsrSharpen = Config.Bind(Advanced, "Filtro nitidezza attivo", true,
                Desc("Abilita il filtro RCAS. Se disattivato, il cursore Nitidezza non ha effetto.", 90, null, true));

            RenderScale = Config.Bind(Advanced, "Scala upscaler bilineare", 0.67f,
                Desc("Risoluzione interna usata solo dall'upscaler Bilineare.", 80,
                    new AcceptableValueRange<float>(0.33f, 1f), true));

            FsrDepthSource = Config.Bind(Advanced, "Sorgente profondita", DepthSource.CameraDepthTexture,
                Desc("Buffer di profondita' passato a FSR. Cambialo solo se compaiono scie o artefatti strani.", 70, null, true));

            // ---------------- 3. Diagnostica ----------------
            FsrDebugView = Config.Bind(Diagnostics, "Vista di debug FSR", false,
                Desc("Mostra la vista di debug di FSR (vettori di movimento, profondita', reattivita').", 100, null, true));

            FrameStatsEnabled = Config.Bind(Diagnostics, "Misura frame time", false,
                Desc("Ogni 10 secondi scrive nel log FPS medi, 1% low e scatti, indicando se l'upscaling era attivo. " +
                     "Utile per confrontare la fluidita' con e senza la mod (F9).", 95, null, true));

            VerboseStats = Config.Bind(Diagnostics, "Statistiche nel log", false,
                Desc("Scrive nel log di BepInEx ogni 2 secondi i contatori interni della mod.", 90, null, true));

            DumpKey = Config.Bind(Diagnostics, "Tasto report telecamere", new KeyboardShortcut(KeyCode.None),
                Desc("Scrive nel log un report di telecamere, post-processing e interfacce. Nessun tasto di default.", 80, null, true));

            SnapshotKey = Config.Bind(Diagnostics, "Tasto salva immagini", new KeyboardShortcut(KeyCode.None),
                Desc("Salva nella cartella BepInEx l'immagine a bassa risoluzione e quella upscalata. Nessun tasto di default.", 70, null, true));

            PostFxSplit.InstallPatch(new HarmonyLib.Harmony(Guid));

            Log.LogInfo($"{Name} {Version} caricato. Upscaling {(ScalingEnabled.Value ? "attivo" : "disattivato")}, {ToggleKey.Value} per cambiare.");
        }

        private static ConfigDescription Desc(string text, int order, AcceptableValueBase range = null, bool advanced = false)
        {
            // Attributi riconosciuti da BepInEx.ConfigurationManager (F5): ordine e flag "avanzato"
            var attrs = new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced };
            return new ConfigDescription(text, range, attrs);
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                ScalingEnabled.Value = !ScalingEnabled.Value;
                Log.LogInfo($"Upscaling {(ScalingEnabled.Value ? "ATTIVO" : "disattivato")} ({Upscaler.Value}, {FsrQuality.Value})");
            }

            if (DumpKey.Value.MainKey != KeyCode.None && DumpKey.Value.IsDown())
                CameraDiagnostics.Dump();

            Fsr4Native.Pump();
            LowResController.Tick();
            FrameStats.Tick(FrameStatsEnabled.Value, 10f, LowResController.Active != null);

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
