"""Synthetic notch test for the demodulation differencing operation.

This matches the indexing used in DSPEquation_Simple_CPU.c and
DSPEquation_Simple_GPU.cu:

    yA[i] = segment[i * 2]     - segment[(i + 8) * 2]
    yB[i] = segment[i * 2 + 1] - segment[(i + 8) * 2 + 1]

For raw interleaved ADC samples, that is a delay of 16 raw samples.
"""

from __future__ import annotations

import argparse
import math
from pathlib import Path

import numpy as np


def response_raw(freq_hz: np.ndarray, fs_raw_hz: float, delay_raw_samples: int = 16) -> np.ndarray:
    return np.abs(2.0 * np.sin(np.pi * freq_hz * delay_raw_samples / fs_raw_hz))


def code_indexed_rms_ratio(freq_hz: float, fs_raw_hz: float, segments: int = 4096) -> tuple[float, float]:
    n_raw = np.arange(segments * 32, dtype=np.float64)
    x = np.cos(2.0 * np.pi * freq_hz * n_raw / fs_raw_hz + 0.123)
    seg = x.reshape(-1, 32)

    da = seg[:, 0:16:2] - seg[:, 16:32:2]
    db = seg[:, 1:16:2] - seg[:, 17:32:2]
    in_a = seg[:, 0:16:2]
    in_b = seg[:, 1:16:2]

    ratio_a = float(np.sqrt(np.mean(da * da)) / np.sqrt(np.mean(in_a * in_a)))
    ratio_b = float(np.sqrt(np.mean(db * db)) / np.sqrt(np.mean(in_b * in_b)))
    return ratio_a, ratio_b


def db(value: float) -> float:
    if value <= 0.0:
        return -400.0
    return 20.0 * math.log10(value)


def main() -> None:
    parser = argparse.ArgumentParser(description="Test the Ai - Ai+8 demodulation notch locations.")
    parser.add_argument("--fs-mhz", type=float, default=1000.0, help="Raw interleaved sample rate in MHz.")
    parser.add_argument("--out", type=Path, default=Path("fir_notch_response.png"), help="Output plot path.")
    args = parser.parse_args()

    fs_raw_hz = args.fs_mhz * 1e6
    freq_mhz = np.linspace(0.0, args.fs_mhz / 2.0, 4001)
    h = response_raw(freq_mhz * 1e6, fs_raw_hz)

    test_freqs_mhz = [
        0.0,
        31.25,
        62.5,
        93.75,
        125.0,
        187.5,
        249.9,
        250.0,
        250.1,
        312.5,
        375.0,
        437.5,
        500.0,
    ]

    print("Matched to code indexing: yA[i] = x[2i] - x[2(i+8)]")
    print(f"Raw Fs: {args.fs_mhz:.3f} MHz")
    print(f"Per-channel Fs: {args.fs_mhz / 2.0:.3f} MHz")
    print("Delay: 16 raw samples = 8 per-channel samples")
    print()
    print(f"{'f_raw (MHz)':>12} {'theory |H|':>12} {'A ratio':>12} {'B ratio':>12} {'dB':>10}")
    print("-" * 64)
    for f_mhz in test_freqs_mhz:
        ratio_a, ratio_b = code_indexed_rms_ratio(f_mhz * 1e6, fs_raw_hz)
        ratio = max(ratio_a, ratio_b)
        theory = float(response_raw(np.array([f_mhz * 1e6]), fs_raw_hz)[0])
        print(f"{f_mhz:12.2f} {theory:12.6g} {ratio_a:12.6g} {ratio_b:12.6g} {db(ratio):10.2f}")

    print()
    print("Near Fs/4:")
    fs4 = fs_raw_hz / 4.0
    for df_hz in [0.0, 1.0, 10.0, 100.0, 1_000.0, 10_000.0, 100_000.0, 1_000_000.0]:
        h_near = float(response_raw(np.array([fs4 + df_hz]), fs_raw_hz)[0])
        print(f"Fs/4 + {df_hz:9.0f} Hz: |H|={h_near:.9e}, dB={db(h_near):8.2f}")

    try:
        import matplotlib.pyplot as plt
    except ImportError:
        print("\nmatplotlib is not installed; skipping plot.")
        return

    args.out.parent.mkdir(parents=True, exist_ok=True)
    plt.figure(figsize=(10, 5))
    plt.plot(freq_mhz, 20.0 * np.log10(np.maximum(h, 1e-15)))
    plt.axvline(args.fs_mhz / 4.0, color="tab:red", linestyle="--", linewidth=1.2, label="Fs/4")
    plt.title("Demodulation Difference Response: x[n] - x[n+16 raw]")
    plt.xlabel("Raw frequency (MHz)")
    plt.ylabel("Magnitude (dB)")
    plt.ylim(-120, 8)
    plt.grid(True, alpha=0.3)
    plt.legend()
    plt.tight_layout()
    plt.savefig(args.out, dpi=160)
    print(f"\nSaved plot: {args.out}")


if __name__ == "__main__":
    main()
