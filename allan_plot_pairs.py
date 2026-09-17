"""Plot exactly two software-style log-log correlation-matrix pair curves.

Edit the values in USER SETTINGS, then run:

    python allan_plot_pairs.py

Each entry in PAIRS is (row_1, column_1, row_2, column_2).  The plotted
time series is CM[row_1, column_1] - CM[row_2, column_2].  Matrix coordinates
are one-based, matching the labels used by the measurement software.
"""

from __future__ import annotations

import json
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np


# =============================================================================
# USER SETTINGS
# =============================================================================

# Copy and paste the run-folder path between the quotes. Forward slashes and
# backslashes are both accepted on Windows.
RUN_FOLDER = r"D:/Quantum Squeezing Project/DataFiles/20260112_095617"

# Enter exactly two curves. Each curve is the difference between the two
# matrix coordinates in one four-number tuple: (row_1, col_1, row_2, col_2).
PAIRS = [
    (3, 2, 8, 7),
    (1, 7, 2, 8),
]

# Figure width and height in inches. For a smaller plot, try (4.0, 3.0).
FIGURE_SIZE = (5.0, 3.5)

# Fallback time per frame when profile.txt does not contain frame timestamps.
FRAME_DT_S = 67.0 / 4861.0

# This matches the software's plotting limit. Every frame is used to calculate
# the running mean; only the displayed line is thinned if it exceeds this size.
MAX_LINE_PLOT_POINTS = 50_000

# Same raw-correlation-to-V^2 factor used by the post-processing software.
RAW_TO_V2 = (0.24**2) / (32768.0**2)

# Image resolution. The output is saved directly inside RUN_FOLDER.
OUTPUT_DPI = 300


def validate_settings(run_folder: Path) -> None:
    """Check the editable settings and provide direct error messages."""
    if not run_folder.is_dir():
        raise FileNotFoundError(f"Run folder not found: {run_folder}")
    if len(PAIRS) != 2:
        raise ValueError(f"PAIRS must contain exactly two entries; found {len(PAIRS)}.")
    if len(FIGURE_SIZE) != 2 or any(float(value) <= 0 for value in FIGURE_SIZE):
        raise ValueError("FIGURE_SIZE must contain two positive values: (width, height).")
    if FRAME_DT_S <= 0:
        raise ValueError("FRAME_DT_S must be positive.")

    for pair in PAIRS:
        if len(pair) != 4 or any(not isinstance(value, (int, np.integer)) for value in pair):
            raise ValueError(
                f"Each PAIRS entry must contain four integers, for example (1, 1, 8, 8): {pair}"
            )
        if any(value < 1 or value > 8 for value in pair):
            raise ValueError(f"Matrix coordinates must be between 1 and 8: {pair}")


def load_cm_frames(run_folder: Path) -> np.ndarray:
    """Read cm.bin as a stack of 8-by-8 float64 correlation matrices."""
    cm_path = run_folder / "cm.bin"
    if not cm_path.is_file():
        raise FileNotFoundError(f"cm.bin not found: {cm_path}")

    raw = np.fromfile(cm_path, dtype=np.float64)
    if raw.size == 0:
        raise ValueError(f"cm.bin is empty: {cm_path}")
    if raw.size % 64:
        raise ValueError(f"cm.bin does not contain complete 8x8 float64 frames: {cm_path}")
    return raw.reshape(-1, 8, 8)


def software_display_scaling(run_folder: Path) -> tuple[float, str]:
    """Return the scale and unit recorded by the software in metadata.json."""
    metadata_path = run_folder / "metadata.json"
    if not metadata_path.is_file():
        raise FileNotFoundError(
            f"metadata.json not found: {metadata_path}. "
            "It is needed to match the software's display units."
        )

    try:
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        physics = metadata["PhysicsData"]
    except (json.JSONDecodeError, KeyError, TypeError) as error:
        raise ValueError(f"Could not read PhysicsData from {metadata_path}") from error

    attenuator_scale = 1.0
    if physics.get("PowerDetectorAttenuatorApplied", False):
        attenuator_scale = float(physics.get("PowerDetectorAttenuatorCorrectionFactor", 1.0))
        if not np.isfinite(attenuator_scale) or attenuator_scale <= 0:
            raise ValueError("PowerDetectorAttenuatorCorrectionFactor must be positive.")

    raw_to_v2 = RAW_TO_V2 * attenuator_scale
    display_unit = str(physics.get("DisplayAmplitudeUnit", "")).strip().lower()
    if display_unit in {"v2", "v^2", "v²"}:
        return raw_to_v2, r"V$^2$"

    conversion_factor = float(physics.get("ConversionFactor_V2_rad2", np.nan))
    if not np.isfinite(conversion_factor) or conversion_factor <= 0:
        raise ValueError(
            "metadata.json does not contain a positive ConversionFactor_V2_rad2."
        )
    return raw_to_v2 * 1e12 / conversion_factor, r"$\mu rad^2$"


def pair_time_series(
    frames: np.ndarray,
    pair: tuple[int, int, int, int],
) -> np.ndarray:
    """Return CM[row_1, col_1] - CM[row_2, col_2] for every frame."""
    row_1, col_1, row_2, col_2 = pair
    return frames[:, row_1 - 1, col_1 - 1] - frames[:, row_2 - 1, col_2 - 1]


