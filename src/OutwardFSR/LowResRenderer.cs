using System.Collections;
using System.IO;
using BepInEx;
using FidelityFX;
using FidelityFX.FSR3;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace OutwardFSR
{
    internal enum PresentMode
    {
        /// <summary>Una seconda telecamera vuota copia l'immagine a schermo prima dell'interfaccia.</summary>
        Presenter,
        /// <summary>Copia a fine frame (copre anche l'interfaccia: solo per diagnostica).</summary>
        FineFrame
    }

    // [Description] = nome mostrato nel menu F5 (senza, il Config Manager scriverebbe "F S R4")
    internal enum UpscalerType
    {
        /// <summary>Semplice stiramento bilineare (test).</summary>
        [System.ComponentModel.Description("Bilinear")]
        Bilinear,
        /// <summary>AMD FidelityFX Super Resolution 3.1.</summary>
        [System.ComponentModel.Description("FSR 3.1")]
        FSR3,
        /// <summary>AMD FSR 4 tramite le DLL ufficiali AMD (DirectX 12, ponte dal DirectX 11 del gioco).</summary>
        [System.ComponentModel.Description("FSR 4")]
        FSR4
    }

    internal enum DepthSource
    {
        /// <summary>Texture di profondità generata dalla telecamera (_CameraDepthTexture).</summary>
        CameraDepthTexture,
        /// <summary>Depth buffer della destinazione della telecamera.</summary>
        CameraTarget
    }

    /// <summary>
    /// Aggancia il rendering a risoluzione ridotta alla telecamera principale
    /// e lo sposta se il gioco cambia telecamera (cambio scena, menu, ecc.).
    /// </summary>
    internal static class LowResController
    {
        private static Camera _target;
        private static LowResRenderer _renderer;
        private static float _nextStatsLog;

        public static LowResRenderer Active => _renderer;

        public static void Tick()
        {
            var cam = Camera.main;
            if (!Plugin.ScalingEnabled.Value || cam == null)
            {
                Detach();
                return;
            }

            if (cam != _target || _renderer == null || _renderer.NeedsRebuild())
            {
                Detach();
                _target = cam;
                _renderer = cam.gameObject.AddComponent<LowResRenderer>();
                _nextStatsLog = Time.unscaledTime + 2f;
            }

            if (Plugin.VerboseStats.Value && Time.unscaledTime >= _nextStatsLog)
            {
                _nextStatsLog = Time.unscaledTime + 2f;
                _renderer.LogStats();
            }
        }

        public static void Detach()
        {
            if (_renderer != null)
                Object.DestroyImmediate(_renderer);
            _renderer = null;
            _target = null;
        }
    }

    /// <summary>
    /// Fa renderizzare la telecamera (scena + post-processing del gioco) in una RenderTexture
    /// più piccola, poi la porta a risoluzione piena con FSR 3.1 (o con uno stiramento bilineare).
    /// L'interfaccia del gioco (Screen Space Overlay) viene disegnata dopo, a piena risoluzione.
    /// </summary>
    internal class LowResRenderer : MonoBehaviour
    {
        private Camera _cam;
        private RenderTexture _lowRes;
        private Camera _presenter;
        private CommandBuffer _presentCb;

        // --- FSR ---
        private bool _useFsr;
        private Fsr3UpscalerContext _context;
        private RenderTexture _upscaled;
        private CommandBuffer _fsrCb;
        private Vector2Int _displaySize, _renderSize;
        private bool _resetHistory = true;
        private readonly Fsr3Upscaler.DispatchDescription _desc = new Fsr3Upscaler.DispatchDescription();
        private PostProcessLayer _ppl;
        private PostProcessLayer.Antialiasing _originalAA;
        private DepthTextureMode _originalDepthMode;

        // Impostazioni con cui è stato creato (se cambiano, il controller ricrea il componente)
        private PresentMode _mode;
        private UpscalerType _upscalerSetting;
        private Fsr3Upscaler.QualityMode _quality;
        private DepthSource _depthSource;
        private bool _splitPostFxSetting;
        private PostFxSplit _postFx;

        // --- Maschera reattiva (riduce le scie su fuoco, particelle, trasparenze) ---
        private bool _useReactive;
        private CommandBuffer _opaqueCb;
        private RenderTexture _colorOpaque;
        private readonly Fsr3Upscaler.GenerateReactiveDescription _genDesc = new Fsr3Upscaler.GenerateReactiveDescription();
        private MipBiasMode _mipMode;

        // --- FSR 4 (plugin nativo) ---
        private bool _useFsr4;
        private ComputeShader _fsr4Prepare;
        private RenderTexture _fsr4Depth, _fsr4Motion;
        private Fsr4Native.FrameParams _fsr4Params;
        private static readonly int IdInDepth = Shader.PropertyToID("_InDepth");
        private static readonly int IdInMotion = Shader.PropertyToID("_InMotion");
        private static readonly int IdOutDepth = Shader.PropertyToID("_OutDepth");
        private static readonly int IdOutMotion = Shader.PropertyToID("_OutMotion");
        private static readonly int IdSize = Shader.PropertyToID("_Size");

        /// <summary>Upscaling temporale (FSR 3.1 o FSR 4): jitter, profondità e vettori di movimento.</summary>
        private bool Temporal => _useFsr || _useFsr4;

        private bool TemporalReady => _useFsr4 ? _upscaled != null : _context != null;

        internal string UpscalerName => _useFsr4 ? "FSR 4" : _useFsr ? "FSR 3.1" : "bilineare";

        /// <summary>Testo per il menu e l'avviso a schermo, ad es. "FSR 4.1.2 · UltraQuality · 2133×1200 → 2560×1440".</summary>
        internal string StatusText
        {
            get
            {
                string name;
                if (_useFsr4)
                {
                    string ver = Fsr4Native.VersionName;
                    if (string.IsNullOrEmpty(ver) || ver == "sconosciuta") name = Plugin.L("FSR 4 (starting...)", "FSR 4 (avvio...)");
                    else name = ver.EndsWith("*") ? "FSR " + ver.TrimEnd('*', ' ') + Plugin.L(" (AMD driver)", " (driver AMD)") : "FSR " + ver;
                }
                else if (_useFsr) name = _upscalerSetting == UpscalerType.FSR4
                    ? Plugin.L("FSR 3.1 (FSR 4 not available)", "FSR 3.1 (FSR 4 non disponibile)")
                    : Plugin.L("FSR 3.1 (built-in)", "FSR 3.1 (integrato)");
                else name = Plugin.L("Bilinear", "Bilineare");

                string q = Temporal ? " · " + _quality : "";
                string res = _lowRes != null ? $" · {_lowRes.width}×{_lowRes.height} → {Screen.width}×{Screen.height}" : "";
                return name + q + res;
            }
        }

        // Contatori diagnostici
        internal int PreCullCount, PresenterPreCullCount, PresenterBlitCount, EndOfFrameBlitCount, SkippedCount, FsrDispatchCount;

        internal RenderTexture Output => Temporal && _upscaled != null ? _upscaled : _lowRes;

        internal bool NeedsRebuild() =>
            _upscalerSetting != Plugin.Upscaler.Value ||
            _quality != Plugin.FsrQuality.Value ||
            _depthSource != Plugin.FsrDepthSource.Value ||
            _splitPostFxSetting != Plugin.FullResPostFx.Value ||
            _useReactive != (_useFsr && Plugin.FsrReactive.Value) ||
            _mipMode != Plugin.MipBiasSetting.Value ||
            (_useFsr4 && Fsr4Native.Failed);

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _mode = PresentMode.Presenter;
            _upscalerSetting = Plugin.Upscaler.Value;
            _quality = Plugin.FsrQuality.Value;
            _depthSource = Plugin.FsrDepthSource.Value;
            _splitPostFxSetting = Plugin.FullResPostFx.Value;
            _mipMode = Plugin.MipBiasSetting.Value;

            if (_upscalerSetting == UpscalerType.FSR4)
            {
                _fsr4Prepare = FsrShaders.GetFsr4Prepare();
                _useFsr4 = _fsr4Prepare != null && !Fsr4Native.Failed && Fsr4Native.IsAvailable();
                if (!_useFsr4)
                    Plugin.Log.LogWarning("FSR 4 non disponibile: uso FSR 3.1.");
            }
            bool wantFsr3 = _upscalerSetting == UpscalerType.FSR3 || (_upscalerSetting == UpscalerType.FSR4 && !_useFsr4);
            _useFsr = wantFsr3 && FsrShaders.Get() != null;
            if (wantFsr3 && !_useFsr)
                Plugin.Log.LogWarning("FSR non disponibile: uso lo stiramento bilineare.");

            if (_mode == PresentMode.Presenter)
            {
                // Creato inattivo: l'eventuale PostProcessLayer va inizializzato prima dell'accensione
                var go = new GameObject("OutwardFSR_Presenter") { hideFlags = HideFlags.DontSave };
                go.SetActive(false);
                _presenter = go.AddComponent<Camera>();
                _presenter.cullingMask = 0;
                _presenter.clearFlags = CameraClearFlags.Nothing;
                _presenter.depth = _cam.depth + 0.01f;
                _presenter.useOcclusionCulling = false;
                _presenter.allowMSAA = false;
                _presenter.renderingPath = RenderingPath.Forward;
                _presenter.fieldOfView = _cam.fieldOfView;
                _presenter.nearClipPlane = _cam.nearClipPlane;
                _presenter.farClipPlane = _cam.farClipPlane;
                go.AddComponent<LowResPresenter>().Owner = this;

                if (_splitPostFxSetting)
                    _postFx = PostFxSplit.TryCreate(_cam, _presenter);
                // Con il post-processing a piena risoluzione il presenter lavora in HDR
                _presenter.allowHDR = _postFx != null;

                // La copia a schermo avviene prima che il presenter disegni qualsiasi cosa
                _presentCb = new CommandBuffer { name = "OutwardFSR: present" };
                _presenter.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, _presentCb);
                go.SetActive(true);
            }

            if (Temporal)
            {
                // FSR ha bisogno di profondità e vettori di movimento (Outward li genera già, ma per sicurezza)
                _originalDepthMode = _cam.depthTextureMode;
                _cam.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

                // FSR fa anche da antialiasing: disattiviamo lo SMAA del gioco
                _ppl = GetComponent<PostProcessLayer>();
                if (_ppl != null)
                {
                    _originalAA = _ppl.antialiasingMode;
                    _ppl.antialiasingMode = PostProcessLayer.Antialiasing.None;
                }

                // FSR viene eseguito alla fine del rendering della telecamera, quando
                // colore, profondità e vettori di movimento sono ancora disponibili.
                _fsrCb = new CommandBuffer { name = "OutwardFSR: " + UpscalerName };
                _cam.AddCommandBuffer(CameraEvent.AfterImageEffects, _fsrCb);

                // Copia della scena prima dei trasparenti: confrontata con l'immagine finale
                // genera la maschera reattiva (dove fuoco/particelle/vetri devono "seguire" il frame attuale)
                _useReactive = _useFsr && Plugin.FsrReactive.Value;
                if (_useReactive)
                {
                    _opaqueCb = new CommandBuffer { name = "OutwardFSR: opaque copy" };
                    _cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, _opaqueCb);
                }
            }

            Plugin.Log.LogInfo($"Upscaling attivo su '{_cam.name}': {(Temporal ? UpscalerName + " " + _quality : "bilineare")}, post-processing {(_postFx != null ? "a piena risoluzione" : "a bassa risoluzione")}");
            StartCoroutine(EndOfFrameLoop());
        }

        private Vector2Int ComputeRenderSize()
        {
            if (Temporal)
            {
                Fsr3Upscaler.GetRenderResolutionFromQualityMode(out int w, out int h, Screen.width, Screen.height, _quality);
                return new Vector2Int(Mathf.Max(1, w), Mathf.Max(1, h));
            }

            float scale = Mathf.Clamp(Plugin.RenderScale.Value, 0.33f, 1f);
            return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale)), Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale)));
        }

        // In OnPreCull è troppo tardi per cambiare destinazione: la impostiamo in LateUpdate.
        private void LateUpdate()
        {
            if (_cam.targetTexture != null && _cam.targetTexture != _lowRes)
                return; // il gioco usa già una sua RenderTexture: non tocchiamo nulla

            var size = ComputeRenderSize();
            EnsureTexture(size.x, size.y);
            if (Temporal) EnsureFsr(new Vector2Int(Screen.width, Screen.height), size);
            _cam.targetTexture = _lowRes;
        }

        private void OnPreCull()
        {
            if (_cam.targetTexture != _lowRes)
            {
                SkippedCount++;
                return;
            }
            PreCullCount++;

            if (Temporal && TemporalReady)
            {
                ApplyJitter();
                if (_useFsr4) BuildFsr4Commands();
                else BuildFsrCommands();
            }
        }

        private void ApplyJitter()
        {
            int phases = Fsr3Upscaler.GetJitterPhaseCount(_renderSize.x, _displaySize.x);
            Fsr3Upscaler.GetJitterOffset(out float jx, out float jy, Time.frameCount, phases);
            _desc.JitterOffset = new Vector2(jx, jy);

            var jitter = Matrix4x4.Translate(new Vector3(2f * jx / _renderSize.x, 2f * jy / _renderSize.y, 0f));
            _cam.nonJitteredProjectionMatrix = _cam.projectionMatrix;
            _cam.projectionMatrix = jitter * _cam.nonJitteredProjectionMatrix;
            _cam.useJitteredProjectionMatrixForTransparentRendering = true;
        }

        private void BuildFsrCommands()
        {
            _fsrCb.Clear();

            _desc.Color = new ResourceView(_lowRes);
            _desc.Depth = _depthSource == DepthSource.CameraDepthTexture
                ? new ResourceView(BuiltinRenderTextureType.Depth, RenderTextureSubElement.Depth)
                : new ResourceView(BuiltinRenderTextureType.CameraTarget, RenderTextureSubElement.Depth);
            _desc.MotionVectors = new ResourceView(BuiltinRenderTextureType.MotionVectors);
            _desc.Exposure = ResourceView.Unassigned;
            _desc.Reactive = ResourceView.Unassigned;
            _desc.TransparencyAndComposition = ResourceView.Unassigned;
            _desc.Output = new ResourceView(_upscaled);

            _desc.PreExposure = 1f;
            _desc.EnableSharpening = Plugin.FsrSharpen.Value;
            _desc.Sharpness = Mathf.Clamp01(Plugin.FsrSharpness.Value);
            _desc.MotionVectorScale = new Vector2(-_renderSize.x, -_renderSize.y);
            _desc.RenderSize = _renderSize;
            _desc.UpscaleSize = _displaySize;
            _desc.FrameTimeDelta = Time.unscaledDeltaTime;
            _desc.CameraNear = _cam.nearClipPlane;
            _desc.CameraFar = _cam.farClipPlane;
            _desc.CameraFovAngleVertical = _cam.fieldOfView * Mathf.Deg2Rad;
            _desc.ViewSpaceToMetersFactor = 1f;
            _desc.VelocityFactor = 1f;
            _desc.Reset = _resetHistory;
            _desc.Flags = Plugin.FsrDebugView.Value ? Fsr3Upscaler.DispatchFlags.DrawDebugView : 0;
            _desc.EnableAutoReactive = Plugin.FsrAutoTcr.Value && _colorOpaque != null;
            if (_desc.EnableAutoReactive)
            {
                _desc.ColorOpaqueOnly = new ResourceView(_colorOpaque);
                _desc.AutoTcThreshold = 0.05f;
                _desc.AutoTcScale = 1.0f;
                _desc.AutoReactiveScale = 5.0f;
                _desc.AutoReactiveMax = 0.9f;
            }
            _resetHistory = false;

            if (SystemInfo.usesReversedZBuffer)
                (_desc.CameraNear, _desc.CameraFar) = (_desc.CameraFar, _desc.CameraNear);

            if (_useReactive && _colorOpaque != null)
            {
                _fsrCb.GetTemporaryRT(Fsr3ShaderIDs.UavAutoReactive, _renderSize.x, _renderSize.y, 0,
                    FilterMode.Point, UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm, 1, true);
                _genDesc.ColorOpaqueOnly = new ResourceView(_colorOpaque);
                _genDesc.ColorPreUpscale = new ResourceView(_lowRes);
                _genDesc.OutReactive = new ResourceView(Fsr3ShaderIDs.UavAutoReactive);
                _genDesc.RenderSize = _renderSize;
                _genDesc.Scale = 0.5f;
                _genDesc.CutoffThreshold = 0.2f;
                _genDesc.BinaryValue = 0.9f;
                _genDesc.Flags = Fsr3Upscaler.GenerateReactiveFlags.ApplyTonemap |
                                 Fsr3Upscaler.GenerateReactiveFlags.ApplyThreshold |
                                 Fsr3Upscaler.GenerateReactiveFlags.UseComponentsMax;
                _context.GenerateReactiveMask(_genDesc, _fsrCb);
                _desc.Reactive = new ResourceView(Fsr3ShaderIDs.UavAutoReactive);
            }

            _context.Dispatch(_desc, _fsrCb);
            if (_useReactive && _colorOpaque != null)
                _fsrCb.ReleaseTemporaryRT(Fsr3ShaderIDs.UavAutoReactive);
            _fsrCb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            FsrDispatchCount++;
        }

        private void BuildFsr4Commands()
        {
            _fsrCb.Clear();
            int w = _renderSize.x, h = _renderSize.y;

            // 1) Profondità e vettori di movimento in texture che il plugin nativo può leggere
            var depth = _depthSource == DepthSource.CameraDepthTexture
                ? new RenderTargetIdentifier(BuiltinRenderTextureType.Depth)
                : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
            _fsrCb.SetComputeTextureParam(_fsr4Prepare, 0, IdInDepth, depth, 0, RenderTextureSubElement.Depth);
            _fsrCb.SetComputeTextureParam(_fsr4Prepare, 0, IdInMotion, BuiltinRenderTextureType.MotionVectors);
            _fsrCb.SetComputeTextureParam(_fsr4Prepare, 0, IdOutDepth, _fsr4Depth);
            _fsrCb.SetComputeTextureParam(_fsr4Prepare, 0, IdOutMotion, _fsr4Motion);
            _fsrCb.SetComputeIntParams(_fsr4Prepare, IdSize, w, h);
            _fsrCb.DispatchCompute(_fsr4Prepare, 0, (w + 7) / 8, (h + 7) / 8, 1);

            // 2) FSR 4 (DirectX 12) tramite il plugin nativo
            var p = _fsr4Params;
            p.JitterX = _desc.JitterOffset.x;
            p.JitterY = _desc.JitterOffset.y;
            p.FrameTimeMs = Time.unscaledDeltaTime * 1000f;
            p.Sharpness = Mathf.Clamp01(Plugin.FsrSharpness.Value);
            p.EnableSharpening = Plugin.FsrSharpen.Value ? 1 : 0;
            p.CameraNear = _cam.nearClipPlane;
            p.CameraFar = _cam.farClipPlane;
            p.FovY = _cam.fieldOfView * Mathf.Deg2Rad;
            p.Reset = _resetHistory ? 1 : 0;
            _resetHistory = false;
            _fsrCb.IssuePluginEventAndData(Fsr4Native.EventFunc, Fsr4Native.EventFrame, Fsr4Native.Push(ref p));
            _fsrCb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            FsrDispatchCount++;
        }

        private static RenderTexture NewRT(string name, int w, int h, RenderTextureFormat format, bool uav)
        {
            var rt = new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear)
            {
                name = name,
                enableRandomWrite = uav,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            rt.Create();
            return rt;
        }

        private void EnsureFsr4(Vector2Int display, Vector2Int render)
        {
            _fsr4Depth = NewRT("OutwardFSR4_Depth", render.x, render.y, RenderTextureFormat.RFloat, true);
            _fsr4Motion = NewRT("OutwardFSR4_Motion", render.x, render.y, RenderTextureFormat.RGHalf, true);

            int flags = SystemInfo.usesReversedZBuffer ? Fsr4Native.FlagDepthInverted : 0;
            if (_postFx != null) flags |= Fsr4Native.FlagHdr | Fsr4Native.FlagAutoExposure;

            // I puntatori nativi non cambiano finché le texture esistono: li leggiamo una volta sola
            _fsr4Params = new Fsr4Native.FrameParams
            {
                Color = _lowRes.GetNativeTexturePtr(),
                Depth = _fsr4Depth.GetNativeTexturePtr(),
                Motion = _fsr4Motion.GetNativeTexturePtr(),
                Output = _upscaled.GetNativeTexturePtr(),
                RenderW = render.x,
                RenderH = render.y,
                DisplayW = display.x,
                DisplayH = display.y,
                MvScaleX = -render.x,
                MvScaleY = -render.y,
                CreateFlags = flags
            };
        }

        private void EnsureFsr(Vector2Int display, Vector2Int render)
        {
            if (TemporalReady && display == _displaySize && render == _renderSize)
                return;

            DestroyFsr();
            _displaySize = display;
            _renderSize = render;

            _upscaled = new RenderTexture(display.x, display.y, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "OutwardFSR_Upscaled",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            _upscaled.Create();

            // Con il post-processing separato FSR riceve l'immagine HDR (prima del tonemapping);
            // altrimenti riceve l'immagine finale già in LDR.
            if (_useFsr4)
            {
                EnsureFsr4(display, render);
            }
            else
            {
                var flags = _postFx != null
                    ? Fsr3Upscaler.InitializationFlags.EnableHighDynamicRange | Fsr3Upscaler.InitializationFlags.EnableAutoExposure
                    : 0;
                _context = Fsr3Upscaler.CreateContext(display, render, FsrShaders.Get(), flags);
            }
            if (_useReactive)
            {
                _colorOpaque = new RenderTexture(render.x, render.y, 0, _lowRes != null ? _lowRes.format : RenderTextureFormat.DefaultHDR)
                {
                    name = "OutwardFSR_OpaqueOnly",
                    hideFlags = HideFlags.DontSave
                };
                _colorOpaque.Create();
                _opaqueCb.Clear();
                _opaqueCb.Blit(BuiltinRenderTextureType.CameraTarget, _colorOpaque);
            }

            MipBias.Apply(MipBias.Compute(_mipMode, render.x, display.x));

            _resetHistory = true;
            RebuildPresentBuffer();
            Plugin.Log.LogInfo($"{UpscalerName} pronto: {render.x}x{render.y} -> {display.x}x{display.y} ({_quality}), maschera reattiva {(_useReactive ? "attiva" : "spenta")}, mipmap bias {MipBias.CurrentBias:F2}");
        }

        private void DestroyFsr()
        {
            if (_context != null)
            {
                _context.Destroy();
                _context = null;
            }
            if (_upscaled != null)
            {
                _upscaled.Release();
                Destroy(_upscaled);
                _upscaled = null;
            }
            if (_colorOpaque != null)
            {
                _colorOpaque.Release();
                Destroy(_colorOpaque);
                _colorOpaque = null;
            }
            ReleaseRT(ref _fsr4Depth);
            ReleaseRT(ref _fsr4Motion);
        }

        private static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        private void RebuildPresentBuffer()
        {
            if (_presentCb == null) return;
            _presentCb.Clear();
            var img = Output;
            if (img != null)
                _presentCb.Blit(img, BuiltinRenderTextureType.CameraTarget);
        }

        /// <summary>La scena è finita: ripristiniamo la telecamera così gli script
        /// del gioco (mira, mouse, UI) vedono la risoluzione reale.</summary>
        internal void RestoreTarget()
        {
            if (_cam == null) return;
            if (_lowRes != null && _cam.targetTexture == _lowRes)
                _cam.targetTexture = null;
            if (Temporal)
                _cam.ResetProjectionMatrix();
        }

        private IEnumerator EndOfFrameLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                RestoreTarget();
                if (_mode == PresentMode.FineFrame && Output != null)
                {
                    Graphics.Blit(Output, (RenderTexture)null);
                    EndOfFrameBlitCount++;
                }
            }
        }

        internal void LogStats()
        {
            Plugin.Log.LogInfo(
                $"[stat 2s] frame ridotti={PreCullCount} saltati={SkippedCount} FSR={FsrDispatchCount} presenter={PresenterPreCullCount}/{PresenterBlitCount} fineFrame={EndOfFrameBlitCount} | " +
                $"render={(_lowRes ? $"{_lowRes.width}x{_lowRes.height}" : "-")} output={(Output ? $"{Output.width}x{Output.height}" : "-")}");
            PreCullCount = SkippedCount = PresenterPreCullCount = PresenterBlitCount = EndOfFrameBlitCount = FsrDispatchCount = 0;
        }

        /// <summary>Salva in PNG l'immagine renderizzata (bassa risoluzione) e quella finale (upscalata).</summary>
        internal void SaveSnapshot()
        {
            Save(_lowRes, "OutwardFSR_lowres.png");
            if (Temporal) Save(_upscaled, "OutwardFSR_fsr.png");
        }

        private static void Save(RenderTexture src, string fileName)
        {
            if (src == null)
            {
                Plugin.Log.LogWarning($"Nessuna immagine da salvare per {fileName}.");
                return;
            }

            var ldr = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, ldr);
            var prev = RenderTexture.active;
            RenderTexture.active = ldr;
            var tex = new Texture2D(ldr.width, ldr.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, ldr.width, ldr.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(ldr);

            var path = Path.Combine(Paths.BepInExRootPath, fileName);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Plugin.Log.LogInfo($"Immagine salvata in {path}");
        }

        private void EnsureTexture(int w, int h)
        {
            if (_lowRes != null && _lowRes.width == w && _lowRes.height == h)
                return;

            ReleaseTexture();
            var format = _cam.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
            _lowRes = new RenderTexture(w, h, 24, format)
            {
                name = "OutwardFSR_LowRes",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            _lowRes.Create();
            _resetHistory = true;
            RebuildPresentBuffer();
            Plugin.Log.LogInfo($"Render a {w}x{h} (schermo {Screen.width}x{Screen.height})");
        }

        private void ReleaseTexture()
        {
            if (_lowRes == null) return;
            if (_cam != null && _cam.targetTexture == _lowRes)
                _cam.targetTexture = null;
            _lowRes.Release();
            Destroy(_lowRes);
            _lowRes = null;
        }

        private void OnDestroy()
        {
            if (_cam != null)
            {
                RestoreTarget();
                if (_fsrCb != null) _cam.RemoveCommandBuffer(CameraEvent.AfterImageEffects, _fsrCb);
                if (_opaqueCb != null) _cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, _opaqueCb);
                if (Temporal) _cam.depthTextureMode = _originalDepthMode;
            }
            if (_ppl != null) _ppl.antialiasingMode = _originalAA;
            _postFx?.Dispose();
            _postFx = null;

            _fsrCb?.Release();
            _fsrCb = null;
            _opaqueCb?.Release();
            _opaqueCb = null;
            MipBias.Remove();
            // Il plugin nativo libera contesto e texture condivise sul thread di rendering,
            // prima che Unity distrugga le texture del gioco a cui puntano i parametri
            if (_useFsr4) GL.IssuePluginEvent(Fsr4Native.EventFunc, Fsr4Native.EventRelease);
            DestroyFsr();
            ReleaseTexture();

            if (_presenter != null)
                Destroy(_presenter.gameObject);
            _presentCb?.Release();
            _presentCb = null;
        }
    }

    /// <summary>Telecamera vuota che copia l'immagine finale sullo schermo, prima dell'interfaccia.</summary>
    internal class LowResPresenter : MonoBehaviour
    {
        internal LowResRenderer Owner;

        private void OnPreCull()
        {
            if (Owner == null) return;
            Owner.PresenterPreCullCount++;
            Owner.RestoreTarget();
        }

        private void OnPostRender()
        {
            if (Owner != null) Owner.PresenterBlitCount++;
        }
    }
}
