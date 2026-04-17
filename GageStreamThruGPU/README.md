# GageStreamThruGPU-Simple

## Recent update

This update fixes several acquisition and analysis issues that were causing unstable
output, alternating correlation results, and unwanted restart behavior.

### Fixes included

- Fixed GPU ping-pong buffer pairing in `StreamThruGPU_Simple.c`.
  The first buffer branch incorrectly assigned `d_buffer2` to `d_buffer11`
  instead of `d_buffer21`, which caused alternating analysis results because one
  half of the stream used `board1/board1` and the other half used `board1/board2`.

- Restored calibration threshold min/max tracking.
  The calibration loop was reading samples but not updating `lo0`, `hi0`, `lo1`,
  or `hi1`, which made edge detection unreliable and could break skew alignment.

- Restored triangle-lock peak detection.
  The triangle-lock code had been changed to search for a minimum instead of a
  maximum, which shifted the computed GPU skip offsets and misaligned the data
  used for analysis.

- Removed automatic self-restart on calibration failure.
  When calibration signals were missing, the program aborted and restarted
  immediately. It now exits the run cleanly without relaunching itself.

- Added a defensive experiment-directory creation step.
  The program now creates the experiment output directory before opening analysis,
  profile, and `cm.bin` files so standalone runs do not fail when the UI has not
  already created the folder.

### Expected result

After this update:

- correlation and `cm.bin` output should no longer alternate between correct and
  incorrect matrices
- calibration should produce stable edge and triangle-lock measurements
- runs should stop cleanly instead of entering an automatic restart loop
- experiment output files should be created more reliably
