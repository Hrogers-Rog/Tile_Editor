"""Vertical alignment helpers for track grade design.

Railroad grade transitions are modeled as parabolic vertical curves. Grade
changes linearly through each transition, which makes elevation and tangent
pitch continuous at the entry and exit.
"""

from __future__ import annotations

import math
from typing import Iterable


def _finite_float(value, label: str, errors: list[str]) -> float:
    try:
        result = float(value)
    except (TypeError, ValueError):
        errors.append(f"{label} must be a number")
        return 0.0
    if not math.isfinite(result):
        errors.append(f"{label} must be finite")
        return 0.0
    return result


def build_vertical_alignment(
        stations_m: Iterable[float],
        start_y: float,
        start_grade_pct: float,
        target_grade_pct: float,
        end_grade_pct: float,
        transition_in_m: float,
        transition_out_m: float) -> dict:
    """Evaluate a grade/vertical-curve profile at the requested stations.

    The profile begins at ``start_y`` and ``start_grade_pct``. Grade changes
    linearly to ``target_grade_pct`` over ``transition_in_m``, remains constant,
    then changes linearly to ``end_grade_pct`` over ``transition_out_m``.

    Returned point grades are tangent grades at each station, not average
    grades between samples.
    """
    errors: list[str] = []
    warnings: list[str] = []
    raw_stations = [
        _finite_float(value, "station", errors)
        for value in stations_m
    ]
    start_y = _finite_float(start_y, "start elevation", errors)
    start_grade_pct = _finite_float(
        start_grade_pct, "start grade", errors
    )
    target_grade_pct = _finite_float(
        target_grade_pct, "target grade", errors
    )
    end_grade_pct = _finite_float(end_grade_pct, "end grade", errors)
    transition_in_m = _finite_float(
        transition_in_m, "entry transition", errors
    )
    transition_out_m = _finite_float(
        transition_out_m, "exit transition", errors
    )

    if len(raw_stations) < 2:
        errors.append("vertical alignment needs at least two stations")
    if transition_in_m < 0.0:
        errors.append("entry transition cannot be negative")
    if transition_out_m < 0.0:
        errors.append("exit transition cannot be negative")
    if errors:
        return {'points': [], 'errors': errors, 'warnings': warnings}

    station_origin = raw_stations[0]
    stations = [value - station_origin for value in raw_stations]
    for left, right in zip(stations, stations[1:]):
        if right <= left:
            errors.append("stations must increase from start to end")
            break
    total_length = stations[-1]
    if total_length <= 0.0:
        errors.append("vertical alignment length must be greater than zero")
    if transition_in_m + transition_out_m > total_length + 1e-6:
        errors.append(
            "entry and exit transitions are longer than the selected chain"
        )
    if errors:
        return {
            'points': [],
            'errors': errors,
            'warnings': warnings,
            'total_length_m': total_length,
        }

    g0 = start_grade_pct / 100.0
    gt = target_grade_pct / 100.0
    g1 = end_grade_pct / 100.0
    exit_start = total_length - transition_out_m

    if transition_in_m == 0.0 and abs(gt - g0) > 1e-9:
        warnings.append("entry grade changes abruptly because its transition is 0 m")
    if transition_out_m == 0.0 and abs(g1 - gt) > 1e-9:
        warnings.append("exit grade changes abruptly because its transition is 0 m")

    if transition_in_m > 0.0:
        entry_end_y = (
            start_y
            + g0 * transition_in_m
            + (gt - g0) * transition_in_m * 0.5
        )
    else:
        entry_end_y = start_y
    exit_start_y = entry_end_y + gt * (exit_start - transition_in_m)

    def evaluate(station: float) -> tuple[float, float]:
        if transition_in_m == 0.0 and station <= 0.0:
            return start_y, g0
        if transition_in_m > 0.0 and station < transition_in_m:
            ratio = station / transition_in_m
            grade = g0 + (gt - g0) * ratio
            elevation = (
                start_y
                + g0 * station
                + (gt - g0) * station * station
                / (2.0 * transition_in_m)
            )
            return elevation, grade

        if transition_out_m == 0.0 and station >= total_length:
            elevation = entry_end_y + gt * (station - transition_in_m)
            return elevation, g1
        if transition_out_m > 0.0 and station > exit_start:
            local = station - exit_start
            ratio = local / transition_out_m
            grade = gt + (g1 - gt) * ratio
            elevation = (
                exit_start_y
                + gt * local
                + (g1 - gt) * local * local
                / (2.0 * transition_out_m)
            )
            return elevation, grade

        elevation = entry_end_y + gt * (station - transition_in_m)
        return elevation, gt

    points = []
    for original_station, station in zip(raw_stations, stations):
        elevation, grade = evaluate(station)
        points.append({
            'station_m': original_station,
            'relative_station_m': station,
            'y': elevation,
            'grade_pct': grade * 100.0,
        })

    entry_delta = abs(target_grade_pct - start_grade_pct)
    exit_delta = abs(end_grade_pct - target_grade_pct)
    return {
        'points': points,
        'errors': errors,
        'warnings': warnings,
        'total_length_m': total_length,
        'start_y': start_y,
        'end_y': points[-1]['y'],
        'rise_m': points[-1]['y'] - start_y,
        'transition_in_end_m': transition_in_m,
        'transition_out_start_m': exit_start,
        'entry_k_m_per_pct': (
            transition_in_m / entry_delta if entry_delta > 1e-9 else None
        ),
        'exit_k_m_per_pct': (
            transition_out_m / exit_delta if exit_delta > 1e-9 else None
        ),
    }


def dense_vertical_alignment_stations(
        total_length_m: float,
        transition_in_m: float,
        transition_out_m: float,
        sample_count: int = 121) -> list[float]:
    """Return plot stations including both vertical-curve boundaries."""
    total = max(0.0, float(total_length_m))
    count = max(2, int(sample_count))
    stations = {
        total * index / (count - 1)
        for index in range(count)
    }
    stations.add(0.0)
    stations.add(total)
    stations.add(max(0.0, min(total, float(transition_in_m))))
    stations.add(
        max(0.0, min(total, total - float(transition_out_m)))
    )
    return sorted(stations)
