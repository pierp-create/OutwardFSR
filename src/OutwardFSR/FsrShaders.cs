using System.IO;
using BepInEx;
using FidelityFX.FSR3;
using UnityEngine;

namespace OutwardFSR
{
    /// <summary>
    /// Carica (una sola volta) i compute shader di FSR 3.1 dall'AssetBundle
    /// "outwardfsr_shaders" che sta accanto alla DLL della mod.
    /// </summary>
    internal static class FsrShaders
    {
        private static bool _attempted;
        private static Fsr3UpscalerShaders _shaders;
        private static AssetBundle _bundle;
        private static bool _prepareAttempted;
        private static ComputeShader _fsr4Prepare;

        private static bool _depthStabAttempted;
        private static ComputeShader _depthStab;

        /// <summary>Compute shader che stabilizza nel tempo la profondita' a risoluzione piena (puo' mancare).</summary>
        public static ComputeShader GetDepthStab()
        {
            if (_depthStabAttempted) return _depthStab;
            Get();
            _depthStabAttempted = true;
            if (_bundle == null) return null;
            _depthStab = _bundle.LoadAsset<ComputeShader>("outwardfsr_depthstab");
            if (_depthStab == null)
                Plugin.Log.LogWarning("Shader 'outwardfsr_depthstab' non presente nel bundle: profondita' per l'atmosfera senza stabilizzazione.");
            return _depthStab;
        }

        private static bool _bloomStabAttempted;
        private static ComputeShader _bloomStab;

        /// <summary>Compute shader che stabilizza nel tempo il bloom (puo' mancare).</summary>
        public static ComputeShader GetBloomStab()
        {
            if (_bloomStabAttempted) return _bloomStab;
            Get();
            _bloomStabAttempted = true;
            if (_bundle == null) return null;
            _bloomStab = _bundle.LoadAsset<ComputeShader>("outwardfsr_bloomstab");
            if (_bloomStab == null)
                Plugin.Log.LogWarning("Shader 'outwardfsr_bloomstab' non presente nel bundle: bloom senza stabilizzazione.");
            return _bloomStab;
        }

        /// <summary>Compute shader che prepara profondità e vettori di movimento per FSR 4.</summary>
        public static ComputeShader GetFsr4Prepare()
        {
            if (_prepareAttempted) return _fsr4Prepare;
            Get();
            _prepareAttempted = true;
            if (_bundle == null) return null;
            _fsr4Prepare = _bundle.LoadAsset<ComputeShader>("outwardfsr4_prepare");
            if (_fsr4Prepare == null)
                Plugin.Log.LogWarning("FSR 4: shader 'outwardfsr4_prepare' non presente nel bundle (ricompila il bundle shader).");
            return _fsr4Prepare;
        }

        public static Fsr3UpscalerShaders Get()
        {
            if (_attempted) return _shaders;
            _attempted = true;

            if (!SystemInfo.supportsComputeShaders)
            {
                Plugin.Log.LogError("FSR: la scheda video non supporta i compute shader.");
                return null;
            }

            string dir = Path.GetDirectoryName(typeof(FsrShaders).Assembly.Location) ?? Paths.PluginPath;
            string path = Path.Combine(dir, "outwardfsr_shaders");
            if (!File.Exists(path))
            {
                Plugin.Log.LogError($"FSR: bundle shader non trovato in {path}. Esegui tools\\CompilaShader.bat e ricompila la mod.");
                return null;
            }

            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Plugin.Log.LogError("FSR: impossibile caricare il bundle shader (versione di Unity diversa?).");
                return null;
            }

            _bundle = bundle;
            var s = new Fsr3UpscalerShaders
            {
                prepareInputsPass = Load(bundle, "ffx_fsr3upscaler_prepare_inputs_pass"),
                lumaPyramidPass = Load(bundle, "ffx_fsr3upscaler_luma_pyramid_pass"),
                shadingChangePyramidPass = Load(bundle, "ffx_fsr3upscaler_shading_change_pyramid_pass"),
                shadingChangePass = Load(bundle, "ffx_fsr3upscaler_shading_change_pass"),
                prepareReactivityPass = Load(bundle, "ffx_fsr3upscaler_prepare_reactivity_pass"),
                lumaInstabilityPass = Load(bundle, "ffx_fsr3upscaler_luma_instability_pass"),
                accumulatePass = Load(bundle, "ffx_fsr3upscaler_accumulate_pass"),
                sharpenPass = Load(bundle, "ffx_fsr3upscaler_rcas_pass"),
                autoGenReactivePass = Load(bundle, "ffx_fsr3upscaler_autogen_reactive_pass"),
                tcrAutoGenPass = Load(bundle, "ffx_fsr3upscaler_tcr_autogen_pass"),
                debugViewPass = Load(bundle, "ffx_fsr3upscaler_debug_view_pass"),
            };

            if (s.prepareInputsPass == null || s.accumulatePass == null || s.sharpenPass == null)
            {
                Plugin.Log.LogError("FSR: mancano shader essenziali nel bundle.");
                return null;
            }

            _shaders = s;
            Plugin.Log.LogInfo("FSR: shader FSR 3.1 caricati correttamente.");
            return _shaders;
        }

        private static ComputeShader Load(AssetBundle bundle, string name)
        {
            var cs = bundle.LoadAsset<ComputeShader>(name);
            if (cs == null) Plugin.Log.LogWarning($"FSR: shader '{name}' non trovato nel bundle.");
            return cs;
        }
    }
}
