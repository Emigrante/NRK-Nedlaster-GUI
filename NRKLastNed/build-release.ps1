#!/usr/bin/env powershell
# ========================================
# NRK Nedlaster - Automatisk Release Builder
# Kompilerer Windows + Mac og lager installasjoner
# ========================================

param(
	[string]$Version = "1.08",
	[string]$Configuration = "Debug",  # Debug eller Release
	[switch]$BuildWindows = $true,
	[switch]$BuildMac = $true,
	[switch]$CreateInstallers = $true,
	[switch]$PushToGitHub = $false
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

# ========================================
# Farger for output
# ========================================
$colors = @{
	Info    = "Cyan"
	Success = "Green"
	Warning = "Yellow"
	Error   = "Red"
	Header  = "Magenta"
}

function Write-Log {
	param([string]$Message, [string]$Type = "Info")
	Write-Host $Message -ForegroundColor $colors[$Type]
}

function New-Section {
	param([string]$Title)
	Write-Host ""
	Write-Host "========================================" -ForegroundColor $colors.Header
	Write-Host $Title -ForegroundColor $colors.Header
	Write-Host "========================================" -ForegroundColor $colors.Header
}

# ========================================
# Start
# ========================================
New-Section "NRK Nedlaster v$Version - Release Builder"
Write-Log "Configuration: $Configuration" "Info"
Write-Log "BuildWindows: $BuildWindows | BuildMac: $BuildMac" "Info"

$outputDir = "DistOut"
if (!(Test-Path $outputDir)) {
	New-Item -ItemType Directory -Path $outputDir | Out-Null
	Write-Log "✓ Created $outputDir directory" "Success"
}

# ========================================
# STEG 1: Bygge Windows
# ========================================
if ($BuildWindows) {
	New-Section "Step 1: Building Windows ($Configuration)"

	try {
		Write-Log "Cleaning Windows project..." "Info"
		dotnet clean NRKLastNed\NRKLastNed.csproj -c $Configuration -p:Platform=x64 2>&1 | Out-Null

		Write-Log "Building Windows..." "Info"
		dotnet build NRKLastNed\NRKLastNed.csproj -c $Configuration -p:Platform=x64

		if ($LASTEXITCODE -eq 0) {
			Write-Log "✓ Windows build successful" "Success"
			$windowsBuildPath = "NRKLastNed\bin\x64\$Configuration\net8.0-windows"
			Write-Log "Output: $windowsBuildPath" "Info"
		} else {
			Write-Log "✗ Windows build failed!" "Error"
			exit 1
		}
	} catch {
		Write-Log "✗ Windows build error: $_" "Error"
		exit 1
	}
}

# ========================================
# STEG 2: Bygge Mac
# ========================================
if ($BuildMac) {
	New-Section "Step 2: Building Mac (Release)"

	try {
		Write-Log "Cleaning Mac project..." "Info"
		dotnet clean NRKLastNed.Mac\NRKLastNed.Mac.csproj -c Release 2>&1 | Out-Null

		Write-Log "Building Mac..." "Info"
		dotnet build NRKLastNed.Mac\NRKLastNed.Mac.csproj -c Release

		if ($LASTEXITCODE -eq 0) {
			Write-Log "✓ Mac build successful" "Success"
			$macBuildPath = "NRKLastNed.Mac\bin\Release\net8.0"
			Write-Log "Output: $macBuildPath" "Info"
		} else {
			Write-Log "✗ Mac build failed!" "Error"
			exit 1
		}
	} catch {
		Write-Log "✗ Mac build error: $_" "Error"
		exit 1
	}
}

# ========================================
# STEG 3: Lage Mac ZIP-pakke
# ========================================
if ($BuildMac) {
	New-Section "Step 3: Creating Mac Package"

	try {
		$sourceDir = "NRKLastNed.Mac\bin\Release\net8.0"
		$zipName = "NRKLastNed-MacOS-v$Version-x64.zip"
		$zipPath = "$outputDir\$zipName"

		Write-Log "Creating ZIP package: $zipName" "Info"

		# Fjern gammel ZIP hvis den finnes
		if (Test-Path $zipPath) {
			Remove-Item $zipPath -Force
			Write-Log "Removed old version" "Warning"
		}

		Compress-Archive -Path $sourceDir -DestinationPath $zipPath -Force

		if (Test-Path $zipPath) {
			$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
			Write-Log "✓ Mac package created: $zipName ($zipSize MB)" "Success"
		} else {
			Write-Log "✗ Failed to create Mac package!" "Error"
			exit 1
		}
	} catch {
		Write-Log "✗ Mac packaging error: $_" "Error"
		exit 1
	}
}

# ========================================
# STEG 4: Inno Setup Windows Installer (hvis tilgjengelig)
# ========================================
if ($CreateInstallers -and $BuildWindows) {
	New-Section "Step 4: Creating Windows Installer (Inno Setup)"

	# Sjekk om Inno Setup er installert
	$isccPaths = @(
		"C:\Program Files (x86)\Inno Setup 6\iscc.exe",
		"C:\Program Files\Inno Setup 6\iscc.exe"
	)

	$isccPath = $null
	foreach ($path in $isccPaths) {
		if (Test-Path $path) {
			$isccPath = $path
			break
		}
	}

	if ($isccPath) {
		try {
			Write-Log "Found Inno Setup at: $isccPath" "Info"
			Write-Log "Compiling: NRK_Nedlaster_Setup.iss" "Info"

			& $isccPath "NRKLastNed\NRK_Nedlaster_Setup.iss" /F"NRK_Nedlaster_Setup_v$Version-x64"

			if ($LASTEXITCODE -eq 0) {
				$setupFile = "$outputDir\NRK_Nedlaster_Setup_v$Version-x64.exe"
				if (Test-Path $setupFile) {
					$exeSize = [math]::Round((Get-Item $setupFile).Length / 1MB, 2)
					Write-Log "✓ Windows installer created: NRK_Nedlaster_Setup_v$Version-x64.exe ($exeSize MB)" "Success"
				}
			} else {
				Write-Log "✗ Inno Setup compilation failed!" "Error"
			}
		} catch {
			Write-Log "✗ Inno Setup error: $_" "Error"
		}
	} else {
		Write-Log "⚠ Inno Setup not found. Skipping Windows installer creation." "Warning"
		Write-Log "Download from: https://jrsoftware.org/isdl.php" "Info"
	}
}

# ========================================
# STEG 5: Oppsummering
# ========================================
New-Section "Release Summary"

if (Test-Path $outputDir) {
	$files = Get-ChildItem $outputDir -File

	if ($files.Count -gt 0) {
		Write-Log "Generated files:" "Info"
		foreach ($file in $files) {
			$size = [math]::Round($file.Length / 1MB, 2)
			Write-Log "  • $($file.Name) ($size MB)" "Success"
		}
	} else {
		Write-Log "No files generated" "Warning"
	}
}

Write-Log "Output directory: $outputDir" "Info"

# ========================================
# STEG 6: Push til GitHub (valgfritt)
# ========================================
if ($PushToGitHub) {
	New-Section "Pushing to GitHub"

	try {
		Write-Log "Staging changes..." "Info"
		git add -A

		Write-Log "Committing..." "Info"
		git commit -m "Release v$Version - Automated build"

		Write-Log "Pushing to origin/master..." "Info"
		git push origin master

		Write-Log "✓ Pushed to GitHub successfully" "Success"
	} catch {
		Write-Log "⚠ Git push error: $_" "Warning"
	}
}

# ========================================
# Ferdig
# ========================================
New-Section "✓ Build Complete!"
Write-Log "Version: $Version" "Success"
Write-Log "Ready for distribution!" "Success"
Write-Host ""
