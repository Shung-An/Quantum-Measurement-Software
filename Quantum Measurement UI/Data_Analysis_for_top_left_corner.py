"""Software-format analysis restricted to the top-left 3x3 pulse region."""

from __future__ import annotations

import csv
import json
import math
import re
import warnings
from datetime import datetime
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

try:
    import imageio.v2 as imageio
except ImportError:
    imageio = None

# =============================================================================
# USER SETTINGS
# =============================================================================

RUN_FOLDER = Path(r"D:\Quantum Squeezing Project\DataFiles\20260804_142332")

# Use the complete final folder name, including year, month, day, hour, minute,
# and second. For example, RUN_FOLDER ending in "20260804_142332" produces
# the output folder "20260804_142332_top_left3x3_analysis".
RUN_TIME_STRING = RUN_FOLDER.name
OUTPUT_FOLDER = RUN_FOLDER / f"{RUN_TIME_STRING}_top_left3x3_analysis"


# These values match the Quantum Measurement Software post-processing setup.
BIN_MM = 0.1
MM_TO_PS = 6.6
FRAME_DT_S = 67.0 / 4861.0
RAW_TO_V2 = (0.24**2) / (32768.0**2)
MAX_PLOT_POINTS = 50_000


def output_path(filename: str) -> Path:
    """Prefix every generated filename with the complete run timestamp."""
    return OUTPUT_FOLDER / f"{RUN_TIME_STRING}_{filename}"

# Same diagonal-tail pairs as the software, restricted to pulse endpoints in
# matrix rows 1-3 and columns 1-3.
PAIRS = np.asarray(
    [
        [1, 1, 8, 8],
        [1, 2, 7, 8],
        [1, 3, 6, 8],
        [2, 2, 8, 8],
        [2, 3, 7, 8],
        [3, 3, 8, 8],
        [2, 1, 8, 7],
        [3, 1, 8, 6],
        [3, 2, 8, 7],
    ],
    dtype=int,
)


def style_plots() -> None:
    plt.style.use("default")
    plt.rcParams.update(
        {
            "figure.facecolor": "white",
            "axes.facecolor": "white",
            "axes.grid": True,
            "grid.alpha": 0.25,
            "grid.linestyle": "--",
            "axes.spines.top": False,
            "axes.spines.right": False,
            "font.size": 11,
            "axes.titlesize": 15,
            "axes.labelsize": 12,
            "legend.fontsize": 10,
            "savefig.dpi": 300,
            "agg.path.chunksize": 10_000,
        }
    )


def pair_labels() -> list[str]:
    return [f"({r1},{c1})-({r2},{c2})" for r1, c1, r2, c2 in PAIRS]


def index_of(row: int, column: int) -> int:
    return (row - 1) * 8 + column - 1


def read_cm() -> np.ndarray:
    path = RUN_FOLDER / "cm.bin"
    if not path.is_file():
        raise FileNotFoundError(f"cm.bin not found: {path}")
    raw = np.fromfile(path, dtype=np.float64)
    if raw.size == 0 or raw.size % 64:
        raise ValueError("cm.bin does not contain complete 64-term float64 frames.")
    return raw.reshape(-1, 64)


def parse_time(value: str) -> float:
    match = re.match(r"\s*(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?", value)
    if not match:
        raise ValueError(value)
    fraction = float("0." + match.group(4)) if match.group(4) else 0.0
    return int(match.group(1)) * 3600 + int(match.group(2)) * 60 + int(match.group(3)) + fraction


def unwrap_midnight(values: np.ndarray) -> np.ndarray:
    result = values.astype(float, copy=True)
    for index in range(1, result.size):
        if result[index] < result[index - 1] - 43_200:
            result[index:] += 86_400
    return result


