<#
.SYNOPSIS
Builds the Windows x64 parakeet-engine daemon and sherpa-onnx/ONNX Runtime/DirectML
DLLs from source (pinned tag v1.13.3) and downloads + SHA-256 verifies silero_vad.onnx.
#>
[CmdletBinding()]
param(
    [string]$WorkDir = (Join-Path ([System.IO.Path]::GetTempPath()) "hyperwhisper-parakeet-build"),
    [string]$OutputDir,
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot "app\windows\HyperWhisper\Resources\parakeet-engine\x64"
}

$SherpaTag = "v1.13.3"
$SherpaRepoUrl = "https://github.com/k2-fsa/sherpa-onnx.git"
$SileroVadUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx"
$SileroVadSha256 = "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6"

$RequiredFiles = @(
    "parakeet-engine.exe",
    "sherpa-onnx-c-api.dll",
    "onnxruntime.dll",
    "DirectML.dll",
    "silero_vad.onnx"
)

function Get-VisualStudioGenerator {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $ver = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationVersion 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($ver)) {
            $major = ($ver.Trim() -split '\.')[0]
            if ($major -eq "17") { return "Visual Studio 17 2022" }
            if ($major -eq "18") { return "Visual Studio 18 2026" }
            if ($major -eq "16") { return "Visual Studio 16 2019" }
        }
    }
    return $null
}

function Copy-RequiredBinary {
    param(
        [string]$FileName,
        [string[]]$PreferredPaths,
        [string]$FallbackSearchRoot,
        [string]$DestinationDir
    )

    $source = $PreferredPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $source) {
        $found = Get-ChildItem -Path $FallbackSearchRoot -Filter $FileName -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $source = $found.FullName
        }
    }
    if (-not $source) {
        throw "Built binary '$FileName' was not found under '$FallbackSearchRoot'."
    }

    $dest = Join-Path $DestinationDir $FileName
    Copy-Item -Path $source -Destination $dest -Force
    Write-Host "Staged $FileName ($((Get-Item $dest).Length) bytes) from $source"
}

function Invoke-VerifyOutputs {
    param([string]$Dir)

    foreach ($file in $RequiredFiles) {
        $path = Join-Path $Dir $file
        if (-not (Test-Path $path)) {
            throw "Required parakeet-engine file is missing: $path"
        }
        $len = (Get-Item $path).Length
        if ($len -le 0) {
            throw "Required parakeet-engine file is empty: $path"
        }
    }

    $vadPath = Join-Path $Dir "silero_vad.onnx"
    $actualHash = (Get-FileHash -Algorithm SHA256 -Path $vadPath).Hash.ToLowerInvariant()
    if ($actualHash -ne $SileroVadSha256) {
        throw "silero_vad.onnx SHA-256 mismatch: expected $SileroVadSha256, got $actualHash"
    }
    Write-Host "Verified silero_vad.onnx SHA-256: $actualHash"

    $exePath = Join-Path $Dir "parakeet-engine.exe"
    $stdout = & $exePath 2>&1
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($exitCode -ne 2) {
        throw "parakeet-engine.exe smoke launch returned exit code $exitCode (expected 2 for missing args). Output: $($stdout -join ' ')"
    }
    Write-Host "Verified parakeet-engine.exe loads sherpa-onnx/ONNX Runtime/DirectML DLLs cleanly (exit code 2 on missing args)."
}

if ($VerifyOnly) {
    Invoke-VerifyOutputs -Dir $OutputDir
    exit 0
}

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$SherpaSrc = Join-Path $WorkDir "sherpa-onnx"
$SherpaBuild = Join-Path $WorkDir "sherpa-build-x64"
$DaemonSrc = Join-Path $RepoRoot "tools\parakeet-engine"
$DaemonBuild = Join-Path $WorkDir "daemon-build-x64"

if (-not (Test-Path (Join-Path $SherpaSrc "CMakeLists.txt"))) {
    if (Test-Path $SherpaSrc) {
        Remove-Item -Path $SherpaSrc -Recurse -Force
    }
    Write-Host "==> Cloning sherpa-onnx ($SherpaTag) into $SherpaSrc..."
    & git clone --depth 1 --branch $SherpaTag $SherpaRepoUrl $SherpaSrc
    if ($LASTEXITCODE -ne 0) { throw "git clone sherpa-onnx failed with exit code $LASTEXITCODE" }
} else {
    Write-Host "==> Reusing sherpa-onnx source at $SherpaSrc..."
}

$generator = Get-VisualStudioGenerator
$cmakeGenArgs = if ($generator) { @("-G", $generator, "-A", "x64") } else { @("-A", "x64") }
Write-Host "==> Using CMake generator args: $($cmakeGenArgs -join ' ')"

