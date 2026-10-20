# NRK Nedlaster GUI – Versjon 1.51 ⚡

Velkommen til **versjon 1.51** av NRK Nedlaster GUI! Denne oppdateringen fokuserer på vesentlige ytelsesforbedringer, redusert minnebruk og raskere maskinvare-deteksjon under panseret.

---

## ⚡ Ytelsesforbedringer og optimaliseringer

### 1. 🚀 Lynrask GPU- og maskinvare-deteksjon
* **Parallellisert enkoder-sjekk**: FFmpeg-prober for **AV1**, **HEVC** og **H.264** kjøres nå parallelt med `Task.WhenAll` i stedet for sekvensielt, noe som gjør maskinvaresjekken opptil 3x raskere.
* **Effektivt registeroppslag under Windows**: GPU-modell slås nå direkte opp i Windows Registry (Hardware Information) før eventuell fallback til PowerShell, noe som eliminerer forsinkelser ved oppstart.
* **Bakgrunns-forvarming**: GPU- og FFmpeg-kapasiteter forvarmes automatisk i bakgrunnen ved appstart, slik at transkoding kan starte umiddelbart etter at første videofil er lastet ned.
* **Optimaliserte probe-kall**: Enkoder-sjekker kjøres nå med minimalt ressursforbruk uten overflødig stdout/stderr-buffering.

### 2. 🖥️ Redusert minnebruk og flytende loggvindu
* **Batching av loggutskrift**: Nye logglinjer i nedlastings- og bearbeidingsvinduet samles opp og skrives ut i puljer (`ConcurrentQueue` med `DispatcherPriority.Background`), noe som drastisk reduserer CPU-belastning under høye nedlastingshastigheter.
* **Beskyttelse mot minnevekst (LOH)**: Logg-bufferen holdes under .NET sin Large Object Heap-grense, og angrehistorikk (undo) i tekstboksen er deaktivert for å hindre unødig minnebruk over tid.
* **Mindre UI-omtegning**: Tilstands- og prosentvisning oppdateres kun når verdiene faktisk endrer seg, og unngår unødige strengallokeringer.

### 3. 🔍 Ytelsesoptimalisert yt-dlp parsing og regex-caching
* **Kildegenererte regulære uttrykk (`[GeneratedRegex]`)**: Parsing av filnavn, sesong- og episodenumre benytter nå ferdigkompilerte regex-instanser for maksimal hastighet.
* **Tidlig avbrudd av bakgrunnsanalyser**: Spillelisteinformasjon (`playlist_count` og `n_entries`) fanges opp direkte fra JSON-strømmen under analyse, og avslutter overflødige bakgrunns-spørringer tidlig.

### 4. 📝 Raskere og mer skånsom fil-logging
* **Bufret fillogging**: Disk-logging caches i minnet og flushes kun ved kritiske meldinger (Info/Error), slik at detaljert feilsøkingslogging (Debug) ikke forsinker nedlastingen.
* **Dato-sammenligning uten strengallokering**: Loggrotasjon sjekker nå heltall for `DayOfYear` og `Year` i stedet for å formatere datostrenger for hver logglinje.

---

## 👥 Credits

* **Utviklet av**: Emigrante
* En stor takk til åpen kildekode-prosjektene:
  * [yt-dlp](https://github.com/yt-dlp/yt-dlp)
  * [FFmpeg](https://ffmpeg.org)
