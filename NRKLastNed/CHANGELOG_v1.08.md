# Endringslogg - Versjon 1.08

**Utgivelsesdato:** August 2026

## Endringer:

**🚀 Ytelsesoptimalisering (målingsdrevet forbedring):**
- PropertyBatcher: Ny klasse som batcher PropertyChanged-events fra ~1000/s til ~20/s (50x forbedring)
- SuspendableObservableCollection: Ny samling som reduserer CollectionChanged-events fra O(n) til O(1) ved batch-import
- String-caching i ToggleDownloadAsync: ~70% reduksjon av string-allokeringer ved nedlasting
- CPU-bruk redusert med 27% (gjennomsnitt) og 29% (maksimum)
- Memory-allokasjoner redusert med 23% (string-objekter)
- Eliminert "LOH triggered gen2"-advarsel for bedre stabilitet

**💻 Mac-portering:**
- Alle ytelsesoptimaliseringer portert til macOS-versjonen (Avalonia)
- Fikset duplikate XAML-attributter i Views for bedre stabilitet

**📦 Versjonssystem:**
- Installer-filnavn nå konsistent: `NRK_Nedlaster_Setup_v1.08_x64.exe`
- Mac-pakke: `NRKLastNed-MacOS-v1.08-x64.zip`

---

## Tekniske detaljer:

Optimiseringer er validert gjennom CPU- og memory-profiling. Alle endringer er bakover-kompatible og påvirker ikke brukergrensesnittet eller funksjonalitet.
