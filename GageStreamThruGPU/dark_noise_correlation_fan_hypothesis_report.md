# Dark Noise Correlation Test Summary

Date: 2026-04-23

## Purpose

This note summarizes the recent dark-noise tests performed to understand why the correlation matrix develops an unusual correlated structure after the system has been running for about 30 seconds.

The main question is whether the correlation is caused by the optical setup, the photodetectors, the digitizer, the computer location, or a thermal/cooling problem in the acquisition computer.

## Main Observation

In recent tests, the abnormal correlation appears after roughly 30 seconds regardless of the measurement configuration.

The effect appears even when changing the surrounding setup, which suggests it is not simply caused by one optical path condition or by the physical position of the computer.

## Tests Already Tried

Several dark-noise or reduced-hardware configurations were tested:

| Test Condition | Result |
|---|---|
| Computer moved to different position | Weird correlation still appears after about 30 seconds |
| Dark noise with fan | Weird correlation still appears |
| Optical table cooled down | Weird correlation still appears |
| Digitizer-only test | Weird correlation still appears |
| Photodetector-connected test | Weird correlation still appears |

The common feature is that the unwanted correlation is delayed in time and appears after the system has been running for a while.

## Comparison With Earlier Dark Noise Data

Earlier this year, dark-noise analysis showed much better behavior. The older C code had some known errors, so the old results were not perfect, but they still showed qualitatively good dark-noise behavior.

This matters because it suggests the present problem may not be purely from the correlation algorithm. If the older setup showed acceptable dark-noise behavior even with imperfect code, then something in the current hardware or environment may have changed.

## Important Difference Found

The main hardware difference identified is the computer cooling fan condition.

Previously:

- There were two working fans.
- Cooling was likely stronger and more stable.

Now:

- One fan is down.
- The remaining fan is unstable or inefficient.

This is a strong suspect because the correlation problem appears after a delay, around 30 seconds. A delayed onset is consistent with a thermal or cooling-related issue, such as:

- digitizer temperature drift,
- unstable clocking or timing caused by heat,
- power supply or motherboard thermal behavior,
- increased electronic noise after the system warms up,
- reduced airflow around the acquisition card.

## Reference Correlation Calibration Test

A reference-channel correction was also tested as a possible way to remove the unwanted correlation.

The idea was to use the two non-signal channels as a reference correlation and subtract that from the signal-channel correlation:

```text
C_corrected = C_signal - alpha * C_reference
```

where:

```text
C_signal    = correlation from the normal signal channels
C_reference = correlation from the other two channels
alpha       = scale factor
```

Two versions were considered:

1. A background/reference matrix version, where the reference correlation is measured separately and subtracted later.
2. A same-kernel version, where `corrValueRef` is evaluated inside the CUDA cross-correlation kernel and subtracted immediately:

```text
aggregatedCorrMatrix = corrValue - alpha * corrValueRef
```

The same-kernel version matched the intended real-time structure better, because each segment would be corrected before the matrix reduction step.

However, the test made the dark-noise result worse rather than better. This suggests that the reference-channel correlation is not a clean copy of the unwanted artifact in the signal-channel correlation.

Likely reasons:

- The reference channels may contain calibration or synchronization structure rather than only background pickup.
- The artifact is not equally distributed across all four channels.
- A fixed `alpha` is probably not valid for all matrix elements.
- The unwanted correlation may be frequency-dependent, especially near the previously observed 250 MHz component.
- Subtracting the reference per segment can add noise if the reference fluctuation is not truly correlated with the signal-channel error.

The result of this calibration test is therefore negative:

```text
Reference-channel subtraction is not currently a reliable correction.
```

This is useful information. It means the abnormal correlation should not be treated as a simple removable offset between channel pairs. The problem is more likely related to hardware behavior, timing, clocking, or thermal stability.

## Working Hypothesis

The current abnormal dark-noise correlation may be caused by insufficient or unstable cooling in the acquisition computer.

The fan problem is not proven to be the cause yet, but it is the most consistent explanation among the tested conditions because:

1. The problem appears across multiple optical and detector configurations.
2. The problem appears after a delay rather than immediately.
3. Earlier dark-noise data looked better under a different cooling condition.
4. The current computer fan state is clearly degraded.

## Recommended Next Test

Buy or install a stronger and more reliable fan, then repeat the dark-noise tests.

Recommended test sequence:

1. Run the system with the current weak/unstable fan and record the time when the correlation appears.
2. Install the stronger fan.
3. Repeat the same dark-noise acquisition with the same settings.
4. Compare:
   - time before abnormal correlation appears,
   - mean correlation matrix,
   - diagonal/off-diagonal structure,
   - raw standard deviation over time,
   - log-log convergence behavior.

If the stronger fan removes the delayed correlation or pushes it much later in time, that would strongly support the cooling hypothesis.

## Suggested Controls

To make the fan test convincing, keep these conditions fixed:

- same digitizer configuration,
- same acquisition duration,
- same computer location if possible,
- same photodetector connection state,
- same optical table condition,
- same post-processing pipeline,
- same `cm.bin` analysis settings.

It would also be useful to record computer or digitizer temperature if any sensor is available.

## Current Conclusion

The recent dark-noise issue is probably not caused only by the optical table, photodetectors, or computer position. The most suspicious change is the degraded cooling condition of the acquisition computer.

The next practical step is to install a stronger fan and repeat the dark-noise test. If the delayed correlation disappears or becomes much weaker, cooling should be treated as a key requirement for reliable dark-noise and correlation measurements.
