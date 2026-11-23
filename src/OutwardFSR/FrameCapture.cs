using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace OutwardFSR
{
    /// <summary>
    /// Diagnostica: registra una sequenza di fotogrammi consecutivi esattamente come escono dalla scheda video
    /// (immagine finale, senza compressione), per analizzare il tremolio pixel per pixel.
    /// La lettura dalla GPU e' asincrona e il salvataggio avviene in un thread separato, cosi' il gioco non
    /// rallenta durante la registrazione e i fotogrammi restano davvero consecutivi.
    /// File: BepInEx/OutwardFSR_captures/&lt;data&gt;_&lt;stato&gt;/frame_NNN.raw (RGBA8, riga 0 = come letta dalla GPU)
    /// piu' info.txt con dimensioni e impostazioni.
    /// </summary>
    internal sealed class FrameCapture : MonoBehaviour
    {
        public const int FrameCount = 30;

        private static FrameCapture _instance;
        private bool _busy;
        private volatile bool _error;

        public static void Begin(MonoBehaviour host)
        {
            if (_instance == null)
                _instance = host.gameObject.AddComponent<FrameCapture>();
            if (_instance._busy)
            {
                Plugin.Log.LogWarning("Registrazione fotogrammi gia' in corso.");
                Plugin.ShowNotice(Plugin.It ? "registrazione ancora in salvataggio, attendi" : "capture still saving, please wait", 3f);
                return;
            }
            _instance.StartCoroutine(_instance.Run());
        }

        private IEnumerator Run()
        {
            _busy = true;
            int w = Screen.width, h = Screen.height;
            string status = Plugin.CurrentStatus();
            string safe = string.Join("_", status.Split(Path.GetInvalidFileNameChars()));
            if (safe.Length > 60) safe = safe.Substring(0, 60);
            string dir = Path.Combine(Paths.BepInExRootPath, "OutwardFSR_captures",
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + safe.Replace(' ', '_').Replace(':', '_'));
            Directory.CreateDirectory(dir);

            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "OutwardFSR_Capture" };
            rt.Create();

            var frames = new byte[FrameCount][];
            int received = 0, failed = 0;
            // Diagnostica della profondita' usata dall'atmosfera (solo i primi fotogrammi, per la memoria)
            const int DepthFrames = 16;
            var depthFull = new byte[DepthFrames][];
            var depthLow = new byte[DepthFrames][];
            var jitters = new List<string>();
            int depthPending = 0;
            int fullW = 0, fullH = 0, lowW = 0, lowH = 0;
            var wait = new WaitForEndOfFrame();
            var times = new List<float>(FrameCount);

            Plugin.Log.LogInfo($"Registrazione di {FrameCount} fotogrammi ({w}x{h}) in corso: {status}");
            Plugin.HideNotice = true; // l'avviso non deve finire nei fotogrammi

            for (int i = 0; i < FrameCount; i++)
            {
                yield return wait;
                times.Add(Time.unscaledDeltaTime * 1000f);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
                int index = i;
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                {
                    if (req.hasError) { failed++; }
                    else frames[index] = req.GetData<byte>().ToArray();
                    received++;
                });

                var lr = LowResController.Active;
                if (lr != null && i < DepthFrames)
                {
                    jitters.Add(lr.DebugJitter.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + ";" +
                                lr.DebugJitter.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
                    var full = lr.DebugSceneDepth;
                    var low = lr.DebugLowDepth;
                    if (full != null)
                    {
                        fullW = full.width; fullH = full.height; depthPending++;
                        AsyncGPUReadback.Request(full, 0, TextureFormat.RFloat, req =>
                        {
                            if (!req.hasError) depthFull[index] = req.GetData<byte>().ToArray();
                            depthPending--;
                        });
                    }
                    if (low != null)
                    {
                        lowW = low.width; lowH = low.height; depthPending++;
                        AsyncGPUReadback.Request(low, 0, TextureFormat.RFloat, req =>
                        {
                            if (!req.hasError) depthLow[index] = req.GetData<byte>().ToArray();
                            depthPending--;
                        });
                    }
                }
            }

            // Attende che arrivino tutte le letture dalla GPU
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((received < FrameCount || depthPending > 0) && Time.realtimeSinceStartup < deadline)
                yield return null;
            rt.Release();
            Destroy(rt);
            Plugin.HideNotice = false;
            Plugin.ShowNotice(Plugin.It ? "salvataggio registrazione in corso..." : "saving capture...", 60f);

            string info =
                $"width={w}\nheight={h}\nformat=RGBA8\nframes={FrameCount}\nfailed={failed}\n" +
                $"status={status}\nversion={Plugin.Version}\n" +
                $"upscaler={Plugin.Upscaler.Value}\nquality={Plugin.FsrQuality.Value}\n" +
                $"fullres_post={Plugin.FullResPostFx.Value}\nstable_bloom={Plugin.BloomStab.Value}\nmip_bias={Plugin.MipBiasSetting.Value}\n" +
                $"reactive={Plugin.FsrReactive.Value}\nfsr4_depth={Plugin.Fsr4UseDepth.Value}\nstable_sky_edges={Plugin.AtmosphereStab.Value}\nsharpen={Plugin.FsrSharpen.Value}\nsharpness={Plugin.FsrSharpness.Value}\n" +
                $"depth_full={fullW}x{fullH}\ndepth_low={lowW}x{lowH}\njitter={string.Join(",", jitters.ToArray())}\n" +
                $"frame_ms={string.Join(",", times.ConvertAll(t => t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).ToArray())}\n";

            var thread = new Thread(() =>
            {
                try
                {
                    File.WriteAllText(Path.Combine(dir, "info.txt"), info);
                    for (int i = 0; i < frames.Length; i++)
                    {
                        if (frames[i] == null) continue;
                        File.WriteAllBytes(Path.Combine(dir, $"frame_{i:D3}.raw"), frames[i]);
                        frames[i] = null;
                    }
                    for (int i = 0; i < DepthFrames; i++)
                    {
                        if (depthFull[i] != null) File.WriteAllBytes(Path.Combine(dir, $"depthfull_{i:D3}.raw"), depthFull[i]);
                        if (depthLow[i] != null) File.WriteAllBytes(Path.Combine(dir, $"depthlow_{i:D3}.raw"), depthLow[i]);
                        depthFull[i] = depthLow[i] = null;
                    }
                    Plugin.Log.LogInfo($"Registrazione salvata: {dir}");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Salvataggio della registrazione fallito: {ex}");
                    _error = true;
                }
            }) { IsBackground = true, Name = "OutwardFSR capture" };
            thread.Start();
            while (thread.IsAlive) yield return null;
            _busy = false;
            Plugin.ShowNotice(_error
                ? (Plugin.It ? "registrazione: errore, vedi log" : "capture: error, see log")
                : (Plugin.It ? "registrazione salvata" : "capture saved"), 4f);
            _error = false;
        }
    }
}
