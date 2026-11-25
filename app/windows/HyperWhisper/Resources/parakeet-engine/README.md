# Parakeet / Qwen3 / Nemotron Engine Binaries (Windows x64)

Prebuilt binaries are not committed to this repository (issue #6). ARM64 binaries have been removed.

To build the Windows x64 daemon (`parakeet-engine.exe`), `sherpa-onnx` (`v1.13.3` with DirectML), and download + SHA-256 verify `silero_vad.onnx` locally on Windows:

```powershell
.\tools\build-parakeet-engine-ci.ps1
```

In GitHub Actions, `.github/workflows/windows-ci.yml` builds and caches these binaries automatically and publishes them as the `parakeet-engine-x64` workflow artifact.
