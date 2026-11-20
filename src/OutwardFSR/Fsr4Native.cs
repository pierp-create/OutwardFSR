using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace OutwardFSR
{
    /// <summary>
    /// Collegamento con OutwardFSR4Native.dll: il plugin C++ che esegue AMD FSR 4
    /// (tramite le DLL ufficiali AMD per DirectX 12) partendo dal rendering DirectX 11 del gioco.
    /// </summary>
    internal static class Fsr4Native
    {
        private const string Dll = "OutwardFSR4Native";
        public const int EventFrame = 1;
        public const int EventRelease = 2;

        // Flag di creazione del contesto (FFX_UPSCALE_ENABLE_*)
        public const int FlagHdr = 1 << 0;
        public const int FlagDepthInverted = 1 << 3;
        public const int FlagAutoExposure = 1 << 5;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct FrameParams
        {
            public IntPtr Color, Depth, Motion, Output;
            public int RenderW, RenderH, DisplayW, DisplayH;
            public float JitterX, JitterY, MvScaleX, MvScaleY;
            public float FrameTimeMs, Sharpness, CameraNear, CameraFar, FovY;
            public int Reset, EnableSharpening, CreateFlags;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport(Dll)] private static extern IntPtr OFSR4_GetRenderEventFunc();
        [DllImport(Dll)] private static extern int OFSR4_GetStatus();
        [DllImport(Dll)] private static extern int OFSR4_GetVersionName(StringBuilder buffer, int size);
        [DllImport(Dll)] private static extern int OFSR4_PopLog(StringBuilder buffer, int size);
        [DllImport(Dll)] private static extern int OFSR4_GetParamsSize();

        private static bool _attempted, _loaded;
        private static IntPtr _eventFunc;
        private static readonly StringBuilder _buffer = new StringBuilder(1024);

        // Ogni frame usa un blocco di memoria diverso: il thread di rendering di Unity
        // può eseguire il frame precedente mentre prepariamo il successivo.
        private const int RingSize = 4;
        private static IntPtr[] _ring;
        private static int _ringIndex;

        /// <summary>Errore a runtime (dispositivo DX12, DLL AMD, contesto): la mod torna a FSR 3.1.</summary>
        public static bool Failed { get; private set; }

        public static IntPtr EventFunc => _eventFunc;

        public static bool IsAvailable()
        {
            if (_attempted) return _loaded && !Failed;
            _attempted = true;

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            {
                Plugin.Log.LogWarning($"FSR 4: richiede DirectX 11 (il gioco usa {SystemInfo.graphicsDeviceType}).");
                return false;
            }

            string dir = Path.GetDirectoryName(typeof(Fsr4Native).Assembly.Location) ?? "";
            string path = Path.Combine(dir, Dll + ".dll");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning($"FSR 4: {Dll}.dll non trovata in {dir}.");
                return false;
            }
            foreach (var amd in new[] { "amd_fidelityfx_loader_dx12.dll", "amd_fidelityfx_upscaler_dx12.dll" })
            {
                if (!File.Exists(Path.Combine(dir, amd)))
                {
                    Plugin.Log.LogWarning($"FSR 4: {amd} non trovata in {dir}.");
                    return false;
                }
            }

            if (LoadLibraryW(path) == IntPtr.Zero)
            {
                Plugin.Log.LogWarning($"FSR 4: impossibile caricare {path} (errore {Marshal.GetLastWin32Error()}).");
                return false;
            }

            try
            {
                int expected = Marshal.SizeOf(typeof(FrameParams));
                int native = OFSR4_GetParamsSize();
                if (native != expected)
                {
                    Plugin.Log.LogError($"FSR 4: versione del plugin nativo non compatibile ({native} != {expected}).");
                    return false;
                }
                _eventFunc = OFSR4_GetRenderEventFunc();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("FSR 4: plugin nativo non utilizzabile: " + e.Message);
                return false;
            }

            _ring = new IntPtr[RingSize];
            for (int i = 0; i < RingSize; i++)
                _ring[i] = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FrameParams)));

            _loaded = _eventFunc != IntPtr.Zero;
            return _loaded;
        }

        /// <summary>Copia i parametri del frame in memoria non gestita e restituisce il puntatore da passare al plugin.</summary>
        public static IntPtr Push(ref FrameParams p)
        {
            _ringIndex = (_ringIndex + 1) % RingSize;
            Marshal.StructureToPtr(p, _ring[_ringIndex], false);
            return _ring[_ringIndex];
        }

        public static int Status => _loaded ? OFSR4_GetStatus() : 0;

        public static string VersionName
        {
            get
            {
                if (!_loaded) return "";
                _buffer.Length = 0;
                OFSR4_GetVersionName(_buffer, _buffer.Capacity);
                return _buffer.ToString();
            }
        }

        /// <summary>Scrive nel log di BepInEx i messaggi del plugin nativo e rileva gli errori.</summary>
        public static void Pump()
        {
            if (!_loaded) return;
            while (true)
            {
                _buffer.Length = 0;
                if (OFSR4_PopLog(_buffer, _buffer.Capacity) <= 0) break;
                Plugin.Log.LogInfo("[FSR 4] " + _buffer);
            }

            if (!Failed && OFSR4_GetStatus() < 0)
            {
                Failed = true;
                Plugin.Log.LogError($"FSR 4 non disponibile (codice {OFSR4_GetStatus()}): torno a FSR 3.1.");
            }
        }
    }
}
