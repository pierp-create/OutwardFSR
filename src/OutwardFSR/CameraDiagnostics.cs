using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using Object = UnityEngine.Object;

namespace OutwardFSR
{
    /// <summary>
    /// Raccoglie le informazioni che servono per integrare FSR:
    /// quali telecamere ci sono, come renderizzano, quali effetti PPv2 usano
    /// e quali command buffer il gioco (o altre mod) ha agganciato.
    /// </summary>
    internal static class CameraDiagnostics
    {
        public static void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("===== OutwardFSR: report telecamere =====");
            sb.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} | API: {SystemInfo.graphicsDeviceType} | {SystemInfo.graphicsDeviceVersion}");
            sb.AppendLine($"Compute shader: {SystemInfo.supportsComputeShaders} | Motion vectors: {SystemInfo.supportsMotionVectors} | Formati RT HDR: {SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.DefaultHDR)}");
            sb.AppendLine($"Schermo: {Screen.width}x{Screen.height} modo={Screen.fullScreenMode} | Qualita: {QualitySettings.names[QualitySettings.GetQualityLevel()]} MSAA={QualitySettings.antiAliasing} vSync={QualitySettings.vSyncCount}");

            var cams = Camera.allCameras.OrderBy(c => c.depth).ToArray();
            sb.AppendLine($"Telecamere attive: {cams.Length} | Camera.main = {(Camera.main ? Camera.main.name : "nessuna")}");

            foreach (var cam in cams)
            {
                sb.AppendLine($"--- '{cam.name}'  [{GetPath(cam.transform)}]");
                sb.AppendLine($"    depth={cam.depth} tag={cam.tag} clear={cam.clearFlags} cullingMask=0x{cam.cullingMask:X8}");
                sb.AppendLine($"    renderingPath={cam.renderingPath} (effettivo {cam.actualRenderingPath}) HDR={cam.allowHDR} MSAA={cam.allowMSAA} dynRes={cam.allowDynamicResolution}");
                sb.AppendLine($"    rect={cam.rect} pixelRect={cam.pixelRect} target={DescribeTarget(cam.targetTexture)}");
                sb.AppendLine($"    depthTextureMode={cam.depthTextureMode} fov={cam.fieldOfView:F1} near={cam.nearClipPlane} far={cam.farClipPlane}");

                foreach (var comp in cam.GetComponents<Component>())
                {
                    if (comp == null || comp is Transform || comp is Camera) continue;
                    var beh = comp as Behaviour;
                    sb.AppendLine($"    componente: {comp.GetType().FullName}{(beh != null && !beh.enabled ? " (disattivato)" : "")}");
                }

                var ppl = cam.GetComponent<PostProcessLayer>();
                if (ppl != null)
                {
                    sb.AppendLine($"    PostProcessLayer: AA={ppl.antialiasingMode} volumeLayer=0x{ppl.volumeLayer.value:X8} trigger={(ppl.volumeTrigger ? ppl.volumeTrigger.name : "null")} stopNaN={ppl.stopNaNPropagation}");
                    // Valori effettivi dopo la miscela dei volumi (per confrontare telecamera principale e presenter)
                    try
                    {
                        var bloom = ppl.GetSettings<Bloom>();
                        if (bloom != null)
                            sb.AppendLine($"      Bloom: attivo={bloom.enabled.value} intensita={bloom.intensity.value:F2} soglia={bloom.threshold.value:F2} " +
                                          $"softKnee={bloom.softKnee.value:F2} diffusione={bloom.diffusion.value:F2} fast={bloom.fastMode.value}");
                        var ae = ppl.GetSettings<AutoExposure>();
                        if (ae != null)
                            sb.AppendLine($"      AutoExposure: attivo={ae.enabled.value} min={ae.minLuminance.value:F2} max={ae.maxLuminance.value:F2} " +
                                          $"key={ae.keyValue.value:F2} tipo={ae.eyeAdaptation.value}");
                        var cg = ppl.GetSettings<ColorGrading>();
                        if (cg != null)
                            sb.AppendLine($"      ColorGrading: attivo={cg.enabled.value} modo={cg.gradingMode.value} tonemapper={cg.tonemapper.value} " +
                                          $"postExposure={cg.postExposure.value:F2} saturazione={cg.saturation.value:F1} contrasto={cg.contrast.value:F1} temperatura={cg.temperature.value:F1}");
                    }
                    catch (Exception e)
                    {
                        sb.AppendLine("      (impossibile leggere gli effetti: " + e.Message + ")");
                    }
                }

                foreach (CameraEvent evt in Enum.GetValues(typeof(CameraEvent)))
                {
                    foreach (var buf in cam.GetCommandBuffers(evt))
                        sb.AppendLine($"    CommandBuffer @{evt}: '{buf.name}' ({buf.sizeInBytes} byte)");
                }
            }

            var volumes = Object.FindObjectsOfType<PostProcessVolume>();
            sb.AppendLine($"PostProcessVolume nella scena: {volumes.Length}");
            foreach (var v in volumes)
            {
                sb.Append($"    '{v.name}' globale={v.isGlobal} peso={v.weight:F2} priorita={v.priority} layer={LayerMask.LayerToName(v.gameObject.layer)} effetti:");
                // Non usare v.profile se non è già istanziato: lo creerebbe.
                var profile = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                if (profile != null)
                {
                    foreach (var s in profile.settings)
                        if (s != null && s.active && s.enabled.value)
                            sb.Append(' ').Append(s.GetType().Name);
                }
                sb.AppendLine();
            }

            var canvases = Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas).ToArray();
            sb.AppendLine($"Interfacce (Canvas radice) attive: {canvases.Length}");
            foreach (var c in canvases)
            {
                sb.AppendLine($"    '{GetPath(c.transform)}' modo={c.renderMode} camera={(c.worldCamera ? c.worldCamera.name : "-")} " +
                              $"layer={LayerMask.LayerToName(c.gameObject.layer)} sortingOrder={c.sortingOrder} planeDistance={c.planeDistance}");
            }

            sb.AppendLine("Tutte le telecamere (anche disattivate):");
            foreach (var c in Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.gameObject.scene.IsValid()))
            {
                sb.AppendLine($"    '{GetPath(c.transform)}' attiva={c.isActiveAndEnabled} depth={c.depth} cullingMask=0x{c.cullingMask:X8} target={DescribeTarget(c.targetTexture)}");
            }

            sb.AppendLine("===== fine report =====");
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string DescribeTarget(RenderTexture rt)
        {
            if (rt == null) return "schermo";
            return $"'{rt.name}' {rt.width}x{rt.height} {rt.format} depth={rt.depth}";
        }

        private static string GetPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
