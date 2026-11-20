using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

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

        public void Dispose()
        {
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
