using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Compila i compute shader di FSR 3.1 (dal pacchetto fidelityfx.fsr di FSR3Unity)
/// in un AssetBundle che la mod OutwardFSR carica all'avvio.
/// Uso interattivo: menu "OutwardFSR > Compila bundle shader".
/// Uso da riga di comando: -batchmode -quit -executeMethod BuildShaderBundle.Build
/// </summary>
public static class BuildShaderBundle
{
    public const string BundleName = "outwardfsr_shaders";

    [MenuItem("OutwardFSR/Compila bundle shader")]
    public static void Build()
    {
        string[] guids = AssetDatabase.FindAssets("t:ComputeShader", new[] { "Packages/fidelityfx.fsr/Shaders" });
        string[] paths = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileName(p).StartsWith("ffx_fsr3upscaler_"))
            .Concat(AssetDatabase.FindAssets("t:ComputeShader", new[] { "Assets/OutwardFSR" }).Select(AssetDatabase.GUIDToAssetPath))
            .OrderBy(p => p)
            .ToArray();

        if (paths.Length == 0)
        {
            Debug.LogError("[OutwardFSR] Nessun compute shader FSR3 trovato in Packages/fidelityfx.fsr/Shaders");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        foreach (var p in paths)
            Debug.Log("[OutwardFSR] Shader incluso: " + p);

        // Cartella "Bundles" accanto alla soluzione Visual Studio
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Bundles"));
        Directory.CreateDirectory(outDir);

        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = paths
        };

        var manifest = BuildPipeline.BuildAssetBundles(outDir, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
        {
            Debug.LogError("[OutwardFSR] Compilazione del bundle FALLITA");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[OutwardFSR] Bundle creato: {Path.Combine(outDir, BundleName)} ({paths.Length} shader)");
    }
}
