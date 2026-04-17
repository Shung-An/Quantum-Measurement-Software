# GageStreamThruGPU Hardware Smoke

This is a minimal real-hardware client for `GageStreamThruGPU.exe`.

What it does:
- starts the real `GageStreamThruGPU.exe`
- connects to `\\.\pipe\DataPipe`
- sends the same start command the WPF UI uses
- sends the required 15-byte experiment token
- requests one real frame and one 64-double correlation matrix
- prints a short preview
- sends the stop command

Build:

```powershell
dotnet build '.\GageStreamThruGPU.HardwareSmoke\GageStreamThruGPU.HardwareSmoke.csproj'
```

Run:

```powershell
dotnet run --project '.\GageStreamThruGPU.HardwareSmoke\GageStreamThruGPU.HardwareSmoke.csproj'
```

By default this now launches `GageStreamThruGPU.exe` with the working directory set to:

```text
C:\Quantum Squeezing\Quantum-Measurement-Software\Quantum Measurement UI\bin\Debug\net8.0-windows7.0
```

so it uses the same `StreamThruGPU.ini` as the UI build output.

The tool also pre-creates:

```text
D:\Quantum Squeezing Project\DataFiles\<15-byte experiment token>
```

to match the folder structure the WPF UI normally prepares before acquisition starts.

Optional arguments:

```powershell
dotnet run --project '.\GageStreamThruGPU.HardwareSmoke\GageStreamThruGPU.HardwareSmoke.csproj' -- `
  'C:\Quantum Squeezing\Quantum-Measurement-Software\GageStreamThruGPU\x64\Debug\GageStreamThruGPU.exe' `
  '260417_090000X' `
  'C:\Quantum Squeezing\Quantum-Measurement-Software\Quantum Measurement UI\bin\Debug\net8.0-windows7.0'
```

Notes:
- the experiment token must be exactly 15 ASCII bytes; the tool pads/truncates automatically
- the third argument optionally overrides the server working directory
- this requires the real GaGe hardware, drivers, CUDA runtime, and a working `StreamThruGPU.ini` in the chosen server working directory
- this is the smallest path to exercise the actual card + GPU + pipe workflow without launching the full WPF UI
