# GageStreamThruGPU

Windows acquisition backend for Quantum Measurement. Streams data from two GaGe CompuScope systems, aligns samples using calibration signals, and computes correlation matrices with CUDA and cuBLAS.

This README covers the `v1.6-2cards-sync-skew` branch.

## Workflow

1. Wait for the companion UI to connect to the Windows named pipe `\\.\pipe\DataPipe` and send the start handshake with a 15-byte experiment directory token.
2. Initialize both CompuScope systems and load `StreamThruGPU.ini` from the process working directory.
3. Stream interleaved samples into alternating buffers for each board.
4. Measure calibration offsets and align samples for GPU processing.
5. Compute correlations, save results, and respond to client requests.

**The executable requires a client to start acquisition**, including when `UseIPC=0`.

## Requirements and build

- Windows x64 and Visual Studio 2022 C++ tools (`v143`) with Windows 10 SDK.
- CUDA 12.3 with Visual Studio integration, cuBLAS, and a compatible NVIDIA GPU and driver.
- Two GaGe CompuScope systems with supported Expert streaming firmware, vendor drivers, and the CompuScope C SDK.

Open `Quantum Measurement.sln`, select x64 and Debug or Release, and build the `GageStreamThruGPU` project.

Before building:

1. Set the `GageDir` build property or environment variable to the SDK directory containing `include` and `Lib64`.
2. Adjust SDK paths in `StreamThruGPU_Simple.c` and `GageStreamThruGPU-Simple.vcxproj`. Some references are hard-coded to `C:\Program Files (x86)\Gage\CompuScope\CompuScope C SDK\C Common`; the relative `C Common` include path may also need adjustment.
3. If using another CUDA version, update both CUDA build-customization imports in the project.

From a Visual Studio Developer PowerShell at the repository root:

```powershell
msbuild .\GageStreamThruGPU\GageStreamThruGPU-Simple.vcxproj /p:Configuration=Release /p:Platform=x64
```

## Configure and run