if (Test-Path $SherpaBuild) { Remove-Item -Path $SherpaBuild -Recurse -Force }
Write-Host "==> Configuring sherpa-onnx ($SherpaTag, x64, DirectML=ON)..."
& cmake @cmakeGenArgs `
    -S $SherpaSrc `
    -B $SherpaBuild `
    -DCMAKE_BUILD_TYPE=Release `
    -DSHERPA_ONNX_ENABLE_DIRECTML=ON `
    -DSHERPA_ONNX_ENABLE_BINARY=OFF `
    -DBUILD_SHARED_LIBS=ON `
    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreadedDLL `
    -DSHERPA_ONNX_ENABLE_TTS=OFF `
    -DSHERPA_ONNX_ENABLE_SPEAKER_DIARIZATION=OFF `
    -DSHERPA_ONNX_ENABLE_AUDIO_TAGGING=OFF `
    -DSHERPA_ONNX_ENABLE_PUNCTUATION=OFF `
    -DSHERPA_ONNX_ENABLE_GPU=OFF
if ($LASTEXITCODE -ne 0) { throw "CMake configure for sherpa-onnx failed with exit code $LASTEXITCODE" }

Write-Host "==> Building sherpa-onnx (Release)..."
& cmake --build $SherpaBuild --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw "CMake build for sherpa-onnx failed with exit code $LASTEXITCODE" }

if (Test-Path $DaemonBuild) { Remove-Item -Path $DaemonBuild -Recurse -Force }
Write-Host "==> Configuring parakeet-engine daemon..."
& cmake @cmakeGenArgs `
    -S $DaemonSrc `
    -B $DaemonBuild `
    -DCMAKE_BUILD_TYPE=Release `
    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreadedDLL `
    -DSHERPA_ONNX_DIR="$SherpaBuild" `
    -DSHERPA_ONNX_SRC_DIR="$SherpaSrc"
if ($LASTEXITCODE -ne 0) { throw "CMake configure for parakeet-engine failed with exit code $LASTEXITCODE" }

Write-Host "==> Building parakeet-engine daemon (Release)..."
& cmake --build $DaemonBuild --config Release --parallel
if ($LASTEXITCODE -ne 0) { throw "CMake build for parakeet-engine failed with exit code $LASTEXITCODE" }

Copy-RequiredBinary -FileName "parakeet-engine.exe" `
    -PreferredPaths @((Join-Path $DaemonBuild "Release\parakeet-engine.exe"), (Join-Path $DaemonBuild "parakeet-engine.exe")) `
    -FallbackSearchRoot $DaemonBuild `
    -DestinationDir $OutputDir

Copy-RequiredBinary -FileName "sherpa-onnx-c-api.dll" `
    -PreferredPaths @((Join-Path $SherpaBuild "bin\Release\sherpa-onnx-c-api.dll"), (Join-Path $SherpaBuild "bin\sherpa-onnx-c-api.dll")) `
    -FallbackSearchRoot $SherpaBuild `
    -DestinationDir $OutputDir

Copy-RequiredBinary -FileName "onnxruntime.dll" `
    -PreferredPaths @((Join-Path $SherpaBuild "bin\Release\onnxruntime.dll"), (Join-Path $SherpaBuild "bin\onnxruntime.dll")) `
    -FallbackSearchRoot $SherpaBuild `
    -DestinationDir $OutputDir

Copy-RequiredBinary -FileName "DirectML.dll" `
    -PreferredPaths @((Join-Path $SherpaBuild "bin\Release\DirectML.dll"), (Join-Path $SherpaBuild "bin\DirectML.dll")) `
    -FallbackSearchRoot $SherpaBuild `
    -DestinationDir $OutputDir

$VadDest = Join-Path $OutputDir "silero_vad.onnx"
$VadTmp = Join-Path $WorkDir "silero_vad.onnx.download"
Write-Host "==> Downloading silero_vad.onnx..."
for ($attempt = 1; ; $attempt++) {
    try {
        Invoke-WebRequest -Uri $SileroVadUrl -OutFile $VadTmp
        break
    }
    catch {
        if ($attempt -ge 5) {
            throw "silero_vad.onnx download failed after $attempt attempts: $_"
        }
        $delay = 5 * [math]::Pow(2, $attempt - 1)
        Write-Warning "silero_vad.onnx download attempt $attempt failed: $_; retrying in $delay s"
        Start-Sleep -Seconds $delay
    }
}

$downloadedHash = (Get-FileHash -Algorithm SHA256 -Path $VadTmp).Hash.ToLowerInvariant()
if ($downloadedHash -ne $SileroVadSha256) {
    Remove-Item -Path $VadTmp -Force -ErrorAction SilentlyContinue
    throw "Downloaded silero_vad.onnx SHA-256 mismatch: expected $SileroVadSha256, got $downloadedHash"
}
Move-Item -Path $VadTmp -Destination $VadDest -Force
Write-Host "Staged silero_vad.onnx ($((Get-Item $VadDest).Length) bytes, SHA-256 $downloadedHash)"

Invoke-VerifyOutputs -Dir $OutputDir
exit 0
