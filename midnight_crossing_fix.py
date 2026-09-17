"""Generate a midnight-safe refinement of the software Allan/log-log plot.

Set ``RUN_FOLDER`` in USER SETTINGS and run:

    python refined_allan_plot.py

The script reads ``cm.bin``, ``profile.txt``, and ``metadata.json`` from the
selected run folder.  It writes ``refined_loglog_eval.png`` into that same
folder and does not modify any input file.
"""

from __future__ import annotations

import json
import math
import re
from datetime import datetime
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np


# =============================================================================
# USER SETTINGS
# =============================================================================

# Paste the complete run-folder path between the quotes.
# Example: r"D:\\Quantum Squeezing Project\\DataFiles\\20260808_230000"
RUN_FOLDER = r"D:\Quantum Squeezing Project\DataFiles\20260909_233355"


# These settings match the current post-processing Allan/log-log evaluation.
FRAME_DT_S = 67.0 / 4861.0
RAW_TO_V2 = (0.24**2) / (32768.0**2)
CRITICAL_PAIR_COUNT = 9
MAX_LINE_PLOT_POINTS = 50_000
OUTPUT_DPI = 300
OUTPUT_FILENAME = "refined_loglog_eval.png"

# The 47 diagonal-tail pairs used by the current post-processing pipeline.
# Matrix coordinates are one-based.
PAIRS = np.asarray(
    [
        [1, 1, 8, 8],
        [1, 2, 7, 8],
        [1, 3, 6, 8],
        [1, 4, 5, 8],
        [1, 5, 4, 8],
        [1, 6, 3, 8],
        [1, 7, 2, 8],
        [2, 2, 8, 8],
        [2, 3, 7, 8],
        [2, 4, 6, 8],
        [2, 5, 5, 8],
        [2, 6, 4, 8],
        [2, 7, 3, 8],
        [3, 3, 8, 8],
        [3, 4, 7, 8],
        [3, 5, 6, 8],
        [3, 6, 5, 8],
        [3, 7, 4, 8],
        [4, 4, 8, 8],
        [4, 5, 7, 8],
        [4, 6, 6, 8],
        [4, 7, 5, 8],
        [5, 5, 8, 8],
        [5, 6, 7, 8],
        [5, 7, 6, 8],
        [6, 6, 8, 8],
        [6, 7, 7, 8],
        [7, 7, 8, 8],
        [2, 1, 8, 7],
        [3, 1, 8, 6],
        [4, 1, 8, 5],
        [5, 1, 8, 4],
        [6, 1, 8, 3],
        [7, 1, 8, 2],
        [3, 2, 8, 7],
        [4, 2, 8, 6],
        [5, 2, 8, 5],
        [6, 2, 8, 4],
        [7, 2, 8, 3],
        [4, 3, 8, 7],
        [5, 3, 8, 6],
        [5, 4, 8, 7],
        [6, 4, 8, 6],
        [7, 4, 8, 5],
        [6, 5, 8, 7],
        [7, 5, 8, 6],
        [7, 6, 8, 7],
    ],
    dtype=int,
)


def validate_run_folder() -> Path:
    """Resolve the editable folder setting and validate required inputs."""
    entered_path = RUN_FOLDER.strip().strip('"')
    if not entered_path:
        raise ValueError(
            "RUN_FOLDER is empty. Paste the run-folder path into RUN_FOLDER "
            "near the top of refined_allan_plot.py."
        )

    run_folder = Path(entered_path).expanduser().resolve()
    if not run_folder.is_dir():
        raise FileNotFoundError(f"Run folder not found: {run_folder}")

    for filename in ("cm.bin", "profile.txt"):
        path = run_folder / filename
        if not path.is_file():
            raise FileNotFoundError(f"Required input not found: {path}")
    return run_folder


def read_cm(run_folder: Path) -> np.ndarray:
    """Read cm.bin as one 64-element correlation matrix per frame."""
    path = run_folder / "cm.bin"
    raw = np.fromfile(path, dtype=np.float64)
    if raw.size == 0:
        raise ValueError(f"cm.bin is empty: {path}")
    if raw.size % 64:
        raise ValueError(f"cm.bin does not contain complete 8x8 float64 frames: {path}")
    return raw.reshape(-1, 64)


def parse_clock_seconds(value: str) -> float:
    """Convert an HH:MM:SS[.fraction] clock value to seconds of day."""
    match = re.fullmatch(r"(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?", value.strip())
    if match is None:
        raise ValueError(f"Unsupported profile timestamp: {value!r}")

    hours, minutes, seconds = (int(match.group(i)) for i in range(1, 4))
    if hours > 23 or minutes > 59 or seconds > 59:
        raise ValueError(f"Invalid profile timestamp: {value!r}")
    fraction = float(f"0.{match.group(4)}") if match.group(4) else 0.0
    return hours * 3600.0 + minutes * 60.0 + seconds + fraction


