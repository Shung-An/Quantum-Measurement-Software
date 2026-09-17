"""Select late-time critical pairs above shot noise and plot their emergence.

Set ``RUN_FOLDER`` below to a run-folder string, then run:

    python shot_noise_exceeding_pairs.py

The run folder may also be supplied as the first command-line argument.  The
script reads the same inputs and uses the same pair ranking, scaling, delay
binning, and plot conventions as ``cm_pipeline_all_in_one.py``.  If the
profile crosses midnight, an existing ``refined_loglog_eval.png`` is used as
the Allan-analysis source.  If it is missing, ``refined_allan_plot.py`` is run
for the selected folder first.  Every other output is written directly into
the selected run folder under a distinct ``shot_noise_exceeding_*`` filename;
input files and normal analysis outputs are never modified.

A critical pair is first tested with the strict rule: every finite, non-outlier
running-mean sample at an averaging time strictly greater than 1000 seconds
must exceed the computed shot-noise result. If no pair passes that rule and
``ENABLE_CONVERGENCE_FALLBACK`` is true, the optional rule selects pairs whose
cleaned late-time curve converges to a stable level above shot noise. Sharp
downward numerical defects are excluded before either rule is evaluated. The
screening CSV records both decisions and the rule used, so the run-level signal
decision can be audited. If neither rule passes, no
``shot_noise_exceeding_*`` output is generated.
"""

from __future__ import annotations

import csv
import json
import math
import re
import sys
import warnings
from dataclasses import dataclass, replace
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

# Paste the complete run-folder path between the quotes.
# Example: r"D:\\Quantum Squeezing Project\\DataFiles\\20260816_191415"
RUN_FOLDER = r"D:\Quantum Squeezing Project\DataFiles\20260818_155010"

# The comparison uses averaging times strictly greater than this value.
MIN_AVERAGING_TIME_S = 1_000.0

# Rule 2 is only consulted when Rule 1 selects no pair.
ENABLE_CONVERGENCE_FALLBACK = True

# Downward-defect detection is performed in log(time) using the global trend
# from 10 seconds onward and a rolling trend across logarithmic time bins.
OUTLIER_TREND_START_S = 10.0
OUTLIER_LOG_BINS_PER_DECADE = 160
OUTLIER_LOCAL_WINDOW_DECADES = 0.75
OUTLIER_LOCAL_QUANTILE = 0.75
OUTLIER_GLOBAL_POLYNOMIAL_DEGREE = 3
OUTLIER_GLOBAL_CLIP_SIGMA = 3.5
OUTLIER_GLOBAL_FIT_ITERATIONS = 6
NUMERICAL_DROP_FACTOR = 10.0
OUTLIER_VALLEY_RECOVERY_RATIO = 0.75
OUTLIER_VALLEY_MAX_GAP_DECADES = 0.03

# Rule 2: test the final half-decade of clean data in equal-width log-time
# bins. A stable plateau has a small log-log slope and robust residual scatter.
CONVERGENCE_WINDOW_DECADES = 0.50
CONVERGENCE_MIN_SPAN_DECADES = 0.25
CONVERGENCE_MIN_LOG_BINS = 12
CONVERGENCE_MAX_ABS_SLOPE = 0.15
CONVERGENCE_MAX_SCATTER_DEX = 0.10
CONVERGENCE_CLIP_SIGMA = 3.5
CONVERGENCE_FIT_ITERATIONS = 6
CONVERGENCE_LOWER_QUANTILE = 0.10


# Values and formats match the software post-processing pipeline.
BIN_MM = 0.1
MM_TO_PS = 6.6
FRAME_DT_S = 67.0 / 4861.0
DETECTOR_AREA_SCALE = (0.24**2) / (32768.0**2)
SHOT_NOISE_RESULT_V2_THRESHOLD = 10_000.0
CRITICAL_PAIR_MAX_COUNT = 9
MAX_LINE_PLOT_POINTS = 50_000
OUTPUT_DPI = 300
OUTPUT_PREFIX = "shot_noise_exceeding"

# The 47 diagonal-tail pairs used by cm_pipeline_all_in_one.py. Coordinates
# are one-based so labels match the software output.
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


@dataclass(frozen=True)
class DisplaySettings:
    scale_from_scaled_v2: float
    unit_label: str
    shot_noise: float
    shot_noise_source: str
    attenuator_correction: float


@dataclass(frozen=True)
class PairScreening:
    pair_index: int
    critical_rank: int
    mse_score: float
    endpoint_mse_1: float
    endpoint_mse_2: float
    clean_tail_min: float
    tail_max: float
    tail_median: float
    tail_final: float
    exceedance_fraction: float
    first_exceedance_time_s: float
    strict_rule_passed: bool
    convergence_start_time_s: float
    convergence_end_time_s: float
    convergence_span_decades: float
    convergence_log_bin_count: int
    convergence_slope: float
    convergence_scatter_dex: float
    converged_level: float
    converged_level_lower_quantile: float
    convergence_rule_passed: bool
    selection_rule: str
    clean_sample_count: int
    downward_outlier_count: int
    downward_outlier_fraction: float
    selected: bool


@dataclass(frozen=True)
class ConvergenceResult:
    start_time_s: float = math.nan
    end_time_s: float = math.nan
    span_decades: float = math.nan
    log_bin_count: int = 0
    slope: float = math.nan
    scatter_dex: float = math.nan
    level: float = math.nan
    lower_quantile: float = math.nan
    converged: bool = False
    passed: bool = False


def output_path(run_folder: Path, suffix: str) -> Path:
    return run_folder / f"{OUTPUT_PREFIX}_{suffix}"


def style_matplotlib() -> None:
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
            "savefig.dpi": OUTPUT_DPI,
            "agg.path.chunksize": 10_000,
            "path.simplify": True,
            "path.simplify_threshold": 0.5,
        }
    )


def validate_run_folder(folder_string: str) -> Path:
    entered = folder_string.strip().strip('"')
    if not entered:
        raise ValueError(
            "RUN_FOLDER is empty. Paste the run-folder path into RUN_FOLDER "
            "near the top of shot_noise_exceeding_pairs.py, or pass it on the command line."
        )
    run_folder = Path(entered).expanduser().resolve()
    if not run_folder.is_dir():
        raise FileNotFoundError(f"Run folder not found: {run_folder}")
    if not (run_folder / "cm.bin").is_file():
        raise FileNotFoundError(f"cm.bin not found: {run_folder / 'cm.bin'}")
    return run_folder


def read_cm(run_folder: Path) -> np.ndarray:
    path = run_folder / "cm.bin"
    raw = np.fromfile(path, dtype=np.float64)
    if raw.size == 0:
        raise ValueError(f"cm.bin is empty: {path}")
    if raw.size % 64:
        raise ValueError(f"cm.bin does not contain complete 8x8 float64 frames: {path}")
    if not np.all(np.isfinite(raw)):
        raise ValueError(f"cm.bin contains NaN or infinite values: {path}")
    return raw.reshape(-1, 64)


