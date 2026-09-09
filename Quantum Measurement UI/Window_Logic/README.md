# Window_Logic

Implementation guide to the WPF main-window behavior on `v1.6-2cards-sync-skew`: experiment startup and shutdown, live data updates, NI-DAQ monitoring, instrument control, metadata, and analysis actions.

Most files extend the same `partial MainWindow` class. They share fields and UI controls; they are not independent services or separately runnable scripts. `ExperimentRecord.cs` defines data models used by experiment history and metadata.

[UI overview](../README.md) · [Repository overview](../../README.md) · [Window layout](../MainWindow.xaml) · [Window initialization](../MainWindow.xaml.cs)

## File guide

| File | Responsibility | Open when changing… |
| --- | --- | --- |
| [Chart_Init_Functions.cs](Chart_Init_Functions.cs) | Chart models, series, axes, and initial bindings | Plot setup, display ranges, or a new chart |
| [Click_Event.cs](Click_Event.cs) | Button handlers, alignment actions, motor commands, FFT processing/export, and analysis actions | User actions, raw-data FFT, or analysis launch behavior |
| [Configuration_Process_and_Closing.cs](Configuration_Process_and_Closing.cs) | Backend process launch, INI updates, run folders, DAQ connection/reading, scanning, and window cleanup | Installation paths, DAQ page behavior, configuration, or shutdown |
| [Constants_Fields.cs](Constants_Fields.cs) | Shared constants, buffers, chart state, controllers, and cancellation/process fields | Timing, dimensions, paths, or shared state |
| [Data_Update.cs](Data_Update.cs) | GaGe pipe connection/reads, live correlation processing, chart updates, motor-position updates, and DAQ signal monitoring | Received-data interpretation, accumulation, or live display |
| [ESP300.cs](ESP300.cs) | Delay-stage commands, program control, settings, and position monitoring | ESP300 behavior |
| [Experiment_Metadata.cs](Experiment_Metadata.cs) | Reading metadata from controls and applying a metadata draft to the UI/configuration | Experiment metadata fields or FFT-save selection |
| [Experiment_Motor_Control.cs](Experiment_Motor_Control.cs) | Experiment orchestration, start/terminate sequences, and motor workflows | Run lifecycle and coordination between components |
| [ExperimentRecord.cs](ExperimentRecord.cs) | Experiment/analysis metadata models and history display properties | Saved metadata compatibility, units, or history columns |
| [Info.md](Info.md) | Earlier architectural notes | Historical context; some names and descriptions predate the current layout |

Related files outside this folder:

- [MainWindow.xaml](../MainWindow.xaml) defines controls and event bindings; [MainWindow.xaml.cs](../MainWindow.xaml.cs) initializes the window.
- [UI_and_Logging.cs](../UI_and_Logging.cs) handles logging, history, and result shortcuts. It lives in the parent folder.
- [PipeClient.cs](../PipeClient.cs) implements the **NI-DAQ** command protocol.
- [ExperimentMetadataDraft.cs](../ExperimentMetadataDraft.cs) and [ExperimentMetadataDialog.xaml](../ExperimentMetadataDialog.xaml) support metadata editing.
- [MotorController.cs](../MotorController.cs), [ESPController.cs](../ESPController.cs), and [Autobalancer.cs](../Autobalancer.cs) provide controller/balancing code.

## Experiment workflow

### Start

The start button delegates to `StartExperimentAsync` in `Experiment_Motor_Control.cs`:

1. Terminate an existing run if one is active.
2. Initialize the experiment log/folder and runtime INI before starting the GaGe backend.
3. Launch the backend, initiate the DAQ connection, and connect the GaGe named-pipe client.
4. Read external-clock status, start elapsed-time tracking, update indicators, and save initial metadata.
5. Start the delay-stage program, finish DAQ connection setup, and begin signal monitoring.
6. Send the GaGe start command (`short` value `1`) followed by the ASCII experiment directory token.
7. Start autobalancing, wait for the current settling delay, enable live updates, and start DAQ/motor-position update flows.

