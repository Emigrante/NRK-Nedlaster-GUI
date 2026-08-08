# NRK Nedlaster GUI - Kompilering og Installer Veiledning v1.08

## Steg 1: Kompilere Windows-versjonen

### Windows Debug Build (for testing)
```powershell
cd E:\NRK-Nedlaster-GUI
dotnet clean NRKLastNed\NRKLastNed.csproj
dotnet build NRKLastNed\NRKLastNed.csproj -c Debug -p:Platform=x64
```

**Resultat:** Kompilert til `NRKLastNed\bin\x64\Debug\net8.0-windows\`

### Windows Release Build (for produksjon/installer)
```powershell
cd E:\NRK-Nedlaster-GUI
dotnet clean NRKLastNed\NRKLastNed.csproj
dotnet build NRKLastNed\NRKLastNed.csproj -c Release -p:Platform=x64
```

**Resultat:** Kompilert til `NRKLastNed\bin\x64\Release\net8.0-windows\`

---

## Steg 2: Kompilere Mac-versjonen

### Mac Build (fra Windows)
```powershell
cd E:\NRK-Nedlaster-GUI
dotnet clean NRKLastNed.Mac\NRKLastNed.Mac.csproj
dotnet build NRKLastNed.Mac\NRKLastNed.Mac.csproj -c Release
```

**Resultat:** Kompilert til `NRKLastNed.Mac\bin\Release\net8.0\`

**Merk:** Mac-versjonen kan kun kjøres på macOS, men koden kan bygges på Windows.

---

## Steg 3: Lage Windows Installer (Inno Setup 6)

### Forutsetninger
- Du må ha **Inno Setup 6.x** installert på datamaskinen
  - Last ned fra: https://jrsoftware.org/isdl.php

### Prosedyre

1. **Åpne Inno Setup kompilator**
   - Kjør `Inno Setup Compiler` eller `iscc.exe`

2. **Åpne installerscriptet**
   - Fil → Open → Browse til `E:\NRK-Nedlaster-GUI\NRKLastNed\NRK_Nedlaster_Setup.iss`

3. **Kompiler**
   - Trykk: Build → Compile
   - Eller bruk snarveien: **Ctrl+F9**

4. **Resultat**
   - Installer-filen genereres til: `E:\NRK-Nedlaster-GUI\NRKLastNed\DistOut\NRK_Nedlaster_Setup_v1.08_x64.exe`

### Alternativ: Kompilere via kommandolinje
```powershell
cd "C:\Program Files (x86)\Inno Setup 6"
.\iscc.exe "E:\NRK-Nedlaster-GUI\NRKLastNed\NRK_Nedlaster_Setup.iss"
```

---

## Steg 4: Lage Mac Pakke

### Opprette ZIP-pakke manuelt
```powershell
# Fra powershell i E:\NRK-Nedlaster-GUI
$sourceDir = "NRKLastNed.Mac\bin\Release\net8.0"
$zipName = "NRKLastNed-MacOS-v1.08-x64.zip"
$outputDir = "DistOut"

# Opprett DistOut-mappe hvis den ikke finnes
if (!(Test-Path $outputDir)) { mkdir $outputDir }

# Komprimér mappen
Compress-Archive -Path $sourceDir -DestinationPath "$outputDir\$zipName" -Force

Write-Host "Mac-pakke opprettet: $outputDir\$zipName"
```

---

## Komplett Kompilering og Release-prosess

### Alt i ett (Windows Release + Mac + Installer)
```powershell
cd E:\NRK-Nedlaster-GUI

# 1. Kompilere Windows Release
Write-Host "=== Kompilerer Windows Release ==="
dotnet clean NRKLastNed\NRKLastNed.csproj
dotnet build NRKLastNed\NRKLastNed.csproj -c Release -p:Platform=x64

# 2. Kompilere Mac
Write-Host "=== Kompilerer Mac ==="
dotnet clean NRKLastNed.Mac\NRKLastNed.Mac.csproj
dotnet build NRKLastNed.Mac\NRKLastNed.Mac.csproj -c Release

# 3. Lage Mac-pakke
Write-Host "=== Lager Mac-pakke ==="
$sourceDir = "NRKLastNed.Mac\bin\Release\net8.0"
$zipName = "NRKLastNed-MacOS-v1.08-x64.zip"
$outputDir = "DistOut"
if (!(Test-Path $outputDir)) { mkdir $outputDir }
Compress-Archive -Path $sourceDir -DestinationPath "$outputDir\$zipName" -Force

Write-Host "=== Ferdig! ===" 
Write-Host "Windows Release: NRKLastNed\bin\x64\Release\net8.0-windows\"
Write-Host "Mac-pakke: $outputDir\$zipName"
Write-Host "For Windows Installer, åpne Inno Setup og kompilér NRK_Nedlaster_Setup.iss"
```

---

## Installasjonsfil-navn Konvensjon (v1.08)

| Platform | Filnavn | Plassering |
|----------|---------|-----------|
| Windows | `NRK_Nedlaster_Setup_v1.08_x64.exe` | `DistOut\` |
| Mac | `NRKLastNed-MacOS-v1.08-x64.zip` | `DistOut\` |

---

## Viktige Merknader

### Debug vs Release Build sekreter
- **Debug**: Større fil, inneholder debug-info (`.pdb`), langsommere
- **Release**: Mindre fil, optimalisert, raskere
- Installer-scriptet bruker **Debug** av sikkerhetsgrunner (enklere debugging hvis noe går galt for sluttbruker)
- For produksjon, endre `Release` i `.iss` og i kompileringskommandoen

### Versjonsoppdateringer
For å endre versjonsnummer for fremtiden:

1. **Windows (.csproj)**
   ```xml
   <AssemblyVersion>1.0.9.0</AssemblyVersion>
   <FileVersion>1.0.9.0</FileVersion>
   ```

2. **Mac (.csproj)**
   ```xml
   <AssemblyVersion>1.0.9.0</AssemblyVersion>
   <FileVersion>1.0.9.0</FileVersion>
   ```

3. **Inno Setup (.iss)**
   ```
   #define MyAppVersion "1.09"
   #define OutputFileName "NRK_Nedlaster_Setup_v" + MyAppVersion + "_x64.exe"
   ```

4. **Mac ZIP-navn** (i PowerShell-skriptet)
   ```powershell
   $zipName = "NRKLastNed-MacOS-v1.09-x64.zip"
   ```

---

## Troubleshooting

### Windows Build feilet
- Sjekk at du har `.NET 8 SDK` installert: `dotnet --version`
- Hvis ikke: https://dotnet.microsoft.com/en-us/download/dotnet/8.0

### Inno Setup finner ikke filer
- Sjekk at relativt path i `.iss` er korrekt fra skriv-plassering
- Debug build er i: `bin\x64\Debug\net8.0-windows\`
- Release build er i: `bin\x64\Release\net8.0-windows\`

### Mac ZIP inneholder feil struktur
- Sjekk at `$sourceDir` peker til riktig `bin\Release` mappe
- Verifiser at komprimeringen inkluderte alle avhengigheter

