# Outward FSR

**AMD FSR 4 and FSR 3.1 upscaling for Outward: Definitive Edition.**

*Italian version below — Versione italiana più sotto.*

Outward FSR renders the game at a lower internal resolution and reconstructs a sharp, stable full-resolution image with AMD FidelityFX Super Resolution. It also acts as the game's anti-aliasing, replacing the built-in SMAA.

## Features

- **AMD FSR 4**, using AMD's official signed DLLs (FSR SDK 2.4, FSR Upscaling 4.1.2). On Radeon RX 9000 series (RDNA 4) GPUs this is the machine-learning upscaler. On other GPUs the AMD DLL automatically uses the best compatible version (FSR 3.1).
- **AMD FSR 3.1**, built into the mod. It works on any DirectX 11 GPU with compute shader support, including NVIDIA and Intel.
- **Automatic fallback:** if FSR 4 cannot start, the mod switches to FSR 3.1 by itself and writes the reason to the BepInEx log.
- **Full-resolution UI:** menus, HUD and text are always drawn at native resolution.
- **Faithful to the original:** by default post-processing, atmosphere and sun are rendered in the game's original order and then upscaled; with a still camera the image shimmers less than the native game. Optional *Full-resolution post-processing* runs these effects after upscaling instead.
- **Texture sharpness (mipmap bias):** keeps textures as detailed as at native resolution.
- **Live toggle with F9**, to compare with native rendering at any time.

The game keeps running in DirectX 11. For FSR 4, the mod opens a DirectX 12 device on the same GPU and shares only the textures FSR needs, synchronized entirely on the GPU.

## Requirements

- Outward: Definitive Edition (default DirectX 11 mode)
- BepInExPack_Outward (installed automatically by r2modman / Thunderstore Mod Manager)
- For FSR 4: Windows 10 (1703 or newer) or Windows 11, and a DirectX 12 capable GPU. The machine-learning version needs a Radeon RX 9000 series GPU.
- Recommended: Outward Config Manager, to change settings in game (F5).

## Settings

Open **F5 → Outward FSR** (with Outward Config Manager), or edit `BepInEx/config/com.outwardfsr.mod.cfg`.

The menu is available in English and Italian (setting *Menu language*, Auto = Windows language).

| Setting | Default | Description |
|---|---|---|
| Menu language | Auto | English, Italiano or Auto (Windows language) |
| Recommended settings | – | Button: puts every graphics option back to its recommended value (click twice to confirm). Menu language and keys are kept |
| Active upscaler | – | Read-only: upscaler in use, its exact version (e.g. FSR 4.1.2) and resolutions |
| Enabled | On | Turns upscaling on or off |
| Toggle key | F9 | Toggles upscaling while playing |
| On-screen notice with F9 | On | Shows the active upscaler for a few seconds when you press F9 |
| Upscaler | FSR 4 | FSR 4, FSR 3.1 or Bilinear (comparison only) |
| Quality | Quality | Internal resolution, see below |
| Sharpness | 0.6 | FSR sharpening strength (0–1) |
| Texture sharpness (mipmap bias) | Moderate | Off / Moderate / AMD. "Moderate" avoids shimmering on fine textures |

| Quality | Internal resolution |
|---|---|
| NativeAA | 100% (anti-aliasing only) |
| UltraQuality | 77% |
| Quality | 67% |
| Balanced | 59% |
| Performance | 50% |
| UltraPerformance | 33% |

Advanced and diagnostic settings appear when *Advanced settings* is ticked in the Config Manager.

## Compatibility

- Tested together with Outward UE5 Overhaul, SideLoader, Sinai's Utility Pack and More Map Details.
- Mods that heavily change the camera or post-processing may conflict. If you see problems, make sure *Full-resolution post-processing* is off (the default) in the advanced settings.
- Not compatible with forcing the game into DirectX 12 or Vulkan (`-force-d3d12`, `-force-vulkan`).

## First start

There is nothing to compile or set up: the shaders are already compiled and included in the package. The first time upscaling turns on, the graphics driver loads the shaders and the FSR 4 model, which can take a moment. After that it is instant.

## Known limitations

- The automatic reactive mask is only used by the built-in FSR 3.1 and is off by default: it reduces trails behind fire and particles but increases shimmering on glass and reflections. FSR 4 handles these cases on its own.
- Toggling with F9 or changing settings recreates FSR, which can cause a short hitch.
- FSR 3.1 can leave light trails around foliage: turn on *Automatic reactive mask (FSR 3.1 only)* to remove them.
- With *Full-resolution post-processing* on, thin details against the sky shimmer more than with it off (the default). *Stable sky edges* reduces it.

## Feedback and bug reports

Please report bugs and suggestions on GitHub: https://github.com/pierp-create/OutwardFSR/issues
Include your GPU, the FSR mode and quality you use, and your `BepInEx/LogOutput.log` if possible.

