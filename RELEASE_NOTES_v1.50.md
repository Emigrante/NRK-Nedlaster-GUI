# NRK Nedlaster GUI – Versjon 1.50 🚀

Velkommen til **versjon 1.50** av NRK Nedlaster GUI! Dette er en omfattende oppgradering med et helt nytt og mer flytende grensesnitt, støtte for nye videokodekser (HEVC og AV1), automatisk GPU-maskinvareakselerasjon, et eget live nedlastings- og bearbeidingsvindu, automatisk verktøykontroll og standardisert filnavngiving.

---

## 🌟 Nye funksjoner og høydepunkter

### 1. 🎬 Valg av videokodeks (x264, x265 / HEVC og AV1)
Du kan nå velge ønsket videokodeks for sluttresultatet – enten individuelt per fil i listen eller som standard i innstillinger:
* **x264 (Original)**: Beholder NRKs opprinnelige videostrøm uten ekstra omkoding. Lynraskt og uendret kvalitet.
* **x265 (HEVC)**: Halverer filstørrelsen uten synlig kvalitetstap – perfekt for lagring og arkivering.
* **AV1**: Neste generasjons åpne videokodeks med markedets beste bildekvalitet og komprimering per megabyte.

### 2. ⚡ Lynrask GPU-maskinvareakselerasjon
* **Automatisk gjenkjenning av grafikkort**: Oppdager automatisk om maskinen har **Intel Arc / Iris Xe / UHD** (Intel QSV), **NVIDIA GeForce** (NVENC) eller **AMD Radeon** (AMF).
* **Ekte hardware-transkoding**: Kjører både dekoding og enkoding direkte på grafikkortet, og oppnår omkodingshastigheter på **opptil 40–50x** på 720p/1080p materiale.
* **Sømløs fallback**: Bytter automatisk over til prosessor (CPU) dersom grafikkortet ikke støtter en kodeks eller opplever en feil underveis.

### 3. 🖥️ Eget nedlastings- og bearbeidingsvindu
* Eget dedikert vindu som åpnes automatisk når en nedlasting starter.
* **To uavhengige sanntids framdriftsindikatorer**:
  1. **Nedlasting**: Viser nedlastingsprosent (0–100%), gjeldende hastighet (f.eks. `58.4 MiB/s`) og ETA.
  2. **Rekoding og klargjøring**: Viser omkodingsfart (f.eks. `48.5x`) og prosessprosent.
* **Total fremdrift i prosent**: Ved nedlasting av flere filer vises samlet fremdrift nederst i vinduet (f.eks. `Total fremdrift: 58%`).
* **Kø-teller**: Viser aktiv fil i køen (f.eks. `Fil 2/5`).
* **Fleksible valg**: Kan skjules underveis mens nedlastingen fortsetter i bakgrunnen, og har valg for automatisk åpning og lukking.

### 4. 📋 Ren og oversiktlig logg (Info vs. Debug)
* **Info-modus (standard)**: Gir en folkelig og ryddig oppsummering av prosessen per fil:
  ```text
  • [Fil 2/5] Laster ned fra NRK: Familien Lykke - S08E02
    ↳ Nedlastet på 18s (snitt 62.22MiB/s)
  • Rekoder til AV1 med maskinvareakselerasjon (Intel(R) Arc(TM) Graphics)...
    ↳ Transkodet til AV1 på 14s (48.5x hastighet) med Intel(R) Arc(TM) Graphics
  ✓ Fullført: Familien.Lykke.S08E02.720p.WEB.AV1-NRKNedlaster.mkv
  ```
* **Debug-modus**: Gir full teknisk utskrift med 10 %-milepæler og detaljer for feilsøking.
* Kan veksles med ett klikk i loggvinduet eller i innstillingsmenyen.

### 5. 🏷️ Standardisert scene-filnavngiving
Alle filer lagres nå med en ryddig, standardisert oppbygging med `-NRKNedlaster`:
* **TV-serier**: `<Serie>.<Sesong/Episode>.<Oppløsning>.WEB.<Kodek>-NRKNedlaster.mkv`  
  *Eksempel*: `Constellation.S01E01.720p.WEB.AV1-NRKNedlaster.mkv`
* **Filmer**: `<Film>.<Oppløsning>.<Kodek>-NRKNedlaster.mkv`  
  *Eksempel*: `Max.Manus.1080p.x264-NRKNedlaster.mkv`
* **Radio**: `<Programnavn>.<Episodenavn>-NRKNedlaster.mp3`  
  *Eksempel*: `Misjonen.med.Antonsen.og.Golden.15.mars.2024-NRKNedlaster.mp3`

### 6. 🛡️ Automatisk verktøykontroll ved oppstart (Tools)
* Programmet kontrollerer automatisk om `ffmpeg.exe`, `ffprobe.exe` og `yt-dlp.exe` er på plass i `Tools`-mappen.
* Hvis verktøy mangler, vises et advarselsvindu med mulighet for automatisk ett-klikks nedlasting og **SHA-256 sjekksum-verifisering** direkte fra offisielle GitHub-utgivelser.

### 7. 🔄 Sanntids oppdateringshåndtering
* Programmet sjekker oppdateringer for både selve applikasjonen, yt-dlp og FFmpeg.
* Oppdateringsvarsler i hovedvinduet oppdateres og nullstilles automatisk idet innstillingsvinduet lukkes etter oppdatering.

---

## 💾 Installasjon og oppgradering

1. Last ned `NRK-Nedlaster-GUI-Setup.exe` (eller zip-pakken).
2. Kjør installasjonsprogrammet eller pakk ut filene til ønsket mappe.
3. Start programmet. Dersom `Tools`-mappen mangler verktøy, vil programmet tilby å hente disse automatisk ved første oppstart.

---

## 👥 Credits

* **Utviklet av**: Emigrante & EmiMan
* En stor takk til åpen kildekode-prosjektene:
  * [yt-dlp](https://github.com/yt-dlp/yt-dlp)
  * [FFmpeg](https://ffmpeg.org)
