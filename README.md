# Outward FSR

**AMD FSR 4 and FSR 3.1 upscaling for Outward: Definitive Edition** (BepInEx mod).

*Italian version below — Versione italiana più sotto.*

- **FSR 4** uses AMD's official signed DLLs (FSR SDK 2.4) through a small native DirectX 11 → DirectX 12 bridge. The game keeps running in DirectX 11. On Radeon RX 9000 series GPUs it is the machine-learning upscaler.
- **FSR 3.1** is built into the mod (based on FSR3Unity) and works on any DirectX 11 GPU. It is also the automatic fallback if FSR 4 cannot start.
- The UI and the final post-processing (bloom, color grading, vignette) stay at full resolution.

**Players:** install the mod from Thunderstore / r2modman. The full user guide is in [`package/README.md`](package/README.md) and the version history in [`package/CHANGELOG.md`](package/CHANGELOG.md).
**Bugs and suggestions:** please open an [issue](https://github.com/pierp-create/OutwardFSR/issues).

## Project structure

| Folder | Contents |
|---|---|
| `src/OutwardFSR` | The mod (C#, .NET 4.7.2, BepInEx 5) |
| `native/OutwardFSR4Native` | C++ plugin that runs FSR 4 in DirectX 12 using the game's DirectX 11 textures |
| `UnityShaders` | Unity 2020.3.26f1 project that compiles the compute shaders into `Bundles/outwardfsr_shaders` |
| `Bundles` | Precompiled shader bundle (used by the build) |
| `package` | Manifest, README, changelog, icon and licenses for the r2modman / Thunderstore package |
| `tools` | Helper scripts |

## External dependencies (not included in this repository)

- **FSR3Unity** → clone into `FSR3Unity/`: `git clone https://github.com/ndepoel/FSR3Unity FSR3Unity`
- **AMD FSR SDK 2.4.0** → extract into `FSR-SDK/FidelityFX-SDK-2.4.0/` (needed: `Kits/FidelityFX/api`, `upscalers` and `signedbin`).
- Visual Studio 2022 with the *.NET desktop development* and *Desktop development with C++* workloads.
- The game and an r2modman profile named `FSR-Dev`: the paths are at the top of `src/OutwardFSR/OutwardFSR.csproj`.

## Building

1. (Only if the shaders change) open `UnityShaders` with Unity 2020.3.26f1 and run *OutwardFSR → Compila bundle shader*.
2. Open `OutwardFSR.sln`, select the **Release** configuration, then *Build → Rebuild Solution*.
3. The mod is copied into the `FSR-Dev` profile and the installable package is created as `dist/pierp-OutwardFSR-<version>.zip`.

## AI assistance

This mod was developed with the help of AI (Anthropic Claude). The author built every version, tested it in game and reviewed the code. The full source code is here so anyone can check what the DLLs do.

## Licenses

- Outward FSR code: MIT, see [`LICENSE`](LICENSE).
- FSR3Unity and FSR 3.1 shaders: MIT, see [`package/THIRD-PARTY-NOTICES.md`](package/THIRD-PARTY-NOTICES.md).
- AMD FSR SDK DLLs (not included in this repository): AMD license, see [`package/LICENSE-AMD-FSR.txt`](package/LICENSE-AMD-FSR.txt).

AMD, Radeon, RDNA and FidelityFX are trademarks of Advanced Micro Devices, Inc. This is an unofficial fan-made mod, not affiliated with or endorsed by AMD, Nine Dots Studio or Deep Silver.

---

# Outward FSR (Italiano)

**Upscaling AMD FSR 4 e FSR 3.1 per Outward: Definitive Edition** (mod BepInEx).

- **FSR 4** usa le DLL ufficiali firmate da AMD (FSR SDK 2.4), tramite un piccolo ponte nativo DirectX 11 → DirectX 12. Il gioco continua a girare in DirectX 11. Sulle Radeon serie RX 9000 è l'upscaler con machine learning.
- **FSR 3.1** è integrato nella mod (basato su FSR3Unity) e funziona su qualsiasi scheda DirectX 11. È anche il ripiego automatico se FSR 4 non può partire.
- L'interfaccia e il post-processing finale (bloom, color grading, vignettatura) restano a risoluzione piena.

**Giocatori:** installate la mod da Thunderstore / r2modman. La guida completa è in [`package/README.md`](package/README.md) e la storia delle versioni in [`package/CHANGELOG.md`](package/CHANGELOG.md).
**Bug e suggerimenti:** aprite una [issue](https://github.com/pierp-create/OutwardFSR/issues).

## Struttura del progetto

| Cartella | Contenuto |
|---|---|
| `src/OutwardFSR` | La mod (C#, .NET 4.7.2, BepInEx 5) |
| `native/OutwardFSR4Native` | Plugin C++ che esegue FSR 4 in DirectX 12 a partire dalle texture DirectX 11 del gioco |
| `UnityShaders` | Progetto Unity 2020.3.26f1 che compila i compute shader nel bundle `Bundles/outwardfsr_shaders` |
| `Bundles` | Bundle shader già compilato (usato dalla build) |
| `package` | Manifest, README, changelog, icona e licenze per il pacchetto r2modman / Thunderstore |
| `tools` | Script di supporto |

## Dipendenze esterne (non incluse nel repository)

- **FSR3Unity** → clonare in `FSR3Unity/`: `git clone https://github.com/ndepoel/FSR3Unity FSR3Unity`
- **AMD FSR SDK 2.4.0** → estrarre in `FSR-SDK/FidelityFX-SDK-2.4.0/` (servono `Kits/FidelityFX/api`, `upscalers` e `signedbin`).
- Visual Studio 2022 con i carichi di lavoro *Sviluppo desktop .NET* e *Sviluppo di applicazioni desktop con C++*.
- Il gioco e un profilo r2modman chiamato `FSR-Dev`: i percorsi sono in cima a `src/OutwardFSR/OutwardFSR.csproj`.

## Compilare

1. (Solo se cambiano gli shader) aprire `UnityShaders` con Unity 2020.3.26f1 e usare il menu *OutwardFSR → Compila bundle shader*.
2. Aprire `OutwardFSR.sln`, scegliere la configurazione **Release**, poi *Compilazione → Ricompila soluzione*.
3. La mod viene copiata nel profilo `FSR-Dev` e il pacchetto installabile viene creato come `dist/pierp-OutwardFSR-<versione>.zip`.

## Aiuto dell'intelligenza artificiale

Questa mod è stata sviluppata con l'aiuto dell'intelligenza artificiale (Anthropic Claude). L'autore ha compilato ogni versione, l'ha provata in gioco e ha controllato il codice. Il codice sorgente completo è qui, così chiunque può verificare cosa fanno le DLL.

## Licenze

- Codice di Outward FSR: MIT, vedi [`LICENSE`](LICENSE).
- FSR3Unity e shader FSR 3.1: MIT, vedi [`package/THIRD-PARTY-NOTICES.md`](package/THIRD-PARTY-NOTICES.md).
- DLL AMD FSR SDK (non incluse nel repository): licenza AMD, vedi [`package/LICENSE-AMD-FSR.txt`](package/LICENSE-AMD-FSR.txt).

AMD, Radeon, RDNA e FidelityFX sono marchi di Advanced Micro Devices, Inc. Questa è una mod amatoriale non ufficiale, non affiliata né approvata da AMD, Nine Dots Studio o Deep Silver.
