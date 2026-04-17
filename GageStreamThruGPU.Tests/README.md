# GageStreamThruGPU Tests

This test project exercises the named-pipe protocol in `GageStreamThruGPU/pipe.cpp` without requiring the GaGe digitizer, CUDA kernels, or the WPF frontend.

Covered behaviors:
- pipe creation and client connection
- no-request fast path
- start request (`1`)
- stop request (`3`)
- invalid request rejection
- request `2` data transfer for interleaved samples plus the 64-double correlation matrix
- invalid `bytesToSend` rejection

Build:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  '.\GageStreamThruGPU.Tests\GageStreamThruGPU.Tests.vcxproj' `
  /p:Configuration=Debug /p:Platform=x64 /nologo
```

Run:

```powershell
& '.\GageStreamThruGPU.Tests\x64\Debug\GageStreamThruGPU.Tests.exe'
```

Run one test:

```powershell
& '.\GageStreamThruGPU.Tests\x64\Debug\GageStreamThruGPU.Tests.exe' 'DataRequestSendsInterleavedSamplesAndMatrix'
```