def parse_clock_seconds(value: str) -> float:
    match = re.fullmatch(r"\s*(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?\s*", value)
    if match is None:
        raise ValueError(value)
    hours, minutes, seconds = (int(match.group(index)) for index in range(1, 4))
    if hours > 23 or minutes > 59 or seconds > 59:
        raise ValueError(value)
    fraction = float(f"0.{match.group(4)}") if match.group(4) else 0.0
    return hours * 3600.0 + minutes * 60.0 + seconds + fraction


def unwrap_clock_times(seconds_of_day: np.ndarray) -> np.ndarray:
    if seconds_of_day.size == 0:
        return seconds_of_day.astype(float)
    result = np.empty_like(seconds_of_day, dtype=float)
    result[0] = seconds_of_day[0]
    day_offset = 0.0
    for index in range(1, seconds_of_day.size):
        if seconds_of_day[index] - seconds_of_day[index - 1] < -43_200.0:
            day_offset += 86_400.0
        result[index] = seconds_of_day[index] + day_offset
    np.maximum.accumulate(result, out=result)
    return result


def read_frame_times(run_folder: Path, frame_count: int) -> tuple[np.ndarray, np.ndarray]:
    profile_path = run_folder / "profile.txt"
    if not profile_path.is_file():
        relative = np.arange(frame_count, dtype=float) * FRAME_DT_S
        return relative, relative.copy()

    text = profile_path.read_text(encoding="utf-8", errors="ignore")
    tokens = re.findall(r"start timestamp:\s*(\S+)", text, flags=re.IGNORECASE)
    parsed: list[float] = []
    for token in tokens[:frame_count]:
        try:
            parsed.append(parse_clock_seconds(token))
        except ValueError:
            continue
    if not parsed:
        relative = np.arange(frame_count, dtype=float) * FRAME_DT_S
        return relative, relative.copy()

    absolute = unwrap_clock_times(np.asarray(parsed, dtype=float))
    if absolute.size < frame_count:
        extension = absolute[-1] + FRAME_DT_S * np.arange(1, frame_count - absolute.size + 1)
        absolute = np.concatenate((absolute, extension))
    absolute = absolute[:frame_count]
    relative = absolute - absolute[0]
    np.maximum.accumulate(relative, out=relative)
    return relative, absolute


def profile_has_midnight_crossing(run_folder: Path) -> bool:
    """Return whether consecutive usable profile clock times cross midnight."""
    profile_path = run_folder / "profile.txt"
    if not profile_path.is_file():
        return False
    text = profile_path.read_text(encoding="utf-8", errors="ignore")
    tokens = re.findall(r"start timestamp:\s*(\S+)", text, flags=re.IGNORECASE)
    parsed: list[float] = []
    for token in tokens:
        try:
            parsed.append(parse_clock_seconds(token))
        except ValueError:
            continue
    return any(current - previous < -43_200.0 for previous, current in zip(parsed, parsed[1:]))


