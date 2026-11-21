# Changelog

*Italian version below — Versione italiana più sotto.*

## 1.0.3 — 2026-11-21

- **Full-resolution post-processing is now off by default** (existing settings are switched off once on update). Measured on uncompressed frame captures with a still camera: with it off, shimmering is about 2.5 times lower than with it on, and lower than the native game; post-processing, atmosphere and sun are rendered in the original game's order, so colours and sun glow match the original. You can still turn it on in the advanced settings.
- Fixed: since 1.0.2, edges against the sky (poles, pillars, roofs, foliage) could look stair-stepped with full-resolution post-processing on. The atmosphere, which also draws the sky, now uses a smooth full-resolution depth with the camera jitter compensated.
- New advanced option *Stable sky edges* (on by default, only with full-resolution post-processing): stabilises the edge between objects and sky over time, halving its shimmer. Sun rays now receive the real scene depth too.
- New diagnostic option *Frame sequence key* (unbound by default): saves 30 consecutive uncompressed frames to `BepInEx/OutwardFSR_captures`, useful for bug reports about shimmering.

## 1.0.2 — 2026-11-21

- Fixed: with *Full-resolution post-processing* on, the sun glow and the atmosphere (Time of Day scattering and sun rays) now run after tonemapping as in the original game. The sun has its full warm halo again and the image is no longer darker than native.
- Fixed: the scene depth was read from a texture that was always empty at that point of the frame. The atmosphere now receives the real depth.
- FSR 4 keeps working without scene depth as in 1.0.1, because it keeps thin foliage sharper. New advanced option *Scene depth for FSR 4* (off by default) to try it with depth: it can reduce trails around moving objects, but foliage looks less sharp.
- FSR 3.1: if you see trails around foliage, turn on *Automatic reactive mask (FSR 3.1 only)*.

## 1.0.1 — 2026-11-20

- New: the settings menu is available in English and Italian (setting "Menu language", Auto = Windows language).
- Settings are now stored with English names in the .cfg file. **Settings from 1.0.0 are reset to their defaults once.**
- FSR 3.1: the automatic reactive mask is now off by default, because it increased shimmering on glass and reflections.
- New: "Upscaler attivo" line in the F5 menu showing the upscaler in use, its exact version (e.g. FSR 4.1.2) and the resolutions.
- New: on-screen notice for a few seconds when pressing F9, showing the active upscaler or "native" (can be turned off).
- Documentation: added "First start" and "Feedback and bug reports" sections, AI-assistance notice and link to the source code.
- Settings menu: options that only affect FSR 3.1 (reactive mask, reflection flicker reduction, debug view) are now labelled "FSR 3.1 only".
- Settings menu: option values now display correctly ("FSR 4", "FSR 3.1", "AMD", "Native AA (100%)"…), and quality presets show their internal resolution.

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

## 1.0.3 — 21/11/2026

- **Il post-processing a piena risoluzione ora è spento di default** (con l'aggiornamento le impostazioni esistenti vengono spente una volta). Misurato su sequenze di fotogrammi non compressi a telecamera ferma: spento, il tremolio è circa 2,5 volte più basso che acceso, e più basso del gioco nativo; post-processing, atmosfera e sole vengono calcolati nell'ordine del gioco originale, quindi colori e alone del sole sono quelli originali. Puoi sempre riattivarlo nelle opzioni avanzate.
- Corretto: dalla 1.0.2, con il post-processing a piena risoluzione acceso, i bordi contro il cielo (aste, pilastri, tetti, fogliame) potevano apparire a scalini. L'atmosfera, che disegna anche il cielo, ora usa una profondità a risoluzione piena e sfumata, con il jitter della telecamera compensato.
- Nuova opzione avanzata *Bordi contro il cielo stabili* (accesa di default, solo con il post-processing a piena risoluzione): stabilizza nel tempo il confine tra oggetti e cielo, dimezzandone il tremolio. Anche i raggi del sole ora ricevono la profondità reale della scena.
- Nuova opzione di diagnostica *Tasto sequenza fotogrammi* (senza tasto di default): salva 30 fotogrammi consecutivi non compressi in `BepInEx/OutwardFSR_captures`, utile per segnalare problemi di tremolio.

## 1.0.2 — 21/11/2026

- Corretto: con *Post-processing a piena risoluzione* attivo, l'alone del sole e l'atmosfera (scattering e raggi del sole di Time of Day) ora vengono applicati dopo il tonemapping, come nel gioco originale. Il sole ha di nuovo il suo grande alone caldo e l'immagine non risulta più scura del nativo.
- Corretto: la profondità della scena veniva letta da una texture che in quel punto del frame era sempre vuota. Ora l'atmosfera riceve la profondità reale.
- FSR 4 continua a lavorare senza profondità della scena come nella 1.0.1, perché così il fogliame sottile resta più nitido. Nuova opzione avanzata *Profondità scena per FSR 4* (spenta di default) per provarlo con la profondità: può ridurre le scie attorno agli oggetti in movimento, ma il fogliame risulta meno nitido.
- FSR 3.1: se noti scie attorno alle foglie, attiva *Maschera reattiva automatica (solo FSR 3.1)*.

## 1.0.1 — 20/11/2026

- Novità: il menu delle impostazioni è disponibile in italiano e in inglese (impostazione "Lingua del menu", Auto = lingua di Windows).
- Le impostazioni ora sono salvate con nomi inglesi nel file .cfg. **Le impostazioni della 1.0.0 tornano una volta ai valori predefiniti.**
- FSR 3.1: la maschera reattiva automatica ora è spenta di default, perché aumentava lo sfarfallio su vetri e riflessi.
- Novità: riga "Upscaler attivo" nel menu F5, con l'upscaler in uso, la sua versione esatta (ad es. FSR 4.1.2) e le risoluzioni.
- Novità: avviso a schermo per qualche secondo quando premi F9, con l'upscaler attivo o "nativo" (disattivabile).
- Documentazione: aggiunte le sezioni "Primo avvio" e "Segnalazioni e suggerimenti", la nota sull'aiuto dell'intelligenza artificiale e il link al codice sorgente.
- Menu delle impostazioni: le opzioni che valgono solo per FSR 3.1 (maschera reattiva, riduzione sfarfallio riflessi, vista di debug) ora riportano "solo FSR 3.1".
- Menu delle impostazioni: i valori delle opzioni ora compaiono correttamente ("FSR 4", "FSR 3.1", "AMD", "Native AA (100%)"…) e i preset di qualità indicano la risoluzione interna.

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