## Troubleshooting

Open `BepInEx/LogOutput.log` and look for lines starting with `[Outward FSR]` or `[FSR 4]`. A working FSR 4 setup shows `Contesto creato: 4.1.2 …` ("context created"). If FSR 4 fails, the log explains why and the mod switches to FSR 3.1.

## Credits and legal

- Mod by **pierp**, developed with the help of AI (Anthropic Claude). All code was built, tested in game and reviewed by the author.
- The full source code is available on GitHub (see the website link on this page), so anyone can check what the DLLs do. The AMD DLLs are the original files digitally signed by AMD.
- **AMD FSR 4 / AMD FSR SDK 2.4:** `amd_fidelityfx_loader_dx12.dll` and `amd_fidelityfx_upscaler_dx12.dll` are © Advanced Micro Devices, Inc. They are redistributed unmodified under the AMD license in `LICENSE-AMD-FSR.txt`.
- **AMD FidelityFX Super Resolution 3.1** shaders: © Advanced Micro Devices, Inc., MIT license.
- **FSR3Unity** (Unity port of FSR 3.1) by Nico de Poel, MIT license: https://github.com/ndepoel/FSR3Unity
- Full license texts: `LICENSE` (this mod, MIT), `THIRD-PARTY-NOTICES.md` and `LICENSE-AMD-FSR.txt`.

AMD, the AMD Arrow logo, Radeon, RDNA, FidelityFX and combinations thereof are trademarks of Advanced Micro Devices, Inc. This is an unofficial fan-made mod. It is not affiliated with, endorsed by or supported by AMD, Nine Dots Studio or Deep Silver. Use it at your own risk.

---

# Outward FSR (Italiano)

**Upscaling AMD FSR 4 e FSR 3.1 per Outward: Definitive Edition.**

Outward FSR fa renderizzare il gioco a una risoluzione interna più bassa e ricostruisce un'immagine nitida e stabile a risoluzione piena con AMD FidelityFX Super Resolution. Fa anche da antialiasing, al posto dello SMAA del gioco.

## Funzionalità

- **AMD FSR 4**, con le DLL ufficiali firmate da AMD (FSR SDK 2.4, FSR Upscaling 4.1.2). Sulle Radeon serie RX 9000 (RDNA 4) è l'upscaler con machine learning. Sulle altre schede la DLL AMD usa da sola la versione compatibile migliore (FSR 3.1).
- **AMD FSR 3.1**, integrato nella mod. Funziona su qualsiasi scheda DirectX 11 con compute shader, comprese NVIDIA e Intel.
- **Ripiego automatico:** se FSR 4 non può partire, la mod passa da sola a FSR 3.1 e scrive il motivo nel log di BepInEx.
- **Interfaccia a risoluzione piena:** menu, HUD e testi restano sempre alla risoluzione nativa.
- **Fedele all'originale:** di default post-processing, atmosfera e sole vengono calcolati nell'ordine originale del gioco e poi ingranditi; a telecamera ferma l'immagine tremola meno del gioco nativo. L'opzione *Post-processing a piena risoluzione* applica invece questi effetti dopo l'upscaling.
- **Nitidezza delle texture (mipmap bias):** le texture restano dettagliate come alla risoluzione nativa.
- **Attivazione al volo con F9**, per confrontare in qualsiasi momento con il rendering nativo.

Il gioco continua a girare in DirectX 11. Per FSR 4 la mod apre un dispositivo DirectX 12 sulla stessa scheda video e condivide solo le texture che servono a FSR, con la sincronizzazione gestita interamente dalla GPU.

## Requisiti

- Outward: Definitive Edition (modalità DirectX 11 predefinita)
- BepInExPack_Outward (installato in automatico da r2modman / Thunderstore Mod Manager)
- Per FSR 4: Windows 10 (1703 o successivo) o Windows 11 e una scheda video compatibile DirectX 12. La versione con machine learning richiede una Radeon serie RX 9000.
- Consigliato: Outward Config Manager, per cambiare le impostazioni in gioco (F5).

## Impostazioni

Apri **F5 → Outward FSR** (con Outward Config Manager) oppure modifica `BepInEx/config/com.outwardfsr.mod.cfg`.

Il menu è disponibile in italiano e in inglese (impostazione *Lingua del menu*, Auto = lingua di Windows).

