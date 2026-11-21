using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using Object = UnityEngine.Object;

namespace OutwardFSR
{
    /// <summary>
    /// Divide il post-processing (PPv2) del gioco in due metà:
    ///  - sulla telecamera principale (bassa risoluzione) restano gli effetti "di scena":
    ///    occlusione ambientale e riflessioni, che devono precedere l'upscaling;
    ///  - sulla telecamera presenter (piena risoluzione, dopo FSR) girano gli effetti "finali":
    ///    bloom, color grading/tonemapping, vignettatura, aberrazione cromatica, grana.
    ///
    /// Metodo: una patch Harmony su PostProcessManager.UpdateSettings, il punto in cui PPv2 calcola
    /// le impostazioni di ogni telecamera. Subito dopo, spegniamo gli effetti che non devono girare
    /// su quella telecamera. Non usa layer né volumi aggiuntivi, quindi convive con altre mod grafiche.
    /// </summary>
    internal sealed class PostFxSplit
    {
        private static readonly FieldInfo ResourcesField =
            typeof(PostProcessLayer).GetField("m_Resources", BindingFlags.NonPublic | BindingFlags.Instance);

        private static bool _patched;
        private static bool _patchFailed;

        // Telecamere attualmente gestite (una sola istanza alla volta)
        private static PostProcessLayer _main;
        private static PostProcessLayer _presenter;

        public PostProcessLayer PresenterLayer { get; private set; }

