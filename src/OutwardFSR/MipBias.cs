using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OutwardFSR
{
    internal enum MipBiasMode
    {
        /// <summary>Nessuna correzione.</summary>
        [System.ComponentModel.Description("Off")]
        Off,
        /// <summary>log2(render/schermo): compensa solo la risoluzione ridotta.</summary>
        [System.ComponentModel.Description("Moderate")]
        Moderate,
        /// <summary>log2(render/schermo) - 1: valore raccomandato da AMD per FSR.</summary>
        [System.ComponentModel.Description("AMD")]
        AMD
    }

    /// <summary>
    /// Applica un mipmap bias negativo alle texture del gioco, così restano nitide come a risoluzione
    /// nativa anche se la scena viene renderizzata più piccola. La scansione delle texture è costosa:
    /// viene fatta solo all'attivazione e quando si carica una scena, mai durante il gioco.
    /// </summary>
    internal static class MipBias
    {
        private static readonly Dictionary<Texture, float> Original = new Dictionary<Texture, float>();
        private static float _bias;
        private static bool _hooked;

        public static float CurrentBias => _bias;

        public static float Compute(MipBiasMode mode, int renderWidth, int displayWidth)
        {
            if (mode == MipBiasMode.Off || renderWidth >= displayWidth) return 0f;
            float b = Mathf.Log((float)renderWidth / displayWidth, 2f);
            if (mode == MipBiasMode.AMD) b -= 1f;
            return Mathf.Clamp(b, -3f, 0f);
        }

        public static void Apply(float bias)
        {
            if (Mathf.Approximately(bias, _bias) && Original.Count > 0) return;
            _bias = bias;
            if (!_hooked)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                _hooked = true;
            }
            Scan();
        }

        public static void Remove()
        {
            int restored = 0;
            foreach (var kv in Original)
            {
                if (kv.Key == null) continue;
                kv.Key.mipMapBias = kv.Value;
                restored++;
            }
            Original.Clear();
            _bias = 0f;
            if (_hooked)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _hooked = false;
            }
            if (restored > 0) Plugin.Log.LogInfo($"Mipmap bias rimosso da {restored} texture.");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Scan();

        private static void Scan()
        {
            if (Mathf.Approximately(_bias, 0f))
            {
                // Bias nullo: ripristina eventuali valori modificati
                foreach (var kv in Original)
                    if (kv.Key != null) kv.Key.mipMapBias = kv.Value;
                Original.Clear();
                return;
            }

            int changed = 0;
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture>())
            {
                if (tex == null || tex is RenderTexture) continue;
                if (!(tex is Texture2D || tex is Texture2DArray)) continue;
                if (tex.mipmapCount <= 1) continue; // UI e texture senza mipmap: non cambiano

                if (!Original.TryGetValue(tex, out float orig))
                {
                    orig = tex.mipMapBias;
                    Original[tex] = orig;
                }
                tex.mipMapBias = orig + _bias;
                changed++;
            }

            // Pulizia delle texture distrutte (scene scaricate)
            var dead = new List<Texture>();
            foreach (var kv in Original) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var d in dead) Original.Remove(d);

            Plugin.Log.LogInfo($"Mipmap bias {_bias:F2} applicato a {changed} texture.");
        }
    }
}