def load_frame_times(run_folder: Path, frame_count: int) -> np.ndarray:
    """Read the same per-frame time values that the software reads from profile.txt."""
    profile_path = run_folder / "profile.txt"
    if not profile_path.is_file():
        return np.arange(frame_count, dtype=float) * FRAME_DT_S

    import re
    from datetime import datetime

    text = profile_path.read_text(encoding="utf-8", errors="ignore")
    tokens = re.findall(r"start timestamp:\s*(\S+)", text)
    parsed_times = []
    for token in tokens[:frame_count]:
        for time_format in ("%H:%M:%S.%f", "%H:%M:%S"):
            try:
                parsed_times.append(datetime.strptime(token.strip(), time_format))
                break
            except ValueError:
                continue

    if not parsed_times:
        return np.arange(frame_count, dtype=float) * FRAME_DT_S

    seconds_of_day = np.asarray(
        [
            time.hour * 3600
            + time.minute * 60
            + time.second
            + time.microsecond / 1_000_000.0
            for time in parsed_times
        ],
        dtype=float,
    )

    # profile.txt stores only a clock time, not a calendar date. Unwrap a large
    # backward jump as midnight so post-midnight frames continue to increasing
    # averaging times instead of being clamped back to FRAME_DT_S.
    unwrapped_times = seconds_of_day.copy()
    day_offset = 0.0
    for index in range(1, unwrapped_times.size):
        if seconds_of_day[index] < seconds_of_day[index - 1] - 43_200.0:
            day_offset += 86_400.0
        unwrapped_times[index] += day_offset

    relative_times = unwrapped_times - unwrapped_times[0]

    # Repeated timestamps are valid, but any small out-of-order clock samples
    # must not make the plotted time axis run backward.
    relative_times = np.maximum.accumulate(relative_times)
    if relative_times.size < frame_count:
        missing_count = frame_count - relative_times.size
        extension = relative_times[-1] + FRAME_DT_S * np.arange(1, missing_count + 1)
        relative_times = np.concatenate((relative_times, extension))
    return relative_times[:frame_count]


def software_running_mean_curve(values: np.ndarray) -> np.ndarray:
    """Use every input frame to reproduce the software's cumulative mean curve."""
    values = np.asarray(values, dtype=float)
    if values.size < 1:
        raise ValueError("At least one correlation-matrix frame is needed.")
    if not np.all(np.isfinite(values)):
        raise ValueError("The selected pair contains NaN or infinite values.")

    divisors = np.arange(1, values.size + 1, dtype=float)
    curve = np.abs(np.cumsum(values) / divisors)
    curve[~np.isfinite(curve) | (curve <= 0)] = np.nan
    return curve


def downsample_for_plot(x_values: np.ndarray, y_values: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Apply the same 50,000-point display cap as the software."""
    if x_values.size <= MAX_LINE_PLOT_POINTS:
        return x_values, y_values
    step = int(np.ceil(x_values.size / MAX_LINE_PLOT_POINTS))
    return x_values[::step], y_values[::step]


def pair_label(pair: tuple[int, int, int, int]) -> str:
    row_1, col_1, row_2, col_2 = pair
    return f"({row_1},{col_1})-({row_2},{col_2})"


def make_plot(
    frames: np.ndarray,
    frame_times: np.ndarray,
    display_scale: float,
    display_unit: str,
    output_path: Path,
) -> None:
    """Draw and save the two requested software running-mean curves."""
    plt.style.use("default")
    plt.rcParams.update(
        {
            "figure.facecolor": "white",
            "axes.facecolor": "white",
            "axes.spines.top": False,
            "axes.spines.right": False,
        }
    )
    fig, axis = plt.subplots(figsize=FIGURE_SIZE)
    x_values = np.maximum(frame_times - frame_times[0], FRAME_DT_S)

    for pair in PAIRS:
        displayed_values = pair_time_series(frames, pair) * display_scale
        full_curve = software_running_mean_curve(displayed_values)
        plot_x, plot_y = downsample_for_plot(x_values, full_curve)
        axis.loglog(
            plot_x,
            plot_y,
            linewidth=1.8,
            label=pair_label(pair),
        )

    axis.set_title(r"Running Mean of Cumsum ($\mu rad^2$), Selected Pairs", fontsize=10)
    axis.set_xlabel("Time (s)", fontsize=9)
    axis.set_ylabel(f"|Running Mean| ({display_unit})", fontsize=9)
    axis.tick_params(labelsize=8)
    axis.grid(True, which="both", linestyle="--", alpha=0.3)
    axis.legend(
        loc="center left",
        bbox_to_anchor=(1.01, 0.5),
        frameon=False,
        fontsize=8,
    )
    fig.tight_layout()
    fig.savefig(output_path, dpi=OUTPUT_DPI, bbox_inches="tight")
    plt.close(fig)


def main() -> None:
    run_folder = Path(RUN_FOLDER.strip().strip('"')).expanduser().resolve()
    validate_settings(run_folder)

    # The final folder name is the run time, e.g. "20260804_142332".
    run_time = run_folder.name
    output_path = run_folder / f"{run_time}_allan_plot_two_pairs.png"

    frames = load_cm_frames(run_folder)
    frame_times = load_frame_times(run_folder, frames.shape[0])
    display_scale, display_unit = software_display_scaling(run_folder)
    make_plot(frames, frame_times, display_scale, display_unit, output_path)
    displayed_count = min(frames.shape[0], int(np.ceil(frames.shape[0] / np.ceil(frames.shape[0] / MAX_LINE_PLOT_POINTS))))
    print(f"Each curve used all {frames.shape[0]:,} frames.")
    print(f"Each displayed line contains {displayed_count:,} points after the software plot cap.")
    print(f"Y-axis unit matched to software metadata: {display_unit}")
    print(f"Saved Allan plot to: {output_path}")


if __name__ == "__main__":
    main()
