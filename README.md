# Outward FSR

Mod BepInEx per **Outward Definitive Edition** che aggiunge l'upscaling **AMD FSR 4** (DLL ufficiali AMD, tramite un ponte DirectX 11 → DirectX 12) e **FSR 3.1** (integrato, qualsiasi scheda video).

Versione attuale: **1.0.0** (autore: pierp). Il README per i giocatori è in `package/README.md`.

## Struttura

| Cartella | Contenuto |
|---|---|
| `src/OutwardFSR` | La mod (C#, .NET 4.7.2, BepInEx 5) |
| `native/OutwardFSR4Native` | Plugin C++ che esegue FSR 4 in DirectX 12 a partire dalle texture DirectX 11 del gioco |
| `UnityShaders` | Progetto Unity 2020.3.26f1 che compila i compute shader nel bundle `Bundles/outwardfsr_shaders` |
| `Bundles` | Bundle shader già compilato (usato dalla build) |
| `package` | manifest, README, icona e licenza AMD per il pacchetto r2modman/Thunderstore |
| `tools` | Script di supporto |

## Dipendenze esterne (non incluse nel repository)

- **FSR3Unity** → clonare in `FSR3Unity/`: `git clone https://github.com/ndepoel/FSR3Unity FSR3Unity`
- **AMD FSR SDK 2.4.0** → estrarre in `FSR-SDK/FidelityFX-SDK-2.4.0/` (servono `Kits/FidelityFX/api`, `upscalers` e `signedbin`).
- Visual Studio 2022 con i carichi di lavoro *.NET desktop* e *Sviluppo di applicazioni desktop con C++*.
- Il gioco e il profilo r2modman `FSR-Dev`: i percorsi sono in cima a `src/OutwardFSR/OutwardFSR.csproj`.

## Compilare

1. (Solo se cambiano gli shader) apri `UnityShaders` con Unity 2020.3.26f1 e usa il menu *OutwardFSR → Compila bundle shader*.
2. Apri `OutwardFSR.sln`, configurazione **Release**, *Compilazione → Ricompila soluzione*.
3. La mod viene copiata nel profilo `FSR-Dev` e il pacchetto installabile viene creato in `dist/pierp-OutwardFSR-<versione>.zip`.

## Licenze

- Codice di Outward FSR: MIT, vedi `LICENSE`.
- FSR3Unity e shader FSR 3.1: MIT, vedi `package/THIRD-PARTY-NOTICES.md`.
- DLL AMD FSR SDK (non incluse nel repository): licenza AMD, vedi `package/LICENSE-AMD-FSR.txt`.
