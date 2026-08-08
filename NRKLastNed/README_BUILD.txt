# NRK Nedlaster - Automatisert Release
# Kjør denne filen for å lage installasjoner automatisk

# Enkleste måte - kjør bare dette i PowerShell:
# .\build-release.ps1 -Version "1.08"

# Eksempler:

# 1. Standard release (Debug for Windows, Release for Mac)
.\build-release.ps1 -Version "1.08"

# 2. Release-kvalitet for alt
.\build-release.ps1 -Version "1.08" -Configuration "Release"

# 3. Bare Mac
.\build-release.ps1 -Version "1.08" -BuildWindows $false

# 4. Bare Windows
.\build-release.ps1 -Version "1.08" -BuildMac $false

# 5. Uten Inno Setup (bare lage Mac ZIP)
.\build-release.ps1 -Version "1.08" -CreateInstallers $false

# 6. Med GitHub push (ikke anbefalt uten review)
.\build-release.ps1 -Version "1.08" -PushToGitHub $true

# Les mer:
# Get-Help .\build-release.ps1 -Full