The GaGe backend expects a 15-byte experiment token. Preserve this contract when changing run-folder naming. Backend board alignment and calibration happen in [GageStreamThruGPU](../../GageStreamThruGPU/README.md#implementation-reference-calibration-equations), not in this folder.

### Receive and display

`StartDataUpdates` launches a cancellable background loop. `UpdateData` observes the pause state, requests a frame, and dispatches chart updates onto the WPF UI thread.

`RequestAndReceiveDataAsync` reads the expected signal payload and correlation matrix with exact-length reads, then calls `ProcessReceivedCorrelationMatrixFrame`. The current main live path uses all received frames by default. Chart initialization and runtime updates are split between `Chart_Init_Functions.cs` and `Data_Update.cs`.

Pausing the UI update loop should not be interpreted as stopping digitizer acquisition. Follow the explicit termination path to end a run.

### Terminate

`TerminateExperimentAsync` cancels update/monitoring tasks, stops balancing and stage activity, releases DAQ resources, sends the GaGe stop command (`short` value `3`) when connected, closes the pipe, and waits for the backend process to exit. It also completes run logging and subsequent end-of-run handling.

Window closure has a separate cleanup path in `MainWindow_Closing`. When adding a timer, task, device connection, or child process, account for both experiment termination and window closure. Existing fixed delays are not proof that every background operation has completed.

## Two different pipe protocols

| Connection | Implementation | Contract |
| --- | --- | --- |
| GaGe acquisition: `DataPipe` | `Data_Update.cs` and experiment lifecycle code | Binary `short` command codes and fixed-layout signal/matrix payloads; startup includes the experiment token |
| NI-DAQ monitoring: `QuantumDAQPipe` | Parent `PipeClient.cs`, called by DAQ and monitoring flows | Four-byte length prefix followed by UTF-8 command/response text |

These protocols are not interchangeable. The DAQ client serializes command/response exchanges with a semaphore; GaGe reads follow the backend's binary layout. See the [GaGe guide](../../GageStreamThruGPU/README.md) and [DAQ protocol guide](../../QuantumDAQService/README.md#pipe-framing) before changing either side.

## NI-DAQ monitoring

`Connection` can launch the NI service and sends `StartAI ai0,ai1,ai2,ai3,ai4,ai5`. `StartAutoRead` and the experiment signal-monitoring flow request recent samples with `ReadAI`; response parsing and NI chart updates reside in `Configuration_Process_and_Closing.cs`.

The current configuration requests 150 samples/channel and uses a 100 ms DAQ UI refresh interval. The parser and chart logic assume six interleaved channels. Changing that layout requires coordinated changes to buffer sizes, parsing, statistics, and plotting.

The service returns rolling snapshots, not a lossless stream: successive polls may overlap or omit samples. Do not infer continuous sampling or synchronization with GaGe from UI refresh timestamps. Read the [DAQ sample-layout and timing notes](../../QuantumDAQService/README.md#sample-layout-and-timing).

## Metadata, FFT, and history

- `Experiment_Metadata.cs` transfers metadata between controls and the draft model. Applying the FFT option also updates `StmConfig/SaveToFile` in the runtime INI.
- `Click_Event.cs` includes raw interleaved FFT analysis, plot/CSV export, and FFT metadata updates, alongside external analysis-launch actions. Check the selected input files and configured sample rate when changing FFT behavior.
- `ExperimentRecord.cs` supplies metadata models and derived history-display properties. Preserve JSON field compatibility and unit meaning when adding or renaming fields.
- Parent `UI_and_Logging.cs` opens history assets such as `loglog_eval.png`, diagonal-offset maps, `fft_result.png`, and `raw_std_over_time.png`. Availability depends on which analysis steps ran.

## Current constraints

- Live OxyPlot axes are locked against pan/zoom, and the main matrix path accumulates all received frames by default.
- The main GaGe client uses a fixed matrix/payload contract. Changing correlation dimensions requires coordinated backend, buffer, display, and reader changes.
- **G2 autocorrelation remains unoptimized in the backend and takes longer than the buffer-transfer interval in the current setup.** UI refresh changes do not resolve that processing deficit; see [G2 throughput](../../GageStreamThruGPU/README.md#current-limitation-g2-autocorrelation-throughput).
- Executable paths, vendor references, and external analysis locations are installation-specific. Check `Constants_Fields.cs`, process-launch methods, and the parent project references when moving to another machine.
- USB bypass/simulated DAQ paths can help exercise UI behavior but do not validate real instrument communication, synchronization, or throughput.

## Implementation checklist

1. Find the control/event binding in `MainWindow.xaml`, then trace its handler and shared fields using the file guide.
2. Keep blocking device/process work off the UI thread and marshal control/plot changes through the dispatcher.
3. Define ownership and cancellation for new background tasks. Avoid creating duplicate polling loops and ensure partial-start failures release acquired resources.
4. Preserve protocol framing and exact payload lengths. A display-only change should not silently alter the acquisition contract.
5. Keep acquisition state, display pause state, and device connection state distinct.
6. Validate metadata/output changes against existing experiment folders as well as new runs.

## Build and verification

This folder builds as part of the parent WPF project. From the repository root:

```powershell
dotnet build ".\Quantum Measurement UI\Quantum Measurement UI.csproj"
```

Follow the [UI setup guide](../README.md) and [repository dependency overview](../../README.md#build-and-setup) for Windows, .NET 8, and vendor references. Close a running UI if it locks the build output.

For behavior changes, check start/stop, pause/resume, repeated runs, window closure, missing backend/device handling, metadata reload, and affected plots. Use real hardware for acquisition/calibration checks. The GaGe protocol tests do not cover WPF lifecycle behavior or the separate NI-DAQ protocol.
