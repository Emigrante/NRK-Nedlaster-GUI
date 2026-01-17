# NRK Nedlaster GUI - macOS Version

Dette er macOS-versjonen av NRK Nedlaster GUI, bygget med Avalonia UI.

## Setup

1. Installer .NET 8.0 SDK hvis du ikke har det:
   ```bash
   brew install dotnet
   ```

2. Restore NuGet packages:
   ```bash
   dotnet restore
   ```

3. Bygg prosjektet:
   ```bash
   dotnet build
   ```

4. Kjør applikasjonen:
   ```bash
   dotnet run
   ```

## Verktøy som trengs

Applikasjonen trenger følgende verktøy i `Tools/` mappen:

- `yt-dlp` - Last ned fra https://github.com/yt-dlp/yt-dlp/releases
- `ffmpeg` - Last ned fra https://evermeet.cx/ffmpeg/ eller via Homebrew: `brew install ffmpeg`

**Viktig på macOS:** Du må gjøre filene kjørbare:
```bash
chmod +x Tools/yt-dlp
chmod +x Tools/ffmpeg
chmod +x Tools/ffprobe
```

## Forskjeller fra Windows-versjonen

- Bruker Avalonia UI i stedet for WPF
- Filendelser er uten `.exe` (yt-dlp, ffmpeg i stedet for yt-dlp.exe, ffmpeg.exe)
- Standard nedlastingsmappe er `~/Movies/NRK` i stedet for `MyVideos/NRK`
- Åpner mapper med `open` kommando i stedet for `explorer.exe`

## Bygge for distribusjon

For å lage en .app bundle for macOS:

```bash
dotnet publish -c Release -r osx-x64 --self-contained
```

Du kan også bruke `dotnet publish` med `-p:PublishSingleFile=true` for å lage en enkeltstående fil.