def unwrap_clock_times(seconds_of_day: np.ndarray) -> tuple[np.ndarray, int]:
    """Turn clock-only timestamps into a continuous, monotonic time axis.

    A drop of more than 12 hours is treated as a midnight crossing and adds one
    day to that sample and all later samples.  Small backward clock jitter is
    clamped so elapsed/averaging time can never move backward on the plot.
    """
    unwrapped = np.empty_like(seconds_of_day, dtype=float)
    unwrapped[0] = seconds_of_day[0]
    day_offset = 0.0
    midnight_crossings = 0

    for index in range(1, seconds_of_day.size):
        if seconds_of_day[index] - seconds_of_day[index - 1] < -43_200.0:
            day_offset += 86_400.0
            midnight_crossings += 1
        unwrapped[index] = seconds_of_day[index] + day_offset

    # Guard against small non-midnight clock reversals without remapping a later
    # running-mean sample to an earlier averaging time.
    np.maximum.accumulate(unwrapped, out=unwrapped)
    return unwrapped, midnight_crossings


def read_elapsed_times(run_folder: Path, frame_count: int) -> tuple[np.ndarray, int, int]:
    """Read, unwrap, and align profile timestamps to the CM frame count."""
    profile_path = run_folder / "profile.txt"
    text = profile_path.read_text(encoding="utf-8", errors="ignore")
    tokens = re.findall(r"start timestamp:\s*(\S+)", text, flags=re.IGNORECASE)
    if not tokens:
        raise ValueError(f"No 'start timestamp' entries were found in {profile_path}")

    parsed: list[float] = []
    for token in tokens[:frame_count]:
        try:
            parsed.append(parse_clock_seconds(token))
        except ValueError:
            continue
    if not parsed:
        raise ValueError(f"No usable frame timestamps were found in {profile_path}")

    unwrapped, midnight_crossings = unwrap_clock_times(np.asarray(parsed, dtype=float))
    parsed_count = unwrapped.size

    # profile.txt can be shorter than cm.bin if acquisition stopped abruptly.
    # Extend only the missing tail using the known processed-frame cadence.
    if parsed_count < frame_count:
        missing_count = frame_count - parsed_count
        extension = unwrapped[-1] + FRAME_DT_S * np.arange(1, missing_count + 1)
        unwrapped = np.concatenate((unwrapped, extension))

    elapsed = unwrapped[:frame_count] - unwrapped[0]
    np.maximum.accumulate(elapsed, out=elapsed)
    return elapsed, midnight_crossings, parsed_count


def metadata_display_scale(run_folder: Path) -> tuple[float, str]:
    """Return the same raw-to-display scale and unit recorded by the software."""
    metadata_path = run_folder / "metadata.json"
    if not metadata_path.is_file():
        raise FileNotFoundError(
            f"metadata.json not found: {metadata_path}. It is required to match plot units."
        )

    try:
        payload = json.loads(metadata_path.read_text(encoding="utf-8"))
        physics = payload["PhysicsData"]
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

    conversion_factor = float(physics.get("ConversionFactor_V2_rad2", math.nan))
    if not np.isfinite(conversion_factor) or conversion_factor <= 0:
        raise ValueError(
            "metadata.json does not contain a positive ConversionFactor_V2_rad2."
        )
    return raw_to_v2 * 1e12 / conversion_factor, r"$\mu rad^2$"


def matrix_index(row: int, column: int) -> int:
    return (row - 1) * 8 + column - 1


def critical_pair_indices(cm: np.ndarray) -> np.ndarray:
    """Select the nine critical pairs using the software's exact MSE score."""
    channel_mse = np.nanmean((cm - np.nanmean(cm, axis=0, keepdims=True)) ** 2, axis=0)
    first = np.asarray([matrix_index(pair[0], pair[1]) for pair in PAIRS])
    second = np.asarray([matrix_index(pair[2], pair[3]) for pair in PAIRS])

    # Match cm_pipeline_all_in_one.py::critical_pair_indices: each predefined
    # pair is scored by the mean MSE of its two endpoint correlation channels.
    scores = np.nanmean(np.column_stack((channel_mse[first], channel_mse[second])), axis=1)
    ranked = np.argsort(np.nan_to_num(scores, nan=-np.inf))[::-1]
    finite_ranked = ranked[np.isfinite(scores[ranked])]
    if finite_ranked.size == 0:
        raise ValueError("No finite correlation-pair MSE values are available to plot.")
    return finite_ranked[: min(CRITICAL_PAIR_COUNT, finite_ranked.size)]