def load_refined_allan_module():
    """Load the colocated refinement module used by this software repository."""
    import importlib.util

    script_path = Path(__file__).resolve().with_name("refined_allan_plot.py")
    if not script_path.is_file():
        raise FileNotFoundError(
            "A midnight crossing was detected, but refined_allan_plot.py was not found next to "
            f"this script: {script_path}"
        )
    spec = importlib.util.spec_from_file_location("software_refined_allan_plot", script_path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Could not load the Allan refinement script: {script_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def refined_plot_has_visible_run_footer(path: Path, run_folder: Path) -> bool:
    """Check the marker written with a visibly rendered run-folder footer."""
    if not path.is_file():
        return False
    try:
        from PIL import Image

        with Image.open(path) as image:
            marker = str(image.info.get("VisibleRunFolderFootnote", "")).strip().lower()
            source = str(image.info.get("SourceRunFolder", "")).strip()
    except (ImportError, OSError):
        return False
    return marker == "lower-left" and source == str(run_folder.resolve())


def prepare_allan_source(
    run_folder: Path,
    raw_cm: np.ndarray,
    midnight_crossing: bool,
) -> tuple[np.ndarray | None, Path | None, str]:
    """Ensure a midnight-safe Allan plot and return its critical-pair indices."""
    if not midnight_crossing:
        return None, None, "original loglog_eval time axis (no midnight crossing detected)"

    refined = load_refined_allan_module()
    if not np.array_equal(np.asarray(refined.PAIRS, dtype=int), PAIRS):
        raise ValueError(
            "refined_allan_plot.py and shot_noise_exceeding_pairs.py use different pair tables."
        )

    refined_path = run_folder / str(refined.OUTPUT_FILENAME)
    refined_existed = refined_path.is_file()
    if refined_plot_has_visible_run_footer(refined_path, run_folder):
        source = f"existing {refined_path.name}"
    else:
        elapsed, midnight_crossings, _parsed_count = refined.read_elapsed_times(
            run_folder,
            raw_cm.shape[0],
        )
        if midnight_crossings < 1:
            raise ValueError(
                "The profile rollover detector found a midnight crossing, but refined_allan_plot.py did not."
            )
        display_scale, display_unit = refined.metadata_display_scale(run_folder)
        refined_path = refined.save_refined_plot(
            run_folder,
            raw_cm,
            elapsed,
            display_scale,
            display_unit,
        )
        if refined_existed:
            source = (
                f"regenerated {refined_path.name} with refined_allan_plot.py "
                "to add the lower-left run-folder footnote"
            )
        else:
            source = f"generated {refined_path.name} with refined_allan_plot.py"

    refined_indices = np.asarray(refined.critical_pair_indices(raw_cm), dtype=int)
    return refined_indices, refined_path, source


def metadata_physics(run_folder: Path) -> dict[str, object]:
    path = run_folder / "metadata.json"
    if not path.is_file():
        return {}
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError):
        return {}
    physics = payload.get("PhysicsData", {}) if isinstance(payload, dict) else {}
    return physics if isinstance(physics, dict) else {}


def finite_float(value: object) -> float:
    try:
        result = float(value)
    except (TypeError, ValueError):
        return math.nan
    return result if np.isfinite(result) else math.nan


def first_log_value(text: str, pattern: str) -> float:
    match = re.search(pattern, text, flags=re.IGNORECASE)
    return float(match.group(1)) if match else math.nan


def read_display_settings(run_folder: Path) -> DisplaySettings:
    sensitivity_path = run_folder / "sensitivity.log"
    text = (
        sensitivity_path.read_text(encoding="utf-8", errors="ignore")
        if sensitivity_path.is_file()
        else ""
    )
    conversion = first_log_value(text, r"Conversion Factor\s*=\s*([\d.Ee+\-]+)")
    shot_from_log = first_log_value(text, r"Shot Noise Result\s*=\s*([\d.Ee+\-]+)")
    p1 = first_log_value(text, r"P1\s*=\s*([\d.Ee+\-]+)\s*mW")
    p2 = first_log_value(text, r"P2\s*=\s*([\d.Ee+\-]+)\s*mW")

    physics = metadata_physics(run_folder)
    metadata_conversion = finite_float(physics.get("ConversionFactor_V2_rad2"))
    # Prefer post-processing metadata when present. This matters for tagged
    # dark-noise runs, where the pipeline may deliberately replace the value
    # recorded in sensitivity.log with a reference conversion factor.
    if np.isfinite(metadata_conversion):
        conversion = metadata_conversion

    correction = finite_float(physics.get("PowerDetectorAttenuatorCorrectionFactor"))
    applied_value = physics.get("PowerDetectorAttenuatorApplied", False)
    applied = applied_value if isinstance(applied_value, bool) else str(applied_value).lower() in {
        "1",
        "true",
        "yes",
        "on",
    }
    if not applied or not np.isfinite(correction) or correction <= 0:
        correction = 1.0

    display_unit = str(physics.get("DisplayAmplitudeUnit", "")).strip().lower()
    if display_unit in {"v2", "v^2", "v²"}:
        display_in_v2 = True
    elif display_unit in {"urad2", "urad^2", "μrad²", "µrad²"}:
        display_in_v2 = False
    else:
        finite_powers = [value for value in (p1, p2) if np.isfinite(value)]
        total_power = float(np.sum(finite_powers)) if finite_powers else 0.0
        display_in_v2 = (
            not np.isfinite(conversion)
            or conversion <= 0
            or total_power <= 0.05
            or (np.isfinite(shot_from_log) and shot_from_log > SHOT_NOISE_RESULT_V2_THRESHOLD)
        )

    if display_in_v2:
        shot_from_metadata = finite_float(physics.get("ShotNoiseResult_V2_rtHz"))
        shot_noise = shot_from_metadata if np.isfinite(shot_from_metadata) else shot_from_log
        source = "metadata.json:PhysicsData.ShotNoiseResult_V2_rtHz"
        if not np.isfinite(shot_from_metadata):
            source = "sensitivity.log:Shot Noise Result"
        scale = 1.0
        unit = r"V$^2$"
    else:
        if not np.isfinite(conversion) or conversion <= 0:
            raise ValueError("A positive Conversion Factor is required for urad^2 display units.")
        shot_from_metadata = finite_float(physics.get("ShotNoiseResult_urad2_rtHz"))
        shot_noise = shot_from_metadata if np.isfinite(shot_from_metadata) else shot_from_log
        source = "metadata.json:PhysicsData.ShotNoiseResult_urad2_rtHz"
        if not np.isfinite(shot_from_metadata):
            source = "sensitivity.log:Shot Noise Result"
        scale = 1e12 / conversion
        unit = r"$\mu rad^2$"

    if not np.isfinite(shot_noise) or shot_noise <= 0:
        raise ValueError(
            "No positive computed shot-noise result was found in metadata.json or sensitivity.log."
        )
    return DisplaySettings(scale, unit, shot_noise, source, correction)


def matrix_index(row: int, column: int) -> int:
    return (row - 1) * 8 + column - 1


def pair_label(pair: np.ndarray) -> str:
    row_1, column_1, row_2, column_2 = pair
    return f"({row_1},{column_1})-({row_2},{column_2})"


def critical_pair_data(
    raw_cm: np.ndarray,
    raw_to_v2: float,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    # Scaling every 64-channel frame first would require another full-size CM
    # array. Variance scales by the square of a constant, so apply that small
    # scalar after computing the raw channel variances instead.
    raw_channel_mse = np.mean(
        (raw_cm - np.mean(raw_cm, axis=0, keepdims=True)) ** 2,
        axis=0,
    )
    channel_mse = raw_channel_mse * raw_to_v2**2
    first = np.asarray([matrix_index(pair[0], pair[1]) for pair in PAIRS], dtype=int)
    second = np.asarray([matrix_index(pair[2], pair[3]) for pair in PAIRS], dtype=int)
    endpoint_1 = channel_mse[first]
    endpoint_2 = channel_mse[second]
    scores = np.mean(np.column_stack((endpoint_1, endpoint_2)), axis=1)
    ranked = np.argsort(np.nan_to_num(scores, nan=-np.inf))[::-1]
    ranked = ranked[np.isfinite(scores[ranked])]
    if ranked.size == 0:
        raise ValueError("No finite correlation-pair MSE values are available.")
    return ranked[: min(CRITICAL_PAIR_MAX_COUNT, ranked.size)], scores, endpoint_1, endpoint_2


def calculate_running_means(
    raw_cm: np.ndarray,
    critical_indices: np.ndarray,
    raw_to_display: float,
) -> np.ndarray:
    pairs = PAIRS[critical_indices]
    first = np.asarray([matrix_index(pair[0], pair[1]) for pair in pairs], dtype=int)
    second = np.asarray([matrix_index(pair[2], pair[3]) for pair in pairs], dtype=int)
    curves = raw_cm[:, first] - raw_cm[:, second]
    curves *= raw_to_display
    np.cumsum(curves, axis=0, out=curves)
    curves /= np.arange(1, raw_cm.shape[0] + 1, dtype=float)[:, None]
    np.abs(curves, out=curves)
    curves[~np.isfinite(curves) | (curves <= 0)] = np.nan
    return curves


def downward_numerical_outliers(times: np.ndarray, values: np.ndarray) -> np.ndarray:
    """Flag deep local defects using global and local log-log trends.

    A curved global trend is fitted robustly after 10 seconds, iteratively
    excluding only low residuals. After removing that global trend, the local
    reference is an upper quantile over a fixed fraction of a log-time decade.
    A drop more than ``NUMERICAL_DROP_FACTOR`` below both references anchors a
    candidate valley. Hysteresis then includes its depressed shoulders until
    recovery on both sides. Duration in seconds and number of points therefore
    do not determine validity: the relevant geometry is measured on the
    logarithmic time axis.
    """
    mask = np.zeros(values.size, dtype=bool)
    finite = (
        np.isfinite(values)
        & (values > 0)
        & np.isfinite(times)
        & (times >= OUTLIER_TREND_START_S)
    )
    if np.count_nonzero(finite) < 3:
        return mask

    finite_indices = np.flatnonzero(finite)
    log_times = np.log10(times[finite])
    log_values = np.log10(values[finite])
    bin_ids = np.floor(log_times * OUTLIER_LOG_BINS_PER_DECADE).astype(np.int64)
    boundaries = np.flatnonzero(np.r_[True, bin_ids[1:] != bin_ids[:-1], True])
    bin_log_times = np.empty(boundaries.size - 1, dtype=float)
    bin_log_values = np.empty(boundaries.size - 1, dtype=float)
    for start, stop in zip(boundaries[:-1], boundaries[1:], strict=False):
        bin_index = np.searchsorted(boundaries, start)
        bin_log_times[bin_index] = float(np.median(log_times[start:stop]))
        bin_log_values[bin_index] = float(np.median(log_values[start:stop]))

    if bin_log_times.size < 3:
        return mask

    polynomial_degree = min(OUTLIER_GLOBAL_POLYNOMIAL_DEGREE, bin_log_times.size - 1)
    fit_keep = np.ones(bin_log_times.size, dtype=bool)
    coefficients = np.polyfit(bin_log_times, bin_log_values, polynomial_degree)
    for _ in range(OUTLIER_GLOBAL_FIT_ITERATIONS):
        if np.count_nonzero(fit_keep) <= polynomial_degree:
            break
        coefficients = np.polyfit(
            bin_log_times[fit_keep],
            bin_log_values[fit_keep],
            polynomial_degree,
        )
        residuals = bin_log_values - np.polyval(coefficients, bin_log_times)
        kept_residuals = residuals[fit_keep]
        center = float(np.median(kept_residuals))
        mad = float(np.median(np.abs(kept_residuals - center)))
        robust_sigma = 1.4826 * mad
        if not np.isfinite(robust_sigma) or robust_sigma <= np.finfo(float).eps:
            break
        new_keep = residuals >= center - OUTLIER_GLOBAL_CLIP_SIGMA * robust_sigma
        if np.array_equal(new_keep, fit_keep):
            break
        fit_keep = new_keep

    window_bins = max(
        3,
        int(round(OUTLIER_LOCAL_WINDOW_DECADES * OUTLIER_LOG_BINS_PER_DECADE)),
    )
    if window_bins % 2 == 0:
        window_bins += 1
    half_window = window_bins // 2
    global_bin_trend = np.polyval(coefficients, bin_log_times)
    detrended_bin_values = bin_log_values - global_bin_trend
    local_residual_reference = np.empty_like(bin_log_values)
    for index in range(bin_log_values.size):
        start = max(0, index - half_window)
        stop = min(bin_log_values.size, index + half_window + 1)
        local_residual_reference[index] = float(
            np.quantile(detrended_bin_values[start:stop], OUTLIER_LOCAL_QUANTILE)
        )

    global_trend = np.polyval(coefficients, log_times)
    local_trend = global_trend + np.interp(
        log_times,
        bin_log_times,
        local_residual_reference,
    )
    # A deep point below both references anchors a possible defect valley.
    conservative_reference = np.minimum(global_trend, local_trend)
    log_drop = math.log10(NUMERICAL_DROP_FACTOR)
    point_cores = log_values < conservative_reference - log_drop

    # Expand each deep core to include its long valley shoulders. Expansion is
    # performed on log-time bins, not raw samples or seconds. The upper of the
    # robust global/local references is used as the recovery target.
    bin_core = np.zeros(bin_log_values.size, dtype=bool)
    for bin_index, (start, stop) in enumerate(
        zip(boundaries[:-1], boundaries[1:], strict=False)
    ):
        bin_core[bin_index] = bool(np.any(point_cores[start:stop]))

    local_bin_reference = global_bin_trend + local_residual_reference
    recovery_reference = np.maximum(global_bin_trend, local_bin_reference)
    recovery_log_ratio = math.log10(OUTLIER_VALLEY_RECOVERY_RATIO)
    depressed_bins = bin_log_values < recovery_reference + recovery_log_ratio

    # Close tiny gaps between depressed bins. The gap allowance is expressed
    # as a fraction of a log decade and therefore grows naturally in seconds.
    max_gap_bins = max(
        0,
        int(round(OUTLIER_VALLEY_MAX_GAP_DECADES * OUTLIER_LOG_BINS_PER_DECADE)),
    )
    closed_depressed = depressed_bins.copy()
    false_boundaries = np.flatnonzero(
        np.diff(np.r_[False, ~closed_depressed, False].astype(np.int8)) != 0
    )
    for start, stop in zip(false_boundaries[::2], false_boundaries[1::2], strict=False):
        bounded = start > 0 and stop < closed_depressed.size
        if bounded and stop - start <= max_gap_bins:
            closed_depressed[start:stop] = True

    eligible_bins = closed_depressed | bin_core
    component_boundaries = np.flatnonzero(
        np.diff(np.r_[False, eligible_bins, False].astype(np.int8)) != 0
    )
    defect_bins = np.zeros_like(eligible_bins)
    for start, stop in zip(
        component_boundaries[::2],
        component_boundaries[1::2],
        strict=False,
    ):
        # A numerical valley must contain a deep core and recover on both
        # sides. An interval reaching either edge is retained as a real trend.
        bounded = start > 0 and stop < eligible_bins.size
        if bounded and np.any(bin_core[start:stop]):
            defect_bins[start:stop] = True

    finite_defects = np.repeat(defect_bins, np.diff(boundaries))
    mask[finite_indices] = finite_defects
    return mask


def assess_convergence(
    times: np.ndarray,
    values: np.ndarray,
    shot_noise: float,
) -> ConvergenceResult:
    """Assess a stable late-time plateau after defects have been removed.

    ``values`` must already contain NaN at detected downward defects. The fit
    gives every occupied log-time bin one vote, which matches the visual
    geometry of the Allan plot and avoids using raw duration or point count as
    evidence of convergence.
    """
    finite = (
        np.isfinite(times)
        & np.isfinite(values)
        & (times > MIN_AVERAGING_TIME_S)
        & (values > 0)
    )
    if np.count_nonzero(finite) < CONVERGENCE_MIN_LOG_BINS:
        return ConvergenceResult()

    log_times = np.log10(times[finite])
    log_values = np.log10(values[finite])
    end_log_time = float(np.max(log_times))
    start_log_time = max(
        math.log10(MIN_AVERAGING_TIME_S),
        end_log_time - CONVERGENCE_WINDOW_DECADES,
    )
    in_window = log_times >= start_log_time
    log_times = log_times[in_window]
    log_values = log_values[in_window]
    if log_times.size < CONVERGENCE_MIN_LOG_BINS:
        return ConvergenceResult()

    bin_ids = np.floor(log_times * OUTLIER_LOG_BINS_PER_DECADE).astype(np.int64)
    boundaries = np.flatnonzero(np.r_[True, bin_ids[1:] != bin_ids[:-1], True])
    bin_count = boundaries.size - 1
    if bin_count < CONVERGENCE_MIN_LOG_BINS:
        return ConvergenceResult(
            start_time_s=float(10.0**np.min(log_times)),
            end_time_s=float(10.0**np.max(log_times)),
            span_decades=float(np.ptp(log_times)),
            log_bin_count=bin_count,
        )

    bin_log_times = np.empty(bin_count, dtype=float)
    bin_log_values = np.empty(bin_count, dtype=float)
    for bin_index, (start, stop) in enumerate(
        zip(boundaries[:-1], boundaries[1:], strict=False)
    ):
        bin_log_times[bin_index] = float(np.median(log_times[start:stop]))
        bin_log_values[bin_index] = float(np.median(log_values[start:stop]))

    keep = np.ones(bin_count, dtype=bool)
    coefficients = np.polyfit(bin_log_times, bin_log_values, 1)
    for _ in range(CONVERGENCE_FIT_ITERATIONS):
        if np.count_nonzero(keep) < CONVERGENCE_MIN_LOG_BINS:
            break
        coefficients = np.polyfit(bin_log_times[keep], bin_log_values[keep], 1)
        residuals = bin_log_values - np.polyval(coefficients, bin_log_times)
        center = float(np.median(residuals[keep]))
        mad = float(np.median(np.abs(residuals[keep] - center)))
        robust_sigma = 1.4826 * mad
        if not np.isfinite(robust_sigma) or robust_sigma <= np.finfo(float).eps:
            break
        new_keep = np.abs(residuals - center) <= CONVERGENCE_CLIP_SIGMA * robust_sigma
        if np.array_equal(new_keep, keep) or np.count_nonzero(new_keep) < CONVERGENCE_MIN_LOG_BINS:
            break
        keep = new_keep

    coefficients = np.polyfit(bin_log_times[keep], bin_log_values[keep], 1)
    kept_residuals = bin_log_values[keep] - np.polyval(coefficients, bin_log_times[keep])
    residual_center = float(np.median(kept_residuals))
    scatter_dex = float(1.4826 * np.median(np.abs(kept_residuals - residual_center)))
    span_decades = float(np.ptp(bin_log_times[keep]))
    slope = float(coefficients[0])
    level = float(10.0 ** np.median(bin_log_values[keep]))
    lower_quantile = float(
        10.0 ** np.quantile(bin_log_values[keep], CONVERGENCE_LOWER_QUANTILE)
    )
    converged = bool(
        np.count_nonzero(keep) >= CONVERGENCE_MIN_LOG_BINS
        and span_decades >= CONVERGENCE_MIN_SPAN_DECADES
        and abs(slope) <= CONVERGENCE_MAX_ABS_SLOPE
        and scatter_dex <= CONVERGENCE_MAX_SCATTER_DEX
    )
    return ConvergenceResult(
        start_time_s=float(10.0**np.min(bin_log_times[keep])),
        end_time_s=float(10.0**np.max(bin_log_times[keep])),
        span_decades=span_decades,
        log_bin_count=int(np.count_nonzero(keep)),
        slope=slope,
        scatter_dex=scatter_dex,
        level=level,
        lower_quantile=lower_quantile,
        converged=converged,
        passed=bool(converged and level > shot_noise),
    )


def screen_pairs(
    elapsed: np.ndarray,
    curves: np.ndarray,
    critical_indices: np.ndarray,
    scores: np.ndarray,
    endpoint_1: np.ndarray,
    endpoint_2: np.ndarray,
    shot_noise: float,
) -> list[PairScreening]:
    tail_mask = elapsed > MIN_AVERAGING_TIME_S
    if not np.any(tail_mask):
        raise ValueError(
            f"The run ends at {elapsed[-1]:.6f} s; no samples are strictly greater than "
            f"{MIN_AVERAGING_TIME_S:.0f} s."
        )

    results: list[PairScreening] = []
    tail_times = elapsed[tail_mask]
    for rank_zero_based, pair_index in enumerate(critical_indices):
        full_curve = curves[:, rank_zero_based]
        full_outliers = downward_numerical_outliers(elapsed, full_curve)
        tail = full_curve[tail_mask]
        finite = np.isfinite(tail)
        outliers = full_outliers[tail_mask]
        clean = finite & ~outliers
        clean_tail = tail[clean]
        clean_times = tail_times[clean]
        cleaned_full_curve = full_curve.copy()
        cleaned_full_curve[full_outliers] = np.nan
        convergence = assess_convergence(elapsed, cleaned_full_curve, shot_noise)
        if clean_tail.size == 0:
            clean_tail_min = tail_max = tail_median = tail_final = exceedance_fraction = math.nan
            first_exceedance = math.nan
        else:
            exceeds = clean_tail > shot_noise
            clean_tail_min = float(np.min(clean_tail))
            tail_max = float(np.max(clean_tail))
            tail_median = float(np.median(clean_tail))
            tail_final = float(clean_tail[-1])
            exceedance_fraction = float(np.mean(exceeds))
            strict_rule_passed = bool(np.all(exceeds))
            first_exceedance = (
                float(clean_times[np.flatnonzero(exceeds)[0]]) if np.any(exceeds) else math.nan
            )
        if clean_tail.size == 0:
            strict_rule_passed = False
        results.append(
            PairScreening(
                pair_index=int(pair_index),
                critical_rank=rank_zero_based + 1,
                mse_score=float(scores[pair_index]),
                endpoint_mse_1=float(endpoint_1[pair_index]),
                endpoint_mse_2=float(endpoint_2[pair_index]),
                clean_tail_min=clean_tail_min,
                tail_max=tail_max,
                tail_median=tail_median,
                tail_final=tail_final,
                exceedance_fraction=exceedance_fraction,
                first_exceedance_time_s=first_exceedance,
                strict_rule_passed=strict_rule_passed,
                convergence_start_time_s=convergence.start_time_s,
                convergence_end_time_s=convergence.end_time_s,
                convergence_span_decades=convergence.span_decades,
                convergence_log_bin_count=convergence.log_bin_count,
                convergence_slope=convergence.slope,
                convergence_scatter_dex=convergence.scatter_dex,
                converged_level=convergence.level,
                converged_level_lower_quantile=convergence.lower_quantile,
                convergence_rule_passed=convergence.passed,
                selection_rule="",
                clean_sample_count=int(np.count_nonzero(clean)),
                downward_outlier_count=int(np.count_nonzero(outliers)),
                downward_outlier_fraction=float(np.mean(outliers[finite])) if np.any(finite) else math.nan,
                selected=False,
            )
        )
    return results


def apply_selection_rules(
    screening: list[PairScreening],
) -> tuple[list[PairScreening], list[int], str | None]:
    """Apply Rule 1 first, then Rule 2 only when Rule 1 finds no signal."""
    strict_columns = [
        column for column, result in enumerate(screening) if result.strict_rule_passed
    ]
    if strict_columns:
        selected_set = set(strict_columns)
        updated = [
            replace(
                result,
                selected=column in selected_set,
                selection_rule=("rule_1_strict_all_clean_points" if column in selected_set else ""),
            )
            for column, result in enumerate(screening)
        ]
        return updated, strict_columns, "Rule 1: all cleaned points above shot noise"

    if ENABLE_CONVERGENCE_FALLBACK:
        convergence_columns = [
            column
            for column, result in enumerate(screening)
            if result.convergence_rule_passed
        ]
        if convergence_columns:
            selected_set = set(convergence_columns)
            updated = [
                replace(
                    result,
                    selected=column in selected_set,
                    selection_rule=(
                        "rule_2_converged_plateau_fallback" if column in selected_set else ""
                    ),
                )
                for column, result in enumerate(screening)
            ]
            return updated, convergence_columns, "Rule 2: converged plateau above shot noise"

    return screening, [], None


def mask_selected_tail_outliers(
    elapsed: np.ndarray,
    curves: np.ndarray,
    selected_columns: list[int],
) -> None:
    """Replace screened downward defects with NaN in the plotted curves."""
    for column in selected_columns:
        outliers = downward_numerical_outliers(elapsed, curves[:, column])
        curves[outliers, column] = np.nan


def provenance_text(run_folder: Path, shot_noise: float, source: str) -> str:
    cm_path = run_folder / "cm.bin"
    stat = cm_path.stat()
    modified = datetime.fromtimestamp(stat.st_mtime).isoformat(timespec="seconds")
    return (
        f"Run folder: {run_folder.resolve()} | Raw: cm.bin | bytes={stat.st_size} | "
        f"modified={modified} | Shot noise={shot_noise:.6g} from {source}"
    )


def save_figure(
    fig: plt.Figure,
    path: Path,
    run_folder: Path,
    shot_noise: float,
    source: str,
) -> None:
    provenance = provenance_text(run_folder, shot_noise, source)
    fig.subplots_adjust(bottom=max(fig.subplotpars.bottom, 0.09))
    fig.text(0.005, 0.006, provenance, ha="left", va="bottom", fontsize=6, color="#555555", wrap=True)
    fig.savefig(
        path,
        bbox_inches="tight",
        metadata={
            "SourceRunFolder": str(run_folder.resolve()),
            "SourceRawFile": str((run_folder / "cm.bin").resolve()),
            "Provenance": provenance,
            "VisibleRunFolderFootnote": "lower-left",
        },
    )


def downsample(x_values: np.ndarray, y_values: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    if x_values.size <= MAX_LINE_PLOT_POINTS:
        return x_values, y_values
    step = int(math.ceil(x_values.size / MAX_LINE_PLOT_POINTS))
    return x_values[::step], y_values[::step]


def write_screening_csv(
    run_folder: Path,
    screening: list[PairScreening],
    shot_noise: float,
    unit_label: str,
    allan_source: str,
) -> Path:
    path = output_path(run_folder, "critical_pairs_summary.csv")
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(
            [
                "pair_index",
                "label",
                "critical_rank",
                "selected",
                "selection_rule",
                "strict_rule_passed",
                "convergence_fallback_enabled",
                "convergence_rule_passed",
                "allan_source",
                "mse_corr_pair_score",
                "mse_corr_channel_1",
                "mse_corr_channel_2",
                "comparison_time_strictly_greater_than_s",
                "shot_noise_level",
                "display_unit",
                "clean_tail_min",
                "clean_tail_max",
                "clean_tail_median",
                "clean_tail_final",
                "clean_tail_exceedance_fraction",
                "first_exceedance_time_s",
                "convergence_start_time_s",
                "convergence_end_time_s",
                "convergence_span_decades",
                "convergence_log_bin_count",
                "convergence_slope_log_value_per_log_time",
                "convergence_scatter_dex",
                "converged_level",
                "converged_level_lower_quantile",
                "convergence_window_decades",
                "convergence_min_span_decades",
                "convergence_min_log_bins",
                "convergence_max_abs_slope",
                "convergence_max_scatter_dex",
                "convergence_lower_quantile",
                "clean_sample_count",
                "downward_outlier_count",
                "downward_outlier_fraction",
                "outlier_trend_start_s",
                "outlier_log_bins_per_decade",
                "outlier_local_window_decades",
                "outlier_local_quantile",
                "outlier_global_polynomial_degree",
                "outlier_global_clip_sigma",
                "numerical_drop_factor",
                "outlier_valley_recovery_ratio",
                "outlier_valley_max_gap_decades",
            ]
        )
        for item in screening:
            writer.writerow(
                [
                    item.pair_index,
                    pair_label(PAIRS[item.pair_index]),
                    item.critical_rank,
                    item.selected,
                    item.selection_rule,
                    item.strict_rule_passed,
                    ENABLE_CONVERGENCE_FALLBACK,
                    item.convergence_rule_passed,
                    allan_source,
                    f"{item.mse_score:.6e}",
                    f"{item.endpoint_mse_1:.6e}",
                    f"{item.endpoint_mse_2:.6e}",
                    f"{MIN_AVERAGING_TIME_S:.6f}",
                    f"{shot_noise:.12g}",
                    unit_label.replace("$", "").replace("\\", ""),
                    f"{item.clean_tail_min:.12g}",
                    f"{item.tail_max:.12g}",
                    f"{item.tail_median:.12g}",
                    f"{item.tail_final:.12g}",
                    f"{item.exceedance_fraction:.12g}",
                    f"{item.first_exceedance_time_s:.12g}",
                    f"{item.convergence_start_time_s:.12g}",
                    f"{item.convergence_end_time_s:.12g}",
                    f"{item.convergence_span_decades:.12g}",
                    item.convergence_log_bin_count,
                    f"{item.convergence_slope:.12g}",
                    f"{item.convergence_scatter_dex:.12g}",
                    f"{item.converged_level:.12g}",
                    f"{item.converged_level_lower_quantile:.12g}",
                    f"{CONVERGENCE_WINDOW_DECADES:.6f}",
                    f"{CONVERGENCE_MIN_SPAN_DECADES:.6f}",
                    CONVERGENCE_MIN_LOG_BINS,
                    f"{CONVERGENCE_MAX_ABS_SLOPE:.6f}",
                    f"{CONVERGENCE_MAX_SCATTER_DEX:.6f}",
                    f"{CONVERGENCE_LOWER_QUANTILE:.6f}",
                    item.clean_sample_count,
                    item.downward_outlier_count,
                    f"{item.downward_outlier_fraction:.12g}",
                    f"{OUTLIER_TREND_START_S:.6f}",
                    OUTLIER_LOG_BINS_PER_DECADE,
                    f"{OUTLIER_LOCAL_WINDOW_DECADES:.6f}",
                    f"{OUTLIER_LOCAL_QUANTILE:.6f}",
                    OUTLIER_GLOBAL_POLYNOMIAL_DEGREE,
                    f"{OUTLIER_GLOBAL_CLIP_SIGMA:.6f}",
                    f"{NUMERICAL_DROP_FACTOR:.6f}",
                    f"{OUTLIER_VALLEY_RECOVERY_RATIO:.6f}",
                    f"{OUTLIER_VALLEY_MAX_GAP_DECADES:.6f}",
                ]
            )
    return path


def save_selected_loglog(
    run_folder: Path,
    elapsed: np.ndarray,
    curves: np.ndarray,
    critical_indices: np.ndarray,
    selected_columns: list[int],
    settings: DisplaySettings,
    active_rule: str,
) -> Path:
    fig, axis = plt.subplots(figsize=(9, 6.5))
    x_values = np.maximum(elapsed, FRAME_DT_S)
    for column in selected_columns:
        pair_index = int(critical_indices[column])
        plot_x, plot_y = downsample(x_values, curves[:, column])
        axis.loglog(plot_x, plot_y, linewidth=1.5, label=pair_label(PAIRS[pair_index]))
    axis.axhline(
        settings.shot_noise,
        color="#c0392b",
        linestyle="--",
        linewidth=1.6,
        label=f"Computed shot noise: {settings.shot_noise:.4g}",
    )
    axis.axvline(MIN_AVERAGING_TIME_S, color="#555555", linestyle=":", linewidth=1.4, label="1000 s cutoff")
    axis.set_xscale("log")
    axis.set_yscale("log")
    axis.set_title(f"Log-Log Evaluation (Signal-Containing Critical Pairs)\n{active_rule}")
    axis.set_xlabel("Time (s)")
    axis.set_ylabel(f"|Running Mean| ({settings.unit_label})")
    if not selected_columns:
        axis.text(
            0.5,
            0.5,
            "No critical pair exceeded shot noise after 1000 s",
            transform=axis.transAxes,
            ha="center",
            va="center",
            bbox=dict(facecolor="white", edgecolor="black"),
        )
    axis.legend(loc="center left", bbox_to_anchor=(1.01, 0.5), frameon=False)
    fig.tight_layout()
    path = output_path(run_folder, "loglog_eval.png")
    save_figure(fig, path, run_folder, settings.shot_noise, settings.shot_noise_source)
    plt.close(fig)
    return path


def find_position_log(run_folder: Path) -> Path | None:
    for name in ("delay_stage_positions.log", "delay_stage_positions.csv"):
        path = run_folder / name
        if path.is_file():
            return path
    return None


def positions_at_frames(
    run_folder: Path,
    elapsed: np.ndarray,
    absolute_frame_times: np.ndarray,
) -> np.ndarray:
    path = find_position_log(run_folder)
    if path is None:
        warnings.warn("No delay-stage position log found; using 25.058 mm for every frame.")
        return np.full(elapsed.size, 25.058, dtype=float)

    log_times: list[float] = []
    log_positions: list[float] = []
    for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
        fields = [part.strip() for part in re.split(r"[,;]", line) if part.strip()]
        if len(fields) < 2:
            continue
        try:
            log_times.append(parse_clock_seconds(fields[0]))
            log_positions.append(float(fields[-1]))
        except ValueError:
            continue
    if not log_times:
        raise ValueError(f"No delay-stage positions could be parsed from {path}")

    position_times = unwrap_clock_times(np.asarray(log_times, dtype=float))
    position_times += round((absolute_frame_times[0] - position_times[0]) / 86_400.0) * 86_400.0
    relative_times = position_times - absolute_frame_times[0]
    unique_times, unique_indices = np.unique(relative_times, return_index=True)
    unique_positions = np.asarray(log_positions, dtype=float)[unique_indices]
    return np.interp(
        elapsed,
        unique_times,
        unique_positions,
        left=unique_positions[0],
        right=unique_positions[-1],
    )


def build_selected_bin_cumsums(
    raw_cm: np.ndarray,
    positions: np.ndarray,
    selected_pair_indices: np.ndarray,
    raw_to_display: float,
) -> tuple[np.ndarray, np.ndarray, list[np.ndarray]]:
    rounded = np.round(positions / BIN_MM) * BIN_MM
    bins, inverse = np.unique(rounded, return_inverse=True)
    if selected_pair_indices.size:
        pairs = PAIRS[selected_pair_indices]
        first = np.asarray([matrix_index(pair[0], pair[1]) for pair in pairs], dtype=int)
        second = np.asarray([matrix_index(pair[2], pair[3]) for pair in pairs], dtype=int)
        differences = raw_cm[:, first] - raw_cm[:, second]
        differences *= raw_to_display
    else:
        differences = np.empty((raw_cm.shape[0], 0), dtype=float)

    cumulative: list[np.ndarray] = []
    counts = np.zeros(bins.size, dtype=int)
    for bin_index in range(bins.size):
        members = differences[inverse == bin_index]
        counts[bin_index] = members.shape[0]
        cumulative.append(np.cumsum(members, axis=0))
    return bins * MM_TO_PS, counts, cumulative


def final_amplitudes(counts: np.ndarray, cumulative: list[np.ndarray]) -> tuple[int, np.ndarray]:
    positive = counts[counts > 0]
    if positive.size == 0:
        raise ValueError("No populated delay-position bins were found.")
    common_frames = int(np.min(positive))
    pair_count = cumulative[0].shape[1] if cumulative else 0
    amplitudes = np.full((len(cumulative), pair_count), np.nan, dtype=float)
    for index, values in enumerate(cumulative):
        if values.shape[0] >= common_frames:
            amplitudes[index] = values[common_frames - 1] / common_frames
    return common_frames, amplitudes


def write_final_amplitudes(
    run_folder: Path,
    delays: np.ndarray,
    amplitudes: np.ndarray,
    selected_pair_indices: np.ndarray,
) -> Path:
    path = output_path(run_folder, "final_amplitudes.csv")
    labels = [pair_label(PAIRS[index]) for index in selected_pair_indices]
    headers = [label.replace("(", "").replace(")", "").replace("-", "_").replace(",", "_") for label in labels]
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["Delay_ps", *headers])
        for delay, row in zip(delays, amplitudes, strict=False):
            writer.writerow([f"{delay:.6f}", *row])
    return path


def save_final_clean_plot(
    run_folder: Path,
    delays: np.ndarray,
    amplitudes: np.ndarray,
    selected_pair_indices: np.ndarray,
    common_frames: int,
    total_frames: int,
    settings: DisplaySettings,
    active_rule: str,
) -> Path:
    fig, axis = plt.subplots(figsize=(11, 7))
    for column, pair_index in enumerate(selected_pair_indices):
        axis.plot(
            delays,
            amplitudes[:, column],
            "o-",
            linewidth=1.8,
            markersize=4,
            label=pair_label(PAIRS[pair_index]),
        )
    axis.set_xlabel("Delay (ps)")
    axis.set_ylabel(f"Amplitude ({settings.unit_label})")
    axis.set_title(
        f"Signal-Containing Critical Pairs Summary — {active_rule}\n"
        f"Integration: {common_frames} Frames ({common_frames * FRAME_DT_S:.2f}s) | "
        f"Total processed: {total_frames} Frames ({total_frames * FRAME_DT_S:.2f}s)"
    )
    if selected_pair_indices.size:
        axis.legend(loc="center left", bbox_to_anchor=(1.01, 0.5), frameon=False)
    else:
        axis.text(
            0.5,
            0.5,
            "No critical pair exceeded shot noise after 1000 s",
            transform=axis.transAxes,
            ha="center",
            va="center",
            bbox=dict(facecolor="white", edgecolor="black"),
        )
    fig.tight_layout()
    path = output_path(run_folder, "final_clean_result.png")
    save_figure(fig, path, run_folder, settings.shot_noise, settings.shot_noise_source)
    plt.close(fig)
    return path


def save_signal_emergence_movie(
    run_folder: Path,
    delays: np.ndarray,
    counts: np.ndarray,
    cumulative: list[np.ndarray],
    selected_pair_indices: np.ndarray,
    settings: DisplaySettings,
    active_rule: str,
) -> Path | None:
    if imageio is None:
        warnings.warn("imageio is not installed; the signal-emergence MP4 was skipped.")
        return None
    if selected_pair_indices.size == 0:
        warnings.warn("No pair passed the shot-noise screen; the signal-emergence MP4 was skipped.")
        return None

    common_frames = int(np.min(counts[counts > 0]))
    if common_frames < 2:
        warnings.warn("Fewer than two frames are present in a delay bin; the MP4 was skipped.")
        return None

    steps = np.unique(np.round(np.linspace(1, common_frames, min(400, common_frames))).astype(int))
    path = output_path(run_folder, "signal_emergence.mp4")
    fig, axis = plt.subplots(figsize=(12, 7))
    fig.text(
        0.005,
        0.006,
        provenance_text(run_folder, settings.shot_noise, settings.shot_noise_source),
        ha="left",
        va="bottom",
        fontsize=6,
        color="#555555",
        wrap=True,
    )
    lines = [
        axis.plot([], [], "o-", linewidth=1.5, markersize=4, label=pair_label(PAIRS[index]))[0]
        for index in selected_pair_indices
    ]
    axis.set_xlabel("Delay (ps)")
    axis.set_ylabel(f"Amplitude ({settings.unit_label})")
    finite_delays = delays[np.isfinite(delays)]
    if finite_delays.size:
        axis.set_xlim(float(np.min(finite_delays)), float(np.max(finite_delays)))
    axis.legend(loc="center left", bbox_to_anchor=(1.01, 0.5), frameon=False)

    with imageio.get_writer(path, fps=15, macro_block_size=1) as writer:
        for frame_count in steps:
            current = np.full((delays.size, selected_pair_indices.size), np.nan, dtype=float)
            for bin_index, values in enumerate(cumulative):
                if values.shape[0] >= frame_count:
                    current[bin_index] = values[frame_count - 1] / frame_count
            for column, line in enumerate(lines):
                line.set_data(delays, current[:, column])
            finite_values = current[np.isfinite(current)]
            if finite_values.size:
                low, high = float(np.min(finite_values)), float(np.max(finite_values))
                span = high - low
                pad = max(0.05 * span, 0.05 * max(abs(low), abs(high), 1.0), 1e-6)
                axis.set_ylim(low - pad, high + pad)
            axis.set_title(
                f"{active_rule}\n"
                f"Integration: {frame_count} Frames ({frame_count * FRAME_DT_S:.2f}s)"
            )
            fig.tight_layout(rect=(0, 0.055, 1, 1))
            fig.canvas.draw()
            writer.append_data(np.asarray(fig.canvas.buffer_rgba())[:, :, :3].copy())
    plt.close(fig)
    return path


def analyze_run(run_folder_string: str) -> dict[str, object]:
    run_folder = validate_run_folder(run_folder_string)
    style_matplotlib()
    raw_cm = read_cm(run_folder)
    elapsed, absolute_times = read_frame_times(run_folder, raw_cm.shape[0])
    settings = read_display_settings(run_folder)
    midnight_crossing = profile_has_midnight_crossing(run_folder)
    refined_indices, refined_plot_path, allan_source = prepare_allan_source(
        run_folder,
        raw_cm,
        midnight_crossing,
    )

    raw_to_v2 = DETECTOR_AREA_SCALE * settings.attenuator_correction
    raw_to_display = raw_to_v2 * settings.scale_from_scaled_v2
    critical_indices, scores, endpoint_1, endpoint_2 = critical_pair_data(raw_cm, raw_to_v2)
    if refined_indices is not None:
        critical_indices = refined_indices
    curves = calculate_running_means(raw_cm, critical_indices, raw_to_display)
    screening = screen_pairs(
        elapsed,
        curves,
        critical_indices,
        scores,
        endpoint_1,
        endpoint_2,
        settings.shot_noise,
    )
    screening, selected_columns, active_rule = apply_selection_rules(screening)
    selected_pair_indices = critical_indices[np.asarray(selected_columns, dtype=int)]

    if selected_pair_indices.size == 0:
        print(f"Analyzed {raw_cm.shape[0]:,} correlation-matrix frames.")
        print(f"Computed shot noise: {settings.shot_noise:.12g} ({settings.unit_label})")
        print(f"Allan source: {allan_source}")
        print("Run signal decision: NO SIGNAL")
        print(
            "Rule 1 found no pair with every cleaned point above shot noise; "
            + (
                "Rule 2 found no pair converging to a stable level above shot noise."
                if ENABLE_CONVERGENCE_FALLBACK
                else "the optional convergence fallback was disabled."
            )
        )
        print("Skipped all shot_noise_exceeding_* output generation.")
        return {
            "signal_found": False,
            "selection_rule": None,
            "screening_csv": None,
            "refined_allan_plot": refined_plot_path,
            "loglog_plot": None,
            "final_amplitudes_csv": None,
            "final_clean_plot": None,
            "signal_emergence_movie": None,
        }

    mask_selected_tail_outliers(elapsed, curves, selected_columns)
    summary_path = write_screening_csv(
        run_folder,
        screening,
        settings.shot_noise,
        settings.unit_label,
        allan_source,
    )
    loglog_path = save_selected_loglog(
        run_folder,
        elapsed,
        curves,
        critical_indices,
        selected_columns,
        settings,
        active_rule,
    )
    del curves

    positions = positions_at_frames(run_folder, elapsed, absolute_times)
    delays, counts, cumulative = build_selected_bin_cumsums(
        raw_cm,
        positions,
        selected_pair_indices,
        raw_to_display,
    )
    common_frames, amplitudes = final_amplitudes(counts, cumulative)
    amplitudes_path = write_final_amplitudes(
        run_folder,
        delays,
        amplitudes,
        selected_pair_indices,
    )
    final_plot_path = save_final_clean_plot(
        run_folder,
        delays,
        amplitudes,
        selected_pair_indices,
        common_frames,
        raw_cm.shape[0],
        settings,
        active_rule,
    )
    movie_path = save_signal_emergence_movie(
        run_folder,
        delays,
        counts,
        cumulative,
        selected_pair_indices,
        settings,
        active_rule,
    )

    print(f"Analyzed {raw_cm.shape[0]:,} correlation-matrix frames.")
    print(f"Computed shot noise: {settings.shot_noise:.12g} ({settings.unit_label})")
    print(f"Shot-noise source: {settings.shot_noise_source}")
    print(f"Allan source: {allan_source}")
    print(f"Critical pairs screened: {len(screening)}")
    print("Run signal decision: SIGNAL FOUND")
    print(f"Selection rule used: {active_rule}")
    print(f"Pairs selected after {MIN_AVERAGING_TIME_S:.0f} s: {selected_pair_indices.size}")
    for pair_index in selected_pair_indices:
        print(f"  {pair_label(PAIRS[pair_index])}")
    print(f"All generated files were written to: {run_folder}")

    return {
        "signal_found": True,
        "selection_rule": active_rule,
        "screening_csv": summary_path,
        "refined_allan_plot": refined_plot_path,
        "loglog_plot": loglog_path,
        "final_amplitudes_csv": amplitudes_path,
        "final_clean_plot": final_plot_path,
        "signal_emergence_movie": movie_path,
    }


def main() -> None:
    folder_string = sys.argv[1] if len(sys.argv) > 1 else RUN_FOLDER
    analyze_run(folder_string)


if __name__ == "__main__":
    main()
