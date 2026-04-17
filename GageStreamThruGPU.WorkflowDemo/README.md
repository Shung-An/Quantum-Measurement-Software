# GageStreamThruGPU Workflow Demo

This is a no-hardware smoke demo for the CUDA compute path in `GageStreamThruGPU`.

What it does:
- creates synthetic input buffers for two boards
- allocates CUDA device buffers
- initializes cuBLAS
- calls `ComputeCrossCorrelationGPU(...)` from `DSPEquation_Simple_GPU.cu`
- prints the first few rows of the reduced `8x8` correlation matrix

This is useful when you want to see the C/CUDA workflow without:
- the GaGe digitizer
- named-pipe IPC
- the WPF frontend
- the full `StreamThruGPU_Simple.c` acquisition loop

Build:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  '.\GageStreamThruGPU.WorkflowDemo\GageStreamThruGPU.WorkflowDemo.vcxproj' `
  /p:Configuration=Debug /p:Platform=x64 /nologo
```

Run:

```powershell
& '.\GageStreamThruGPU.WorkflowDemo\x64\Debug\GageStreamThruGPU.WorkflowDemo.exe'
```

If CUDA is installed correctly and a CUDA-capable GPU is available, the program should print a small matrix preview.
