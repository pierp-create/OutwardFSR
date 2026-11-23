using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace OutwardFSR
{
    /// <summary>
    /// Bloom stabile con l'upscaling attivo.
    ///
    /// Con il post-processing a bassa risoluzione (impostazione consigliata) il bloom del gioco viene calcolato
    /// prima di FSR, sull'immagine con il jitter: i riflessi piccoli (vetri, metalli, cupole) cambiano da un
    /// frame all'altro e il loro alone lampeggia. Le misure lo confermano: con il bloom spento lo scintillio
    /// sparisce. Qui, subito dopo che PPv2 ha calcolato il bloom, lo si media nel tempo (riproiettato con i
    /// vettori di movimento) e si passa al gioco il bloom stabilizzato. Ordine degli effetti e aspetto restano
    /// quelli originali: cambia solo che l'alone non tremola.
    /// </summary>
    internal static class BloomStab
    {
        private static readonly int IdBloomTex = Shader.PropertyToID("_BloomTex");
        private static readonly int IdInBloom = Shader.PropertyToID("_InBloom");
        private static readonly int IdInMotion = Shader.PropertyToID("_InMotion");
        private static readonly int IdHistory = Shader.PropertyToID("_History");
        private static readonly int IdOut = Shader.PropertyToID("_Out");
        private static readonly int IdParams = Shader.PropertyToID("_Params");
        private static readonly int IdSizes = Shader.PropertyToID("_Sizes");

        private const float HistoryWeight = 0.9f;

        private static ComputeShader _cs;
        private static RenderTexture _a, _b;
        private static int _lastFrame = -10;
        private static bool _failed;
        private static readonly System.Reflection.FieldInfo BloomBufferField =
            AccessTools.Field(typeof(PostProcessRenderContext), "bloomBufferNameID");

        public static void InstallPatch(Harmony harmony)
        {
            try
            {
                var type = AccessTools.TypeByName("UnityEngine.Rendering.PostProcessing.BloomRenderer");
                var target = type != null ? AccessTools.Method(type, "Render") : null;
                if (target == null)
                {
                    Plugin.Log.LogWarning("Bloom stabile: BloomRenderer.Render non trovato, opzione non disponibile.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(BloomStab), nameof(RenderPostfix)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Bloom stabile non installato: " + e.Message);
            }
        }

        private static void RenderPostfix(object __instance, PostProcessRenderContext context)
        {
            if (_failed || context == null || !Plugin.BloomStab.Value) return;
            var active = LowResController.Active;
            if (active == null || context.camera == null || context.camera.gameObject != active.gameObject) return;

            try
            {
                if (BloomBufferField == null) { _failed = true; Plugin.Log.LogWarning("Bloom stabile: campo bloomBufferNameID non trovato."); return; }
                int bloomId = (int)BloomBufferField.GetValue(context);
                if (_cs == null)
                {
                    _cs = FsrShaders.GetBloomStab();
                    if (_cs == null) { _failed = true; return; }
                }

                // Stesse dimensioni con cui BloomRenderer crea il primo livello della piramide
                float ratio = 0f;
                var settings = Traverse.Create(__instance).Field("settings").GetValue() as Bloom;
                if (settings != null) ratio = Mathf.Clamp(settings.anamorphicRatio.value, -1f, 1f);
                float rw = ratio < 0f ? -ratio : 0f;
                float rh = ratio > 0f ? ratio : 0f;
                int w = Mathf.Max(1, Mathf.FloorToInt(context.screenWidth / (2f - rw)));
                int h = Mathf.Max(1, Mathf.FloorToInt(context.screenHeight / (2f - rh)));

                bool reset = Time.frameCount - _lastFrame > 1;
                _lastFrame = Time.frameCount;
                if (_a == null || _a.width != w || _a.height != h)
                {
                    Release();
                    _a = NewRT(w, h, "OutwardFSR_BloomA");
                    _b = NewRT(w, h, "OutwardFSR_BloomB");
                    reset = true;
                }

                var t = _a; _a = _b; _b = t; // _a = uscita di questo frame, _b = storico

                var cmd = context.command;
                cmd.BeginSample("OutwardFSR bloom stabile");
                cmd.SetComputeTextureParam(_cs, 0, IdInBloom, new RenderTargetIdentifier(bloomId));
                cmd.SetComputeTextureParam(_cs, 0, IdInMotion, BuiltinRenderTextureType.MotionVectors);
                cmd.SetComputeTextureParam(_cs, 0, IdHistory, _b);
                cmd.SetComputeTextureParam(_cs, 0, IdOut, _a);
                cmd.SetComputeVectorParam(_cs, IdParams, new Vector4(HistoryWeight, reset ? 1f : 0f, 0f, 0f));
                cmd.SetComputeIntParams(_cs, IdSizes, w, h, 0, 0);
                cmd.DispatchCompute(_cs, 0, (w + 7) / 8, (h + 7) / 8, 1);
                // Il pannello finale di PPv2 legge il bloom da qui
                cmd.SetGlobalTexture(IdBloomTex, _a);
                cmd.EndSample("OutwardFSR bloom stabile");
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning("Bloom stabile disattivato per un errore: " + e.Message);
            }
        }

        private static RenderTexture NewRT(int w, int h, string name)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            rt.Create();
            return rt;
        }

        public static void Release()
        {
            if (_a != null) { _a.Release(); UnityEngine.Object.Destroy(_a); _a = null; }
            if (_b != null) { _b.Release(); UnityEngine.Object.Destroy(_b); _b = null; }
        }
    }
}
