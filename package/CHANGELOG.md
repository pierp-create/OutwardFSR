# Changelog

*Italian version below — Versione italiana più sotto.*

## 1.0.1 — 2026-11-20

- New: the settings menu is available in English and Italian (setting "Menu language", Auto = Windows language).
- Settings are now stored with English names in the .cfg file. **Settings from 1.0.0 are reset to their defaults once.**
- FSR 3.1: the automatic reactive mask is now off by default, because it increased shimmering on glass and reflections.
- New: "Upscaler attivo" line in the F5 menu showing the upscaler in use, its exact version (e.g. FSR 4.1.2) and the resolutions.
- New: on-screen notice for a few seconds when pressing F9, showing the active upscaler or "native" (can be turned off).
- Documentation: added "First start" and "Feedback and bug reports" sections, AI-assistance notice and link to the source code.
- Settings menu: options that only affect FSR 3.1 (reactive mask, reflection flicker reduction, debug view) are now labelled "FSR 3.1 only".
- Settings menu: upscaler names now display correctly as "FSR 4" and "FSR 3.1".

## 1.0.0 — 2026-11-20

First public release.

### Added
- **AMD FSR 4 upscaler** using AMD's official signed DLLs (FSR SDK 2.4, FSR Upscaling 4.1.2). It is the machine-learning upscaler on Radeon RX 9000 series GPUs.
- Native DirectX 11 → DirectX 12 bridge (`OutwardFSR4Native.dll`): the game stays in DirectX 11 and only the textures FSR needs are shared with a DirectX 12 device on the same GPU, synchronized entirely on the GPU.
- Automatic fallback to FSR 3.1 if FSR 4 cannot start, with the reason written to the log.
- The log reports which FSR versions are available, including newer ones delivered by the AMD driver.
- AMD FSR 3.1 upscaler built into the mod, for any DirectX 11 GPU with compute shaders.
- Full-resolution UI and full-resolution post-processing (bloom, color grading and vignette run after upscaling).
- Automatic reactive mask for FSR 3.1, to reduce trails on fire, particles and transparencies.
- Texture sharpness setting (mipmap bias): Off / Moderate / AMD.
- F9 live toggle, and settings in Outward Config Manager (F5) with a basic and an advanced section.
- Diagnostic tools: frame-time statistics (average FPS, 1% low, worst frame), camera report and image capture.

### Compatibility
- The post-processing split uses a Harmony patch instead of extra camera layers, so it works alongside mods such as Outward UE5 Overhaul.

### Development history (pre-release builds, not published)
- 0.1 – 0.2: diagnostics and low-resolution rendering with a full-resolution UI.
- 0.3: AMD FSR 3.1 integration (based on FSR3Unity).
- 0.4: post-processing moved after upscaling, cleaned-up settings menu, installable package.
- 0.5: mipmap bias, frame-time statistics, compatibility with other mods.
- 0.6: FSR 4 bridge (internal tests).

---

# Registro delle modifiche (Italiano)

## 1.0.1 — 20/11/2026

- Novità: il menu delle impostazioni è disponibile in italiano e in inglese (impostazione "Lingua del menu", Auto = lingua di Windows).
- Le impostazioni ora sono salvate con nomi inglesi nel file .cfg. **Le impostazioni della 1.0.0 tornano una volta ai valori predefiniti.**
- FSR 3.1: la maschera reattiva automatica ora è spenta di default, perché aumentava lo sfarfallio su vetri e riflessi.
- Novità: riga "Upscaler attivo" nel menu F5, con l'upscaler in uso, la sua versione esatta (ad es. FSR 4.1.2) e le risoluzioni.
- Novità: avviso a schermo per qualche secondo quando premi F9, con l'upscaler attivo o "nativo" (disattivabile).
- Documentazione: aggiunte le sezioni "Primo avvio" e "Segnalazioni e suggerimenti", la nota sull'aiuto dell'intelligenza artificiale e il link al codice sorgente.
- Menu delle impostazioni: le opzioni che valgono solo per FSR 3.1 (maschera reattiva, riduzione sfarfallio riflessi, vista di debug) ora riportano "solo FSR 3.1".
- Menu delle impostazioni: i nomi degli upscaler ora compaiono correttamente come "FSR 4" e "FSR 3.1".

## 1.0.0 — 20/11/2026

Prima versione pubblica.

### Aggiunto
- **Upscaler AMD FSR 4** con le DLL ufficiali firmate da AMD (FSR SDK 2.4, FSR Upscaling 4.1.2). Sulle Radeon serie RX 9000 è l'upscaler con machine learning.
- Ponte nativo DirectX 11 → DirectX 12 (`OutwardFSR4Native.dll`): il gioco resta in DirectX 11 e solo le texture che servono a FSR vengono condivise con un dispositivo DirectX 12 sulla stessa scheda video, con la sincronizzazione gestita interamente dalla GPU.
- Ripiego automatico su FSR 3.1 se FSR 4 non può partire, con il motivo scritto nel log.
- Il log indica le versioni di FSR disponibili, comprese quelle più recenti fornite dal driver AMD.
- Upscaler AMD FSR 3.1 integrato nella mod, per qualsiasi scheda DirectX 11 con compute shader.
- Interfaccia e post-processing a risoluzione piena (bloom, color grading e vignettatura dopo l'upscaling).
- Maschera reattiva automatica per FSR 3.1, per ridurre le scie su fuoco, particelle e trasparenze.
- Impostazione della nitidezza delle texture (mipmap bias): Disattivato / Moderato / AMD.
- Attivazione al volo con F9 e impostazioni in Outward Config Manager (F5), con una sezione base e una avanzata.
- Strumenti di diagnostica: statistiche dei tempi dei frame (FPS medi, 1% low, frame peggiore), report delle telecamere e cattura delle immagini.

### Compatibilità
- La separazione del post-processing usa una patch Harmony invece di layer aggiuntivi delle telecamere, così funziona insieme a mod come Outward UE5 Overhaul.

### Storia dello sviluppo (versioni di prova, non pubblicate)
- 0.1 – 0.2: diagnostica e rendering a bassa risoluzione con interfaccia a risoluzione piena.
- 0.3: integrazione di AMD FSR 3.1 (basata su FSR3Unity).
- 0.4: post-processing spostato dopo l'upscaling, menu delle impostazioni riordinato, pacchetto installabile.
- 0.5: mipmap bias, statistiche dei tempi dei frame, compatibilità con altre mod.
- 0.6: ponte per FSR 4 (test interni).