| Impostazione | Predefinito | Descrizione |
|---|---|---|
| Lingua del menu | Auto | English, Italiano o Auto (lingua di Windows) |
| Impostazioni consigliate | – | Pulsante: riporta tutte le opzioni grafiche al valore consigliato (clicca due volte per confermare). Lingua del menu e tasti restano invariati |
| Upscaler attivo | – | Solo lettura: upscaler in uso, versione esatta (ad es. FSR 4.1.2) e risoluzioni |
| Attivo | Sì | Accende o spegne l'upscaling |
| Tasto attiva/disattiva | F9 | Accende o spegne l'upscaling durante il gioco |
| Avviso a schermo con F9 | Sì | Mostra per qualche secondo l'upscaler attivo quando premi F9 |
| Upscaler | FSR 4 | FSR 4, FSR 3.1 o Bilinear (solo per confronto) |
| Qualità | Quality | Risoluzione interna, vedi sotto |
| Nitidezza | 0.6 | Intensità della nitidezza di FSR (0–1) |
| Nitidezza texture (mipmap bias) | Moderate | Off / Moderate / AMD. "Moderate" evita lo sfarfallio sulle texture fini |

| Qualità | Risoluzione interna |
|---|---|
| NativeAA | 100% (solo antialiasing) |
| UltraQuality | 77% |
| Quality | 67% |
| Balanced | 59% |
| Performance | 50% |
| UltraPerformance | 33% |

Le opzioni avanzate e di diagnostica compaiono spuntando *Advanced settings* nel Config Manager.

## Compatibilità

- Provata insieme a Outward UE5 Overhaul, SideLoader, Sinai's Utility Pack e More Map Details.
- Le mod che modificano pesantemente la telecamera o il post-processing potrebbero entrare in conflitto. In caso di problemi, controlla che *Post-processing a piena risoluzione* sia spento (predefinito) nelle opzioni avanzate.
- Non è compatibile con il gioco forzato in DirectX 12 o Vulkan (`-force-d3d12`, `-force-vulkan`).

## Primo avvio

Non c'è niente da compilare né da configurare: gli shader sono già compilati e inclusi nel pacchetto. La prima volta che l'upscaling si attiva, il driver video carica gli shader e il modello di FSR 4, e può servire qualche istante. Dopo è immediato.

## Limitazioni note

- La maschera reattiva automatica è usata solo dall'FSR 3.1 integrato ed è spenta di default: riduce le scie dietro fuoco e particelle ma aumenta lo sfarfallio su vetri e riflessi. FSR 4 gestisce questi casi da solo.
- Attivare o disattivare con F9, o cambiare le impostazioni, ricrea FSR e può causare un breve scatto.
- FSR 3.1 può lasciare leggere scie attorno al fogliame: attiva *Maschera reattiva automatica (solo FSR 3.1)* per eliminarle.
- Con *Post-processing a piena risoluzione* acceso, i dettagli sottili contro il cielo tremolano di più che con l'opzione spenta (predefinita). *Bordi contro il cielo stabili* lo riduce.

## Segnalazioni e suggerimenti

Segnala bug e suggerimenti su GitHub: https://github.com/pierp-create/OutwardFSR/issues
Se puoi, indica scheda video, modalità e qualità di FSR che usi, e allega il file `BepInEx/LogOutput.log`.

## Risoluzione dei problemi

Apri `BepInEx/LogOutput.log` e cerca le righe che iniziano con `[Outward FSR]` o `[FSR 4]`. Se FSR 4 funziona compare `Contesto creato: 4.1.2 …`. Se FSR 4 non parte, il log spiega il motivo e la mod passa a FSR 3.1.

## Crediti e note legali

- Mod di **pierp**, sviluppata con l'aiuto dell'intelligenza artificiale (Anthropic Claude). Tutto il codice è stato compilato, provato in gioco e controllato dall'autore.
- Il codice sorgente completo è disponibile su GitHub (vedi il link al sito in questa pagina), così chiunque può verificare cosa fanno le DLL. Le DLL AMD sono i file originali firmati digitalmente da AMD.
- **AMD FSR 4 / AMD FSR SDK 2.4:** `amd_fidelityfx_loader_dx12.dll` e `amd_fidelityfx_upscaler_dx12.dll` sono © Advanced Micro Devices, Inc. e sono ridistribuite senza modifiche secondo la licenza AMD contenuta in `LICENSE-AMD-FSR.txt`.
- Shader di **AMD FidelityFX Super Resolution 3.1:** © Advanced Micro Devices, Inc., licenza MIT.
- **FSR3Unity** (porting Unity di FSR 3.1) di Nico de Poel, licenza MIT: https://github.com/ndepoel/FSR3Unity
- Testi completi delle licenze: `LICENSE` (questa mod, MIT), `THIRD-PARTY-NOTICES.md` e `LICENSE-AMD-FSR.txt`.

AMD, il logo AMD Arrow, Radeon, RDNA, FidelityFX e le loro combinazioni sono marchi di Advanced Micro Devices, Inc. Questa è una mod amatoriale non ufficiale. Non è affiliata, approvata né supportata da AMD, Nine Dots Studio o Deep Silver. La usi a tuo rischio.
