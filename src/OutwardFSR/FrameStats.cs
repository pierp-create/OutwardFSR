using System;
using UnityEngine;

namespace OutwardFSR
{
    /// <summary>
    /// Misura i tempi di frame e ogni N secondi scrive nel log FPS medi, 1% low e picco peggiore,
    /// indicando se l'upscaling era attivo. Serve a confrontare la fluidità con e senza la mod.
    /// </summary>
    internal static class FrameStats
    {
        private const int Capacity = 4096;
        private const float WarmupSeconds = 3f;
        private static readonly float[] Samples = new float[Capacity];
        private static readonly float[] Sorted = new float[Capacity];
        private static int _count;
        private static float _windowStart = -1f;
        private static bool _lastState;
        private static int _spikes;

        public static void Tick(bool enabled, float windowSeconds, bool upscalingActive)
        {
            if (!enabled)
            {
                _windowStart = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (_windowStart < 0f || upscalingActive != _lastState)
            {
                // Nuova finestra (anche quando si preme F9, per non mischiare i due stati).
                // Si parte dopo 3 secondi: l'attivazione/disattivazione causa un attimo di pausa
                // (creazione di FSR, aggiornamento texture) che non fa parte del gameplay.
                _windowStart = now + WarmupSeconds;
                _count = 0;
                _spikes = 0;
                _lastState = upscalingActive;
                return;
            }
            if (now < _windowStart) return;

            float dt = Time.unscaledDeltaTime;
            if (_count > 8)
            {
                // Picco: frame lungo più del doppio della media corrente
                float avgSoFar = 0f;
                for (int i = Math.Max(0, _count - 60); i < _count; i++) avgSoFar += Samples[i];
                avgSoFar /= Math.Min(60, _count);
                if (dt > avgSoFar * 2f) _spikes++;
            }
            if (_count < Capacity) Samples[_count++] = dt;

            if (now - _windowStart >= windowSeconds && _count > 30)
            {
                Report();
                _windowStart = now;
                _count = 0;
                _spikes = 0;
            }
        }

        private static void Report()
        {
            Array.Copy(Samples, Sorted, _count);
            Array.Sort(Sorted, 0, _count);

            double sum = 0;
            for (int i = 0; i < _count; i++) sum += Sorted[i];
            double avg = sum / _count;

            // 1% low = media del peggior 1% dei frame
            int worst = Math.Max(1, _count / 100);
            double worstSum = 0;
            for (int i = _count - worst; i < _count; i++) worstSum += Sorted[i];
            double low1 = worstSum / worst;

            float max = Sorted[_count - 1];
            Plugin.Log.LogInfo(
                $"[frame time] upscaling {(_lastState ? "ON " : "OFF")} | FPS medi {1.0 / avg:F0} | 1% low {1.0 / low1:F0} FPS " +
                $"| frame peggiore {max * 1000f:F1} ms | scatti (>2x media) {_spikes} | campioni {_count}");
        }
    }
}