def frame_times(frame_count: int) -> tuple[np.ndarray, np.ndarray]:
    profile = RUN_FOLDER / "profile.txt"
    if profile.is_file():
        tokens = re.findall(
            r"start timestamp:\s*(\S+)",
            profile.read_text(encoding="utf-8", errors="ignore"),
            flags=re.IGNORECASE,
        )
        parsed = []
        for token in tokens[:frame_count]:
            try:
                parsed.append(parse_time(token))
            except ValueError:
                pass
        if parsed:
            absolute = unwrap_midnight(np.asarray(parsed))
            if absolute.size < frame_count:
                extension = absolute[-1] + FRAME_DT_S * np.arange(1, frame_count - absolute.size + 1)
                absolute = np.concatenate((absolute, extension))
            absolute = absolute[:frame_count]
            return absolute - absolute[0], absolute

    relative = np.arange(frame_count, dtype=float) * FRAME_DT_S
    return relative, relative.copy()


def positions_at_frames(times_relative: np.ndarray, times_absolute: np.ndarray) -> np.ndarray:
    position_path = None
    for name in ("delay_stage_positions.log", "delay_stage_positions.csv"):
        candidate = RUN_FOLDER / name
        if candidate.is_file():
            position_path = candidate
            break
    if position_path is None:
        warnings.warn("No position log found; using the software default position 25.058 mm.")
        return np.full(times_relative.size, 25.058)

    log_times, log_positions = [], []
    for line in position_path.read_text(encoding="utf-8", errors="ignore").splitlines():
        fields = [part.strip() for part in re.split(r"[,;]", line) if part.strip()]
        try:
            log_times.append(parse_time(fields[0]))
            log_positions.append(float(fields[-1]))
        except (IndexError, ValueError):
            continue
    if not log_times:
        raise ValueError(f"No positions could be parsed from {position_path}")

    position_times = unwrap_midnight(np.asarray(log_times))
    position_times += round((times_absolute[0] - position_times[0]) / 86_400) * 86_400
    relative_position_times = position_times - times_absolute[0]
    unique_times, unique_indices = np.unique(relative_position_times, return_index=True)
    unique_positions = np.asarray(log_positions)[unique_indices]
    return np.interp(
        times_relative,
        unique_times,
        unique_positions,
        left=unique_positions[0],
        right=unique_positions[-1],
    )


def analysis_units() -> tuple[float, str, bool]:
    """Return display multiplier, label, and whether the output remains V^2."""
    sensitivity = RUN_FOLDER / "sensitivity.log"
    text = sensitivity.read_text(encoding="utf-8", errors="ignore") if sensitivity.is_file() else ""

    def value(pattern: str) -> float:
        match = re.search(pattern, text, flags=re.IGNORECASE)
        return float(match.group(1)) if match else math.nan

    conversion = value(r"Conversion Factor\s*=\s*([\d.Ee+\-]+)")
    p1 = value(r"P1\s*=\s*([\d.Ee+\-]+)\s*mW")
    p2 = value(r"P2\s*=\s*([\d.Ee+\-]+)\s*mW")
    shot_result = value(r"Shot Noise Result\s*=\s*([\d.Ee+\-]+)")

    finite_powers = [power for power in (p1, p2) if np.isfinite(power)]
    total_power = float(np.sum(finite_powers)) if finite_powers else 0.0
    dark_or_invalid = (
        not np.isfinite(conversion)
        or conversion <= 0
        or total_power <= 0.05
        or (np.isfinite(shot_result) and shot_result > 10_000)
    )
    if dark_or_invalid:
        return 1.0, "V$^2$", True
    return 1e12 / conversion, r"$\mu rad^2$", False


def attenuator_correction() -> float:
    path = RUN_FOLDER / "metadata.json"
    if not path.is_file():
        return 1.0
    try:
        physics = json.loads(path.read_text(encoding="utf-8")).get("PhysicsData", {})
        applied = physics.get("PowerDetectorAttenuatorApplied", False)
        factor = float(physics.get("PowerDetectorAttenuatorCorrectionFactor", 1.0))
        return factor if applied and np.isfinite(factor) and factor > 0 else 1.0
    except (ValueError, TypeError, json.JSONDecodeError):
        return 1.0