        /// <summary>Installa la patch (una volta sola, all'avvio della mod).</summary>
        public static void InstallPatch(Harmony harmony)
        {
            if (_patched || _patchFailed) return;
            try
            {
                var target = AccessTools.Method(typeof(PostProcessManager), "UpdateSettings");
                if (target == null) throw new MissingMethodException("PostProcessManager.UpdateSettings");
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(PostFxSplit), nameof(UpdateSettingsPostfix)));
                _patched = true;
            }
            catch (Exception e)
            {
                _patchFailed = true;
                Plugin.Log.LogWarning($"Post-processing a piena risoluzione non disponibile: {e.Message}");
            }
        }

        /// <summary>
        /// Prova ad attivare la divisione. Il presenter deve essere ancora inattivo
        /// (il PostProcessLayer va inizializzato prima che si accenda).
        /// </summary>
        public static PostFxSplit TryCreate(Camera mainCam, Camera presenter)
        {
            if (!_patched)
            {
                Plugin.Log.LogWarning("Post-processing a piena risoluzione: patch non installata, opzione ignorata.");
                return null;
            }

            var mainLayer = mainCam.GetComponent<PostProcessLayer>();
            if (mainLayer == null || !mainLayer.enabled)
            {
                Plugin.Log.LogWarning("Post-processing a piena risoluzione: il gioco non usa PPv2 su questa telecamera, opzione ignorata.");
                return null;
            }

            var resources = ResourcesField?.GetValue(mainLayer) as PostProcessResources;
            if (resources == null)
            {
                Plugin.Log.LogWarning("Post-processing a piena risoluzione: risorse PPv2 non trovate, opzione ignorata.");
                return null;
            }

            var ppl = presenter.gameObject.AddComponent<PostProcessLayer>();
            ppl.Init(resources);
            ppl.volumeTrigger = mainLayer.volumeTrigger != null ? mainLayer.volumeTrigger : mainCam.transform;
            ppl.volumeLayer = mainLayer.volumeLayer; // vede gli stessi volumi del gioco (anche quelli delle altre mod)
            ppl.antialiasingMode = PostProcessLayer.Antialiasing.None;
            ppl.stopNaNPropagation = true;

            _main = mainLayer;
            _presenter = ppl;

            Plugin.Log.LogInfo("Post-processing finale spostato a piena risoluzione (dopo l'upscaling).");
            return new PostFxSplit { PresenterLayer = ppl };
        }

        // ------------------------------------------------------------------------------------------
        // Effetti immagine "finali" del gioco (OnRenderImage senza [ImageEffectOpaque]), per esempio i
        // raggi del sole di Time of Day (TOD_Rays). Nel gioco originale girano DOPO il tonemapping di PPv2;
        // se restassero sulla telecamera principale lavorerebbero sull'immagine HDR non ancora tonemappata
        // e il loro contributo (alone del sole, foschia calda) risulterebbe molto piu' debole.
        // Li spostiamo quindi sul presenter, dopo PPv2, come nel gioco originale.
        // ------------------------------------------------------------------------------------------
        private sealed class MovedEffect
        {
            public MonoBehaviour Original;
            public MonoBehaviour Copy;
            public FieldInfo[] Fields;
            public bool WasEnabled;
            public MethodInfo PreCull, PostRender;
        }

        private readonly System.Collections.Generic.List<MovedEffect> _moved = new System.Collections.Generic.List<MovedEffect>();

        public bool HasMovedEffects => _moved.Count > 0;

        // Effetti spostati, nell'ordine in cui compaiono sulla telecamera: lo scattering atmosferico di Time of Day
        // (atmosfera, foschia, bagliore del sole) e i raggi del sole. Entrambi ricostruiscono la scena dalla
        // profondita': sul presenter ricevono la profondita' della telecamera principale (DepthOverride).
        // Lo scattering riceve la profondita' reale della scena (DepthOverride, letta da ResolvedDepth);
        // i raggi restano come nella versione stabile.
        private static readonly string[] MovableEffects = { "TOD_Scattering", "TOD_Rays" };

        private static bool IsScattering(MovedEffect e) => e.Original != null && e.Original.GetType().Name == "TOD_Scattering";

        /// <summary>Profondita' della scena (bassa risoluzione) da dare agli effetti spostati.</summary>
        public Texture DepthOverride { get; set; }

        private static readonly int IdCameraDepth = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly System.Collections.Generic.Dictionary<Type, FieldInfo[]> MaterialFieldCache =
            new System.Collections.Generic.Dictionary<Type, FieldInfo[]>();

        private static FieldInfo[] MaterialFields(Type type)
        {
            if (MaterialFieldCache.TryGetValue(type, out var cached)) return cached;
            var list = new System.Collections.Generic.List<FieldInfo>();
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!f.IsStatic && typeof(Material).IsAssignableFrom(f.FieldType)) list.Add(f);
            return MaterialFieldCache[type] = list.ToArray();
        }

        /// <summary>
        /// Componente sulla telecamera principale: esegue OnPreCull/OnPostRender dell'effetto originale (spento),
        /// come farebbe il gioco. TOD_Scattering li usa per nascondere la cupola dell'atmosfera mentre la
        /// telecamera principale disegna la scena (il cielo lo disegna lo scattering stesso): senza questo,
        /// il cielo verrebbe disegnato due volte.
        /// </summary>
        private sealed class MainCameraHook : MonoBehaviour
        {
            public PostFxSplit Owner;

            private void OnPreCull() => Owner?.InvokeOnOriginals("OnPreCull");
            private void OnPostRender() => Owner?.InvokeOnOriginals("OnPostRender");
        }

        private MainCameraHook _hook;

        private void InvokeOnOriginals(string method)
        {
            foreach (var e in _moved)
            {
                if (e.Original == null || e.Copy == null || !e.Copy.enabled) continue;
                var m = method == "OnPreCull" ? e.PreCull : e.PostRender;
                if (m == null) continue;
                try { m.Invoke(e.Original, null); }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"{e.Original.GetType().Name}.{method}: {ex.InnerException?.Message ?? ex.Message}");
                    if (method == "OnPreCull") e.PreCull = null; else e.PostRender = null;
                }
            }
        }

        private static MethodInfo FindMethod(Type type, string name)
        {
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (m != null) return m;
            }
            return null;
        }

        private static bool IsFinalImageEffect(MonoBehaviour b)
        {
            if (b == null || !b.enabled || b is PostProcessLayer || b is LowResRenderer) return false;
            if (Array.IndexOf(MovableEffects, b.GetType().Name) < 0) return false;
            var m = b.GetType().GetMethod("OnRenderImage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return false;
            // Gli effetti "opachi" (nebbia, scattering) devono restare prima dei trasparenti, sulla telecamera principale
            return m.GetCustomAttributes(typeof(ImageEffectOpaque), true).Length == 0;
        }

        private static bool IsCopyableField(FieldInfo f)
        {
            if (f.IsStatic || f.IsInitOnly || f.IsLiteral || f.IsNotSerialized) return false;
            if (!f.IsPublic && f.GetCustomAttributes(typeof(SerializeField), true).Length == 0) return false;
            var t = f.FieldType;
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || (t.IsValueType && !t.IsPointer)) return true;
            // Risorse condivisibili; NON copiamo riferimenti a telecamere, materiali o altri componenti
            return typeof(Shader).IsAssignableFrom(t) || typeof(Texture).IsAssignableFrom(t) ||
                   t == typeof(AnimationCurve) || t == typeof(Gradient);
        }

        private static FieldInfo[] CopyableFields(Type type)
        {
            var list = new System.Collections.Generic.List<FieldInfo>();
            for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (IsCopyableField(f)) list.Add(f);
            return list.ToArray();
        }

        private static void CopyFields(MovedEffect e)
        {
            foreach (var f in e.Fields)
            {
                try { f.SetValue(e.Copy, f.GetValue(e.Original)); }
                catch { /* campo non copiabile: si ignora */ }
            }
        }

        /// <summary>
        /// Sposta sul presenter (che deve essere ancora inattivo) gli effetti finali della telecamera principale.
        /// </summary>
        public void MoveFinalEffects(Camera mainCam, Camera presenter)
        {
            foreach (var b in mainCam.GetComponents<MonoBehaviour>())
            {
                if (!IsFinalImageEffect(b)) continue;
                try
                {
                    var copy = (MonoBehaviour)presenter.gameObject.AddComponent(b.GetType());
                    var e = new MovedEffect
                    {
                        Original = b, Copy = copy, Fields = CopyableFields(b.GetType()), WasEnabled = b.enabled,
                        PreCull = FindMethod(b.GetType(), "OnPreCull"), PostRender = FindMethod(b.GetType(), "OnPostRender")
                    };
                    CopyFields(e);
                    _moved.Add(e);
                    b.enabled = false;
                    if (IsScattering(e) && (e.PreCull != null || e.PostRender != null) && _hook == null)
                    {
                        _hook = mainCam.gameObject.AddComponent<MainCameraHook>();
                        _hook.Owner = this;
                    }
                    Plugin.Log.LogInfo($"Effetto '{b.GetType().Name}' spostato dopo il post-processing finale (come nel gioco originale).");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Impossibile spostare l'effetto '{b.GetType().Name}': {ex.Message}");
                }
            }
        }

        /// <summary>Ogni frame: copia i parametri aggiornati dal gioco e rispetta un'eventuale riaccensione.</summary>
        /// <summary>Passa DepthOverride ai materiali dello scattering (chiamato anche durante il frame).</summary>
        public void ApplyDepthOverride()
        {
            if (DepthOverride == null) return;
            foreach (var e in _moved)
                if (e.Copy != null && e.Copy.enabled)
                    foreach (var f in MaterialFields(e.Copy.GetType()))
                        if (f.GetValue(e.Copy) is Material mat && mat != null)
                            mat.SetTexture(IdCameraDepth, DepthOverride);
        }

        public void SyncMovedEffects()
        {
            foreach (var e in _moved)
            {
                if (e.Original == null || e.Copy == null) continue;
                if (e.Original.enabled)
                {
                    // Il gioco (es. impostazioni di qualita') lo ha riacceso: lo teniamo attivo solo sul presenter
                    e.Copy.enabled = true;
                    e.Original.enabled = false;
                }
                CopyFields(e);

                // Profondita' della scena: il presenter non disegna geometria, la sua profondita' e' vuota
                if (DepthOverride != null && e.Copy.enabled)
                    foreach (var f in MaterialFields(e.Copy.GetType()))
                        if (f.GetValue(e.Copy) is Material mat && mat != null)
                            mat.SetTexture(IdCameraDepth, DepthOverride);
            }
        }

        private void RestoreMovedEffects()
        {
            if (_hook != null)
            {
                // Riporta lo stato lasciato dagli OnPreCull (es. cupola dell'atmosfera di nuovo visibile)
                InvokeOnOriginals("OnPostRender");
                Object.Destroy(_hook);
                _hook = null;
            }
            foreach (var e in _moved)
            {
                if (e.Copy != null) Object.Destroy(e.Copy);
                if (e.Original != null) e.Original.enabled = e.WasEnabled;
            }
            _moved.Clear();
        }

        public void Dispose()
        {
            RestoreMovedEffects();
            if (_presenter == PresenterLayer)
            {
                _main = null;
                _presenter = null;
            }
        }

        // Chiamato da PPv2 per ogni telecamera, dopo aver mescolato i volumi
        private static void UpdateSettingsPostfix(object[] __args)
        {
            if (_main == null || __args == null || __args.Length == 0) return;
            var layer = __args[0] as PostProcessLayer;
            if (layer == null) return;

            if (layer == _main)
            {
                // Bassa risoluzione: niente effetti finali (verranno fatti dopo FSR)
                Off<Bloom>(layer);
                Off<ColorGrading>(layer);
                Off<Vignette>(layer);
                Off<ChromaticAberration>(layer);
                Off<Grain>(layer);
                Off<LensDistortion>(layer);
                Off<AutoExposure>(layer);
            }
            else if (layer == _presenter)
            {
                // Piena risoluzione: niente effetti di scena (sono già nell'immagine di FSR)
                Off<AmbientOcclusion>(layer);
                Off<ScreenSpaceReflections>(layer);
                Off<MotionBlur>(layer);
                Off<DepthOfField>(layer);
            }
        }

        private static void Off<T>(PostProcessLayer layer) where T : PostProcessEffectSettings
        {
            try
            {
                var s = layer.GetSettings<T>();
                if (s != null) s.enabled.value = false;
            }
            catch
            {
                // Effetto non presente in questa versione di PPv2: niente da fare
            }
        }
    }
}