def pair_label(pair: np.ndarray) -> str:
    row_1, column_1, row_2, column_2 = pair
    return f"({row_1},{column_1})-({row_2},{column_2})"


def downsample_for_plot(x_values: np.ndarray, y_values: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Limit rendered points without changing the full running-mean calculation."""
    if x_values.size <= MAX_LINE_PLOT_POINTS:
        return x_values, y_values
    step = int(math.ceil(x_values.size / MAX_LINE_PLOT_POINTS))
    return x_values[::step], y_values[::step]


def save_refined_plot(
    run_folder: Path,
    cm: np.ndarray,
    elapsed_times: np.ndarray,
    display_scale: float,
    display_unit: str,
) -> Path:
    """Calculate the full running means and save the corrected log-log plot."""
    selected_indices = critical_pair_indices(cm)
    selected_pairs = PAIRS[selected_indices]
    first = np.asarray([matrix_index(pair[0], pair[1]) for pair in selected_pairs])
    second = np.asarray([matrix_index(pair[2], pair[3]) for pair in selected_pairs])

    differences = (cm[:, first] - cm[:, second]) * display_scale
    divisors = np.arange(1, cm.shape[0] + 1, dtype=float)[:, None]
    y_values = np.abs(np.cumsum(differences, axis=0) / divisors)
    y_values[~np.isfinite(y_values) | (y_values <= 0)] = np.nan

    # Log axes cannot display zero. Clamp only sub-frame elapsed values to the
    # nominal frame duration. Since elapsed_times is already midnight-unwrapped,
    # every later point keeps its true averaging time and the axis never reverses.
    x_values = np.maximum(elapsed_times, FRAME_DT_S)

    plt.style.use("default")
    plt.rcParams.update(
        {
            "figure.facecolor": "white",
            "axes.facecolor": "white",
            "axes.spines.top": False,
            "axes.spines.right": False,
            "savefig.dpi": OUTPUT_DPI,
            "agg.path.chunksize": 10_000,
        }
    )
    fig, axis = plt.subplots(figsize=(9, 6.5))
    for column, pair in enumerate(selected_pairs):
        plot_x, plot_y = downsample_for_plot(x_values, y_values[:, column])
        axis.loglog(plot_x, plot_y, linewidth=1.5, label=pair_label(pair))

    axis.set_title("Refined Log-Log Evaluation (Midnight Corrected)")
    axis.set_xlabel("Averaging Time (s)")
    axis.set_ylabel(f"|Running Mean| ({display_unit})")
    axis.grid(True, which="both", linestyle="--", alpha=0.3)
    axis.legend(loc="center left", bbox_to_anchor=(1.01, 0.5), frameon=False)
    fig.tight_layout(rect=(0, 0.055, 1, 1))

    output_path = run_folder / OUTPUT_FILENAME
    cm_path = run_folder / "cm.bin"
    stat = cm_path.stat()
    modified = datetime.fromtimestamp(stat.st_mtime).isoformat(timespec="seconds")
    provenance = (
        f"Run folder: {run_folder.resolve()} | Raw: cm.bin | "
        f"bytes={stat.st_size} | modified={modified}"
    )
    fig.text(
        0.005,
        0.006,
        provenance,
        ha="left",
        va="bottom",
        fontsize=6,
        color="#555555",
        wrap=True,
    )
    fig.savefig(
        output_path,
        dpi=OUTPUT_DPI,
        bbox_inches="tight",
        metadata={
            "SourceRunFolder": str(run_folder.resolve()),
            "SourceRawFile": str(cm_path.resolve()),
            "SourceRawBytes": str(stat.st_size),
            "SourceRawModified": modified,
            "Provenance": provenance,
            "VisibleRunFolderFootnote": "lower-left",
        },
    )
    plt.close(fig)
    return output_path


def main() -> None:
    run_folder = validate_run_folder()
    cm = read_cm(run_folder)
    elapsed_times, midnight_crossings, parsed_count = read_elapsed_times(
        run_folder, cm.shape[0]
    )
    display_scale, display_unit = metadata_display_scale(run_folder)
    output_path = save_refined_plot(
        run_folder, cm, elapsed_times, display_scale, display_unit
    )

    print(f"Used all {cm.shape[0]:,} correlation-matrix frames.")
    print(f"Read {parsed_count:,} profile timestamps.")
    print(f"Corrected {midnight_crossings} midnight crossing(s).")
    print(f"Final averaging time: {elapsed_times[-1]:,.6f} s")
    print(f"Saved refined Allan plot to: {output_path}")


if __name__ == "__main__":
    main()
