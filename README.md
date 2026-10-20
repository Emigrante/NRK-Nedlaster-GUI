# NRK Nedlaster GUI

Velkommen til **NRK Nedlaster GUI**!

Dette programmet er laget for å gjøre det så enkelt som mulig å laste ned TV-serier, filmer og radioprogrammer fra NRK ([tv.nrk.no](https://tv.nrk.no) og [radio.nrk.no](https://radio.nrk.no)) til din egen PC eller Mac. 

I stedet for å knote med kompliserte kommandolinjeverktøy i terminalen, limer du bare inn en lenke, velger ønsket kvalitet og format, og lar programmet ta seg av nedlasting, undertekster og eventuell omkoding.

---

## 🚀 Hovedfunksjoner

* **Alt-i-ett nedlasting**:
  Støtter enkeltepisoder, spillefilmer, hele sesonger og radioprogrammer i én og samme operasjon.
* **Valg av videokodeks**:
  * **x264 (Original)**: NRKs opprinnelige videostrøm. Beholder filen direkte uten omkoding for maksimal nedlastingshastighet.
  * **x265 (HEVC)**: Høykomprimert format som nesten halverer filstørrelsen uten synlig kvalitetstap.
  * **AV1**: Neste generasjons åpne videokodeks med markedets beste komprimering og fremtidsrettet effektivitet.
  *(Kodeks kan velges per element i listen eller settes som standard i innstillinger).*
* **Automatisk GPU-maskinvareakselerasjon**:
  * Gjenkjenner automatisk grafikkort i maskinen: **Intel Arc** (Intel QSV), **NVIDIA** (NVENC) og **AMD** (AMF).
  * Utnytter dedikert maskinvare for lynrask omkoding (f.eks. 40–50x hastighet på 720p/1080p materiale).
  * Sømløs fallback: Bytter automatisk over til prosessor (CPU) dersom grafikkortet ikke støtter den valgte kodeksen eller opplever en feil.
* **Eget nedlastings- og bearbeidingsvindu**:
  * Eget live-vindu som åpnes automatisk under nedlasting og bearbeiding.
  * To separate sanntids framdriftsindikatorer:
    1. **Nedlasting** (0–100%) med live hastighet og ETA.
    2. **Rekoding og klargjøring** (0–100%) med live transkodingsfart (f.eks. `48x`).
  * Total fremdrift i prosent når flere filer lastes ned (f.eks. `Total fremdrift: 58%`).
  * Viser aktiv fil og kø-indeks (f.eks. `Fil 2/5`).
  * Valg for automatisk åpning og automatisk lukking når ferdig, samt mulighet for å skjule vinduet mens jobben fortsetter i bakgrunnen.
* **Ren og informativ logg (Info vs. Debug)**:
  * **Info-modus** *(standard/anbefalt)*: Viser en folkelig og ryddig oppsummering av tidsbruk, snitthastighet, benyttet GPU og ferdig filnavn.
  * **Debug-modus**: Viser full teknisk utskrift med 10 %-milepæler og detaljer for feilsøking.
  * Kan enkelt veksles i innstillingene eller rett i loggvinduet.
* **Standardisert scene-filnavngiving**:
  Filer lagres automatisk med ryddig standard-navngiving:
  * **TV-serier**: `<Serie>.<Sesong/Episode>.<Oppløsning>.WEB.<Kodek>-NRKNedlaster.mkv`  
    *Eks: `Constellation.S01E01.720p.WEB.AV1-NRKNedlaster.mkv`*
  * **Filmer**: `<Film>.<Oppløsning>.<Kodek>-NRKNedlaster.mkv`  
    *Eks: `Max.Manus.1080p.x264-NRKNedlaster.mkv`*
  * **Radio**: `<Programnavn>.<Episodenavn>-NRKNedlaster.mp3`  
    *Eks: `Misjonen.med.Antonsen.og.Golden.15.mars.2024-NRKNedlaster.mp3`*
* **Radio-støtte**:
  Gjenkjenner automatisk radio-lenker og laster ned sendinger direkte som høykvalitets MP3-lydfiler med metadata.
* **Automatisk verktøysjekk (Tools)**:
  Sjekker ved oppstart at `ffmpeg.exe`, `ffprobe.exe` og `yt-dlp.exe` er på plass i Tools-mappen. Mangler noe, tilbys automatisk nedlasting og SHA-256 verifisering direkte fra GitHub.
* **Innebygd oppdateringssystem**:
  Sjekker automatisk etter nye versjoner av både selve programmet, yt-dlp og FFmpeg, med varsling i hovedvinduet og ett-klikks oppdatering. Varsler oppdateres og nullstilles automatisk idet innstillingsvinduet lukkes.
* **Organisering og mapper**:
  Støtter separate lagringsmapper for TV og Radio, eller felles mappe.
* **Fargetema**:
  Moderne mørkt tema som standard, med mulighet for å bytte til lyst tema.

---

## 📖 Hvordan bruke programmet

1. **Start NRK Nedlaster GUI**.  
   *(Første gang: Åpne «Innstillinger» for å velge dine foretrukne mapper for TV og Radio, samt standard kodeks og oppløsning).*
2. **Finn programmet** du ønsker på [tv.nrk.no](https://tv.nrk.no) eller [radio.nrk.no](https://radio.nrk.no) i nettleseren.
3. **Kopier nettadressen (URL)** fra nettleserens adressefelt.  
   *(Du kan kopiere lenken til en episode, en film, eller hovedsiden til en hel serie for å hente alle tilgjengelige episoder).*
4. **Lim inn lenken** i programmet og klikk på **«LEGG TIL»**. Programmet analyserer lenken og lister opp alle tilgjengelige episoder.
5. **Tilpass innstillinger i listen ved behov**:
   * Huk av eller på episodene du vil laste ned.
   * Velg ønsket oppløsning (f.eks. 1080p, 720p, Best).
   * Velg ønsket videokodeks (x264, x265 eller AV1).
   * Velg språk for undertekster (norsk, svensk, dansk).
6. **Klikk på «START NEDLASTING»**.
7. **Logg- og bearbeidingsvinduet** åpnes automatisk og viser nedlasting, eventuell maskinvare-omkoding og ferdigstillelse.

---

## ⚙️ Om verktøyene i bakgrunnen

NRK Nedlaster GUI benytter to anerkjente åpen kildekode-verktøy som kjører i bakgrunnen:

1. **yt-dlp** – Laster ned lyd- og videostrømmer og undertekster fra NRK.
2. **FFmpeg** – Setter sammen bilde, lydspor og undertekster i en MKV/MP3-fil, og utfører eventuell GPU-akselerert rekoding (QSV/NVENC/AMF).

Disse verktøyene ligger i undermappen `Tools`. Programmet holder disse oppdatert for deg via Innstillinger-menyen.

---

## 💻 Systemkrav

* Windows 10 / 11 (64-bit) eller macOS (10.15+ / Apple Silicon / Intel).
* .NET 8.0 Runtime (dersom rammeverket ikke allerede er installert).
* Internettilkobling.
* Valgfritt grafikkort for maskinvareakselerasjon:
  * Intel Arc / Intel Core med Iris Xe / UHD Graphics (Intel QSV)
  * NVIDIA GeForce GTX / RTX (NVENC)
  * AMD Radeon RX (AMF)

---

## 👥 Credits

* **Utviklet av**: Emigrante & EmiMan
* En stor takk til utviklerne av kjerne-verktøyene:
  * [yt-dlp](https://github.com/yt-dlp/yt-dlp)
  * [FFmpeg](https://ffmpeg.org)

*Innhold lastet ned fra NRK er kun til privat bruk iht. gjeldende lovverk.*