def provenance() -> str:
    cm_path = RUN_FOLDER / "cm.bin"
    stat = cm_path.stat()
    modified = datetime.fromtimestamp(stat.st_mtime).isoformat(timespec="seconds")
    return f"Run folder: {RUN_FOLDER.resolve()} | Raw: cm.bin | bytes={stat.st_size} | modified={modified}"


def save_figure(fig: plt.Figure, filename: str) -> None:
    fig.text(0.005, 0.006, provenance(), ha="left", va="bottom", fontsize=6, color="#555555", wrap=True)
    fig.savefig(output_path(filename), bbox_inches="tight", metadata={"SourceRunFolder": str(RUN_FOLDER.resolve())})


def downsample(x: np.ndarray, y: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    if x.size <= MAX_PLOT_POINTS:
        return x, y
    step = int(math.ceil(x.size / MAX_PLOT_POINTS))
    return x[::step], y[::step]


def critical_pair_order(cm: np.ndarray) -> list[int]:
    channel_mse = np.mean((cm - np.mean(cm, axis=0, keepdims=True)) ** 2, axis=0)
    scores = []
    for r1, c1, r2, c2 in PAIRS:
        scores.append(np.mean([channel_mse[index_of(r1, c1)], channel_mse[index_of(r2, c2)]]))
    order = np.argsort(np.asarray(scores))[::-1]

    with output_path("critical_pairs_summary.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["pair_index", "label", "mse_corr_pair_score"])
        labels = pair_labels()
        for index in order:
            writer.writerow([int(index), labels[index], f"{scores[index]:.6e}"])
    return [int(index) for index in order]


def save_loglog(cm: np.ndarray, times: np.ndarray, order: list[int], scale: float, unit: str) -> None:
    first = np.asarray([index_of(*pair[:2]) for pair in PAIRS])
    second = np.asarray([index_of(*pair[2:]) for pair in PAIRS])
    differences = cm[:, first] - cm[:, second]
    running_mean = np.cumsum(differences, axis=0) / np.arange(1, cm.shape[0] + 1)[:, None]
    y_values = np.abs(running_mean * scale)
    y_values[y_values <= 0] = np.nan

    fig, axis = plt.subplots(figsize=(9, 6.5))
    x_values = np.maximum(times - times[0], FRAME_DT_S)
    labels = pair_labels()
    for index in order:
        plot_x, plot_y = downsample(x_values, y_values[:, index])
        axis.loglog(plot_x, plot_y, label=labels[index])
    axis.set_title("Log-Log Evaluation (Cleaned)")
    axis.set_xlabel("Time (s)")
    axis.set_ylabel(f"|Running Mean| ({unit})")
    axis.legend(loc="center left", bbox_to_anchor=(1.01, 0.5), frameon=False)
    fig.tight_layout(rect=(0, 0.04, 1, 1))
    save_figure(fig, "loglog_eval.png")
    plt.close(fig)


def save_emergence(
    cm: np.ndarray,
    positions: np.ndarray,
    order: list[int],
    scale: float,
    unit: str,
) -> None:
    first = np.asarray([index_of(*pair[:2]) for pair in PAIRS])
    second = np.asarray([index_of(*pair[2:]) for pair in PAIRS])
    differences = cm[:, first] - cm[:, second]

    rounded = np.round(positions / BIN_MM) * BIN_MM
    bins, inverse = np.unique(rounded, return_inverse=True)
    members = [differences[inverse == index] for index in range(bins.size)]
    counts = np.asarray([member.shape[0] for member in members], dtype=int)
    cumulative = [np.cumsum(member, axis=0) * scale for member in members]
    common_frames = int(np.min(counts))
    delays = bins * MM_TO_PS
    labels = pair_labels()

    final_amplitudes = np.asarray([values[common_frames - 1] / common_frames for values in cumulative])
    with output_path("final_amplitudes_all_pairs.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["Delay_ps", *labels])
        for delay, row in zip(delays, final_amplitudes, strict=False):
            writer.writerow([f"{delay:.6f}", *row])

    fig, axis = plt.subplots(figsize=(11, 7))
    for index in order:
        axis.plot(delays, final_amplitudes[:, index], "o-", linewidth=1.8, markersize=4, label=labels[index])
    axis.set_title(
        "Critical Pairs Summary\n"
        f"Integration: {common_frames} Frames ({common_frames * FRAME_DT_S:.2f}s) | "
        f"Total processed: {cm.shape[0]} Frames ({cm.shape[0] * FRAME_DT_S:.2f}s)"
    )
    axis.set_xlabel("Delay (ps)")
    axis.set_ylabel(f"Amplitude ({unit})")
    axis.legend(loc="center left", bbox_to_anchor=(1.01, 0.5), frameon=False)
    fig.tight_layout(rect=(0, 0.04, 1, 1))
    save_figure(fig, "final_clean_result.png")
    plt.close(fig)

    if imageio is None or common_frames < 2:
        warnings.warn("imageio is unavailable or there are fewer than two frames per bin; MP4 skipped.")
        return

    steps = np.unique(np.round(np.linspace(1, common_frames, min(400, common_frames))).astype(int))
    fig, axis = plt.subplots(figsize=(12, 7))
    fig.text(0.005, 0.006, provenance(), ha="left", va="bottom", fontsize=6, color="#555555", wrap=True)
    lines = [axis.plot([], [], "o-", linewidth=1.5, markersize=4, label=labels[index])[0] for index in order]
    axis.set_xlabel("Delay (ps)")
    axis.set_ylabel(f"Amplitude ({unit})")
    axis.set_xlim(float(np.min(delays)), float(np.max(delays)))
    axis.legend(loc="center left", bbox_to_anchor=(1.01, 0.5), frameon=False)

    with imageio.get_writer(output_path("signal_emergence.mp4"), fps=15, macro_block_size=1) as writer:
        for frame_count in steps:
            current = np.asarray([values[frame_count - 1] / frame_count for values in cumulative])
            selected = current[:, order]
            for column, line in enumerate(lines):
                line.set_data(delays, selected[:, column])
            finite = selected[np.isfinite(selected)]
            if finite.size:
                low, high = float(np.min(finite)), float(np.max(finite))
                span = high - low
                pad = max(0.05 * span, 0.05 * max(abs(low), abs(high), 1.0), 1e-6)
                axis.set_ylim(low - pad, high + pad)
            axis.set_title(f"Integration: {frame_count} Frames ({frame_count * FRAME_DT_S:.2f}s)")
            fig.tight_layout(rect=(0, 0.055, 1, 1))
            fig.canvas.draw()
            writer.append_data(np.asarray(fig.canvas.buffer_rgba())[:, :, :3].copy())
    plt.close(fig)


def main() -> None:
    if not RUN_FOLDER.is_dir():
        raise FileNotFoundError(f"Experiment folder not found: {RUN_FOLDER}")

    OUTPUT_FOLDER.mkdir(parents=True, exist_ok=True)
    style_plots()
    raw_cm = read_cm()
    times_relative, times_absolute = frame_times(raw_cm.shape[0])
    positions = positions_at_frames(times_relative, times_absolute)
    cm_v2 = raw_cm * RAW_TO_V2 * attenuator_correction()
    scale, unit, _display_in_v2 = analysis_units()
    order = critical_pair_order(cm_v2)

    save_loglog(cm_v2, times_relative, order, scale, unit)
    save_emergence(cm_v2, positions, order, scale, unit)

    print(f"Analyzed {raw_cm.shape[0]:,} correlation-matrix frames.")
    print(f"Original run folder was read only: {RUN_FOLDER}")
    print(f"All generated files were written to: {OUTPUT_FOLDER}")


if __name__ == "__main__":
    main()
