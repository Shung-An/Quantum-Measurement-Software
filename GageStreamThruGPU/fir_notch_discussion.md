# FIR Notch Discussion

This note summarizes the discussion around the demodulation subtraction used in
the GaGe stream-through-GPU correlation code and what it means for residual
correlation after filtering.

## Where the FIR Comes From

The math PDF describes the correlation terms as differences between two halves
of a segment:

```text
Cij = (Ai - Ai+8) (Bj - Bj+8)
```

The CPU implementation matches this structure:

```c
value1 = segment[i * 2];
value2 = segment[(i + 8) * 2];
value3 = segment[j * 2 + 1];
value4 = segment[(j + 8) * 2 + 1];
result = (value1 - value2) * (value3 - value4);
```

Because raw samples are interleaved as:

```text
A1, B1, A2, B2, ..., A16, B16
```

the operation is equivalent to:

```text
yA[i] = x[2i]     - x[2(i+8)]
yB[i] = x[2i + 1] - x[2(i+8) + 1]
```

So this is a two-nonzero-tap FIR difference, but the delay is:

```text
8 per-channel samples = 16 raw interleaved ADC samples
```

## Equivalent Filter

In raw ADC sample units:

```text
h[n] = delta[n] - delta[n - 16]
H(f) = 1 - exp(-j 2 pi f 16 / Fs_raw)
|H(f)| = 2 |sin(16 pi f / Fs_raw)|
```

For `Fs_raw = 1 GHz`, the notch spacing is:

```text
Fs_raw / 16 = 62.5 MHz
```

The notches are therefore:

```text
0, 62.5, 125, 187.5, 250, 312.5, 375, 437.5, 500 MHz
```

This means the filter should perfectly reject an exactly centered `Fs/4`
component:

```text
Fs_raw / 4 = 250 MHz
```

## Synthetic Test

A reusable synthetic test script was added:

```text
GageStreamThruGPU/fir_notch_test.py
```

Run it with:

```powershell
python GageStreamThruGPU\fir_notch_test.py --fs-mhz 1000 --out GageStreamThruGPU\fir_notch_response.png
```

The saved plot is:

```text
GageStreamThruGPU/fir_notch_response.png
```

The test directly matches the code indexing:

```text
yA[i] = x[2i] - x[2(i+8)]
yB[i] = x[2i+1] - x[2(i+8)+1]
```

Result around `Fs/4`:

```text
250 MHz exactly:      about -202 dB in the numerical test
250 MHz + 1 kHz:      about -80 dB
250 MHz + 10 kHz:     about -60 dB
250 MHz + 100 kHz:    about -40 dB
250 MHz + 1 MHz:      about -20 dB
```

So the notch is exact at the center frequency, but the deep-rejection region is
narrow.

## Notch Bandwidth

Around a notch center `f0`, for small frequency offset `Delta f`:

```text
|H(f0 + Delta f)| ~= 2 pi 16 |Delta f| / Fs_raw
```

For `Fs_raw = 1 GHz`, the approximate notch widths are:

| Rejection Level | Half-Width | Full Width |
| ---: | ---: | ---: |
| -20 dB | 0.995 MHz | 1.99 MHz |
| -40 dB | 99.5 kHz | 199 kHz |
| -60 dB | 9.95 kHz | 19.9 kHz |
| -80 dB | 995 Hz | 1.99 kHz |
| -100 dB | 99.5 Hz | 199 Hz |

The conventional full `-3 dB` notch width relative to the passband peak is:

```text
Fs_raw / 32 = 31.25 MHz
```

However, for correlation cleanup the deep-rejection bandwidth is usually more
important than the `-3 dB` width.

## Can It Cancel Common Mode?

It can cancel common-mode components that are identical across the FIR delay.
For a constant offset:

```text
x[n] = signal[n] + C
y[n] = x[n] - x[n-16]
```

the constant `C` cancels.

It does not cancel every common-mode component. Any common-mode signal that
changes over the 16 raw-sample delay survives according to the same response:

```text
common[n] - common[n-16]
```

The filter strongly rejects the comb-notch frequencies but strongly passes the
frequencies halfway between notches.

For `Fs_raw = 1 GHz`, passband peaks occur at:

```text
31.25, 93.75, 156.25, 218.75, 281.25, 343.75, 406.25, 468.75 MHz
```

## Why Residual Correlation Can Remain

If finite correlation remains after this FIR, likely causes include:

- The correlated component is close to, but not exactly at, `250 MHz`.
- The component drifts or broadens during the integration time.
- FFT bin leakage spreads energy away from the exact notch.
- The residual is produced after the differencing step.
- There is channel-to-channel crosstalk with frequency-dependent phase or gain.
- There is shared-clock or board-synchronous pickup outside the notch centers.
- The effective sample rate or indexing differs from the assumed raw `1 GHz`
  interleaved sample stream.

The important conclusion is:

```text
The FIR perfectly cancels the exact Fs/4 tone, but it does not guarantee that
all nearby, drifting, or differently phased correlated components disappear.
```

## Possible Further Cancellation

For remaining deterministic correlation, the next useful approach is complex
cross-spectrum subtraction. For channels `X` and `Y`, estimate:

```text
alpha(f) = <Y(f) X*(f)> / <X(f) X*(f)>
```

Then subtract the measured leakage:

```text
Y_clean(f) = Y(f) - alpha(f) X(f)
```

This should be calibrated using data where the true physical signal is known to
be uncorrelated. Otherwise, real physical correlation may be accidentally
removed.

