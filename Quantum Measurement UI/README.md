# Quantum Measurement UI

This project is the WPF frontend for QuantaMeasure. It is responsible for:

- connecting to the `GageStreamThruGPU` named-pipe server
- showing live signal and correlation data
- coordinating DAQ and ESP300 control flows
- launching post-run FFT and analysis steps
- browsing saved experiment results

## Main Files

- `MainWindow.xaml`: top-level UI layout
- `MainWindow.xaml.cs`: startup and window initialization
- `Window_Logic/Chart_Init_Functions.cs`: chart setup for OxyPlot and LiveCharts views
- `Window_Logic/Data_Update.cs`: named-pipe reads, live frame processing, heatmap updates, and matrix accumulation
- `Window_Logic/Click_Event.cs`: button actions, FFT export flow, and user-triggered operations
- `Window_Logic/Experiment_Motor_Control.cs`: experiment start/stop lifecycle
- `UI_and_Logging.cs`: message log, history loading, and experiment-history actions
- `Window_Logic/Info.md`: longer file-by-file explanation of the partial `MainWindow` class

## Current Live Behavior

- The live matrix path now uses all received frames by default.
- The previous even-frame-only accumulation path has been removed from the main WPF heatmap and matrix-balance flow.
- OxyPlot axes used by the live UI are locked against pan and zoom so fixed ranges stay stable during acquisition.
- The heatmap, matrix summary, and related counters now reflect processed frames rather than accepted-even-frame terminology.

## Experiment History Shortcuts

The **Experiment History** tab can open common analysis outputs directly from the selected experiment folder:

- `loglog_eval.png`
- `diagonal_offset_matrix_urad2.png`
- `diagonal_offset_matrix_V2.png` as the diagonal-offset fallback
- `fft_result.png`
- `raw_std_over_time.png`

These shortcuts are implemented in `UI_and_Logging.cs`.

## Analysis Notes

- FFT export is produced by the UI flow and saved as `fft_result.png`.
- The external post-processing pipeline can generate additional analysis images such as `loglog_eval.png`, diagonal-offset maps, and `raw_std_over_time.png`.
- Shot-noise metadata may be stored in either `urad^2` or `V^2` form depending on the post-processing threshold logic.

## Build

```powershell
dotnet build ".\Quantum Measurement UI\Quantum Measurement UI.csproj"
```

If the UI executable is currently running, build may fail at the final copy step because `Quantum Measurement UI.exe` is locked by the active process.

## Related Projects

- `GageStreamThruGPU`: acquisition and correlation-matrix backend
- `QuantumDAQService`: DAQ-side monitoring
- `prototype and postprocessing/post processing`: offline Python analysis pipeline