1. Prepare a hardware-appropriate `StreamThruGPU.ini` in the backend's **working directory**. This branch has no tracked INI file. Missing entries fall back to defaults; these are not a validated hardware configuration.
2. Update the UI executable path in [Constants_Fields.cs](../Quantum%20Measurement%20UI/Window_Logic/Constants_Fields.cs) to match your build.
3. Check `experimentLogDirectory` in `StreamThruGPU_Simple.c`. The base path is `D:\Quantum Squeezing Project\DataFiles\`; the client token is appended to it. Prepare the parent directory or adapt the path.
4. Connect measurement and calibration signals and start an experiment through the UI.
5. Check board detection, calibration offsets, and transfer progress in the console. During streaming, **Esc** aborts acquisition and **F** requests a force trigger.

For operation without the full UI, use the [hardware smoke client](../GageStreamThruGPU.HardwareSmoke/README.md), adjusting its installation-specific paths first.

### Configuration

Acquisition, channel, and trigger sections are loaded through the CompuScope SDK. Use the SDK documentation and your lab's working configuration for those hardware settings.

| Section | Key | Default | Purpose |
| --- | --- | --- | --- |
| `StmConfig` | `BufferSize` | `2097152` | Requested buffer size in bytes |
| `StmConfig` | `TimeoutOnTransfer` | `10000` | Transfer timeout |
| `StmConfig` | `SaveToFile` | `0` | Save raw streams |
| `StmConfig` | `DataFile` | `Data` | Raw output prefix |
| `GpuConfig` | `DoAnalysis` | `1` | Enable analysis |
| `GpuConfig` | `UseGpu` | `1` | Select GPU processing |
| `ExpConfig` | `DemodulationWindowSize` | `16` | Demodulation window size |
| `ExpConfig` | `GPUBlockSize` | `256` | Experiment GPU block size |
| `ExpConfig` | `CorrelationType` | `0` | 0: cross-correlation; 1: G2 correlation |
| `ExpConfig` | `Profile` | `1` | Enable profiling |
| `ExpConfig` | `UseCpuVerify` | `0` | CPU verification setting |
| `ExpConfig` | `UseIPC` | `0` | Runtime IPC setting; startup still requires a client |

These are loader defaults, not a complete INI. Check buffer, window, and launch dimensions together when changing settings. Do not assume the CPU path provides equivalent two-board analysis without validation.

### Calibration

The code assumes two interleaved channels per board. Rising edges on **board 1, channel 2** and **board 2, channel 1** establish relative timing; triangle minimum detection determines additional alignment offsets. Channel assignments and the target index are set in the source.

`BYPASS_CALIBRATION_EDGE_ALGORITHM` is `0`, enabling calibration. Setting it to `1` bypasses calibration and forces zero GPU skip offsets. This is a source-level option, not an INI key.

Missing calibration edges cause the run to exit without automatically relaunching.

## Outputs

| File | Contents and behavior |
| --- | --- |
| `cm.bin` | Binary correlation output, appended |
| `analysis.txt` | Text analysis output, overwritten |
| `profile.txt` | Timing information when profiling is enabled, overwritten |
| `<DataFile>_1_<cardIndex>.bin`, `<DataFile>_2_<cardIndex>.bin` | Raw streams when `SaveToFile=1`; location follows the `DataFile` prefix |

Experiment results use the configured base directory and client token. Use a new directory for each run: reusing one can append binary data while replacing text logs.

`cm.bin` contains consecutive native `double` values without a header. Preserve the run configuration separately and determine record dimensions from the selected correlation path. The base correlation size is the square of `DemodulationWindowSize`. Pipe tests and the smoke client use a 64-double payload, so check client compatibility before changing dimensions.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Waiting for client connection | Connect the UI or compatible client and send the start handshake. |
| INI missing or unexpected defaults | Verify the process working directory. |
| Missing SDK headers or libraries | Check `GageDir` and SDK include/source paths. |
| CUDA build import fails | Install CUDA 12.3 integration or adjust project imports. |
| Calibration edges not found | Check routing, amplitude, and channel assignments. |
| Output files cannot be opened | Check parent directory and write access; experiment-folder creation does not create a missing parent tree. |
| FIFO full or transfer timeout | Check triggers and acquisition, processing, and disk throughput. |
| Alternating correlation results | Confirm both ping-pong branches use the corrected board pairing. |

## Source and validation

- [StreamThruGPU_Simple.c](StreamThruGPU_Simple.c): acquisition, configuration, calibration, buffering, and output lifecycle.
- [DSPEquation_Simple_GPU.cu](DSPEquation_Simple_GPU.cu): CUDA kernels, cuBLAS reduction, and result serialization.
- [DSPEquation_Simple_CPU.c](DSPEquation_Simple_CPU.c): CPU processing code.
- [pipe.cpp](pipe.cpp): named-pipe communication.
- [Correlation mathematics](Correlation_Matrix_Computation_Math.pdf): supporting reference.
- [Protocol tests](../GageStreamThruGPU.Tests/README.md): pipe tests without digitizer hardware or CUDA kernels.
- [Workflow demo](../GageStreamThruGPU.WorkflowDemo/README.md): separate workflow demonstration.
- [Hardware smoke client](../GageStreamThruGPU.HardwareSmoke/README.md): short acquisition against the real backend.

Protocol tests do not validate GPU numerical results or physical synchronization. Validate these with acquisition hardware and known input signals.

## Recent fixes

- Corrected GPU ping-pong pairing so both branches pair board 1 with board 2.
- Restored calibration minimum/maximum tracking for edge thresholds.
- Updated triangle-lock alignment; the current source selects the triangle minimum (see calibration equations below).
- Removed automatic relaunch on missing calibration signals.
- Added experiment-directory creation before opening result files.

## Source notices

Preserve the GaGe Applied Technologies copyright and usage notices in the acquisition source and follow their conditions when modifying or redistributing it.

## Implementation reference: acquisition loop

The main program handles the client handshake, SDK/configuration setup, worker startup, acquisition control, and cleanup. `CardStreamThread` manages the two-board transfers and analysis. Use the following sequence when porting or restructuring it.

| Stage | Current behavior |
| --- | --- |
| Prepare | Open experiment outputs; allocate two stream buffers per board; register/map host buffers for CUDA; allocate analysis arrays and initialize cuBLAS. |
| Select buffers | Even loop counts receive into `pBuffer11`/`pBuffer21`; odd counts receive into `pBuffer12`/`pBuffer22`. |
| Start transfers | Call `CsStmTransferToBuffer` for each board's current buffer. |
| Process previous buffers | Calibrate or analyze the completed work buffers while the new transfers are outstanding. Never analyze the buffers currently being filled. |
| Save and communicate | Optionally write raw work buffers and handle runtime pipe requests. IPC views use the calibration sample offsets. |
| Wait and inspect | Wait for both transfers with `CsStmGetTransferStatus`, then inspect completion and error flags. |
| Rotate | Promote current buffers to work buffers, assign their matching GPU pointers, and increment the loop counter. |
| Finish | Handle final raw output where applicable, release resources, and return to main-program cleanup. |

Startup has a deliberate warm-up: count `0` fills the first pair without previous data to process; count `1` calibrates from that first completed pair; GPU correlation runs only when `u32LoopCount > 1`. Preserve this distinction when deciding which buffers produce results. Do not assume every acquired buffer produces a correlation record or that final raw-file handling also flushes a final correlation result.

The board pairing must remain `d_buffer11` with `d_buffer21`, or `d_buffer12` with `d_buffer22`. Mixing pairs or using board 1 twice can produce alternating incorrect matrices.

## Implementation reference: calibration equations

Calibration runs once, using completed **host** work buffers. A frame contains one sample from each of the two channels: `ch1, ch2, ch1, ch2, ...`. Frame offsets therefore become interleaved sample offsets by multiplying by two.

### 1. Find a shared timing reference

Read board 1 channel 2 and board 2 channel 1 over the first `min(number_of_frames, 2048)` frames. Track each signal's minimum and maximum and set its threshold to their midpoint:

```text
threshold0 = (low0 + high0) / 2
threshold1 = (low1 + high1) / 2
```

For each signal, select the first index `i >= 1` satisfying `previous < threshold` and `current >= threshold`. Let these rising-edge indices be `edge0` and `edge1`:

```text
delta_frames  = edge1 - edge0
delta_samples = 2 * delta_frames
```

A positive delta means the reference edge appears later in board 2's buffer, so board 2 needs a larger starting offset. This aligns sample indices in software; it does not adjust the hardware clocks or continuously correct clock drift.

### 2. Lock the triangle phase

Inspect board 1 channel 1 over its first eight frames. **The current implementation selects the minimum**, stored as `tri_low_frame`, and aims to place that minimum at frame index `3` (the fourth pixel) in the GPU view.

```text
period_frames     = 8
target_frame      = 3
base_skip_samples = 16                # eight two-channel frames
extra_frames      = (tri_low_frame - target_frame) mod 8
skip0_samples     = 16 + 2 * extra_frames
skip1_samples     = skip0_samples + delta_samples
```

The source implements modulo eight with `& (period - 1)`, which relies on the period being a power of two. The base skip is one full triangle period, so it does not change phase.

Example: if `edge0=100`, `edge1=103`, and `tri_low_frame=6`, then `delta_samples=6`, `extra_frames=3`, `skip0_samples=22`, and `skip1_samples=28`. The aligned reference edges both appear at frame `89`; the triangle minimum appears at frame `3` modulo eight.

### 3. Apply and retain offsets

```text
GPU input for board 1 = mapped work-buffer pointer 1 + skip0_samples
GPU input for board 2 = mapped work-buffer pointer 2 + skip1_samples
```

Pointer arithmetic is in `short` samples, not bytes. The offsets are reused for later buffers, and runtime IPC uses corresponding offsets into the host buffers. If either reference edge is absent, the worker signals error/abort and exits. Bypass mode instead marks calibration complete with both offsets zero.

### Requirements for a future implementation

- Validate both edges before committing offsets or marking calibration complete. The current code computes offsets before its missing-edge check.
- Initialize extrema from actual samples or numeric limits. The current `low=15000`, `high=0` seeds are assumptions that should not be carried into a general implementation.
- Reject flat/noisy references or add a defined amplitude and hysteresis policy; first midpoint crossing alone is not a noise-quality check.
- Check that both offsets are nonnegative and that each offset plus the requested processing length stays inside the completed buffer. A negative relative skew can otherwise produce an invalid board 2 offset.
- Keep period, channel selection, and phase target explicit. The declared `target_idx=89` is not used by the active offset equations; the triangle target is `target_pixel_frame=3`.
- Define reset/recalibration behavior for repeated runs and drift. The current calibration state is static and the measurement is one-time.
- Test positive/negative skew, all eight triangle phases, missing edges, and short buffers with synthetic data before validating against hardware.

## Current limitation: G2 autocorrelation throughput

**The G2 autocorrelation path (`CorrelationType=1`) is not yet optimized. In the current experimental setup, its processing time exceeds the buffer-transfer duration, so it cannot finish processing the previous buffer within the next buffer's transfer interval.** This is the reported operational limitation, not a new benchmark measured for this documentation update.

The G2 routine builds per-segment autocorrelation arrays for both inputs, reduces each with cuBLAS matrix-vector operations, performs a matrix-matrix operation, normalizes the result, copies it to the host, synchronizes, and writes results. With `M = corrMatrixSize`, it returns `M * M` doubles, compared with `M` for the cross-correlation output. Both computation and output volume therefore need attention.

The ping-pong loop overlaps processing with transfer, but it starts the next iteration only after processing, optional file/IPC work, and transfer-status handling finish. When processing exceeds the available interval, transfer scheduling falls behind and sustained acquisition can exhaust hardware buffering. Extra buffering alone cannot fix a sustained throughput deficit.

For a future optimization, measure kernels, cuBLAS operations, host copies, and result writing separately; verify numerical correctness and matrix layout before optimizing; then require the complete processing/output path to fit within the measured buffer cadence with headroom. `profile.txt` reports a combined "Transfer and process Time", not an isolated DMA duration, and the timed GPU routine includes result writing. Use separate timing instrumentation to establish the actual transfer/processing budget. Do not describe G2 as sustaining real-time streaming until this is validated on the target setup.
