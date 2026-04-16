"""Alignment helpers for draft guide paths, deviation checks, and arc fitting."""

from __future__ import annotations

import math


def polyline_length(points: list[tuple[float, float]]) -> float:
    """Return the total X/Z length of a polyline."""
    if len(points) < 2:
        return 0.0
    total = 0.0
    px, pz = points[0]
    for x1, z1 in points[1:]:
        total += math.hypot(x1 - px, z1 - pz)
        px, pz = x1, z1
    return total


def cumulative_lengths(points: list[tuple[float, float]]) -> list[float]:
    """Return cumulative X/Z lengths for each point in a polyline."""
    if not points:
        return []
    lengths = [0.0]
    total = 0.0
    px, pz = points[0]
    for x1, z1 in points[1:]:
        total += math.hypot(x1 - px, z1 - pz)
        lengths.append(total)
        px, pz = x1, z1
    return lengths


def project_point_to_segment(
    point: tuple[float, float],
    start: tuple[float, float],
    end: tuple[float, float],
) -> dict:
    """Project a point onto a line segment in X/Z."""
    px, pz = point
    ax, az = start
    bx, bz = end
    dx = bx - ax
    dz = bz - az
    seg_len2 = dx * dx + dz * dz
    if seg_len2 <= 1e-9:
        dist = math.hypot(px - ax, pz - az)
        return {"point": (ax, az), "distance": dist, "t": 0.0}
    t = ((px - ax) * dx + (pz - az) * dz) / seg_len2
    t = max(0.0, min(1.0, t))
    qx = ax + dx * t
    qz = az + dz * t
    dist = math.hypot(px - qx, pz - qz)
    return {"point": (qx, qz), "distance": dist, "t": t}


def project_point_to_polyline(
    point: tuple[float, float],
    points: list[tuple[float, float]],
) -> dict | None:
    """Return the closest point on a polyline to the given point."""
    if not points:
        return None
    if len(points) == 1:
        x0, z0 = points[0]
        return {
            "point": (x0, z0),
            "distance": math.hypot(point[0] - x0, point[1] - z0),
            "segment_index": 0,
            "t": 0.0,
        }
    best = None
    for idx in range(len(points) - 1):
        sample = project_point_to_segment(point, points[idx], points[idx + 1])
        if best is None or sample["distance"] < best["distance"]:
            best = {
                "point": sample["point"],
                "distance": sample["distance"],
                "segment_index": idx,
                "t": sample["t"],
            }
    return best


def deviation_samples(
    sample_points: list[tuple[float, float]],
    target_polyline: list[tuple[float, float]],
) -> dict:
    """Measure deviations from sample points to a target polyline."""
    samples = []
    if not sample_points or not target_polyline:
        return {"samples": samples, "max_distance": None, "rms_distance": None}

    sq_total = 0.0
    max_dist = 0.0
    for point in sample_points:
        hit = project_point_to_polyline(point, target_polyline)
        if not hit:
            continue
        dist = float(hit["distance"])
        sq_total += dist * dist
        max_dist = max(max_dist, dist)
        samples.append(
            {
                "from_point": point,
                "to_point": hit["point"],
                "distance": dist,
                "segment_index": hit["segment_index"],
            }
        )

    if not samples:
        return {"samples": [], "max_distance": None, "rms_distance": None}
    rms = math.sqrt(sq_total / len(samples))
    return {"samples": samples, "max_distance": max_dist, "rms_distance": rms}


def signed_turn(points: list[tuple[float, float]]) -> int:
    """Return +1 for mostly left-turning, -1 for mostly right-turning, 0 if flat."""
    if len(points) < 3:
        return 0
    total = 0.0
    for idx in range(1, len(points) - 1):
        ax, az = points[idx - 1]
        bx, bz = points[idx]
        cx, cz = points[idx + 1]
        abx = bx - ax
        abz = bz - az
        bcx = cx - bx
        bcz = cz - bz
        total += abx * bcz - abz * bcx
    if abs(total) < 1e-9:
        return 0
    return 1 if total > 0.0 else -1


def fit_circle(points: list[tuple[float, float]]) -> dict | None:
    """Fit a circle in X/Z using a simple algebraic least-squares fit."""
    if len(points) < 3:
        return None

    n = float(len(points))
    mean_x = sum(x for x, _ in points) / n
    mean_z = sum(z for _, z in points) / n
    shifted = [(x - mean_x, z - mean_z) for x, z in points]

    suu = sum(u * u for u, _ in shifted)
    svv = sum(v * v for _, v in shifted)
    suv = sum(u * v for u, v in shifted)
    suuu = sum(u * u * u for u, _ in shifted)
    svvv = sum(v * v * v for _, v in shifted)
    suvv = sum(u * v * v for u, v in shifted)
    svuu = sum(v * u * u for u, v in shifted)

    det = suu * svv - suv * suv
    if abs(det) < 1e-9:
        return None

    rhs_u = 0.5 * (suuu + suvv)
    rhs_v = 0.5 * (svvv + svuu)
    uc = (rhs_u * svv - rhs_v * suv) / det
    vc = (rhs_v * suu - rhs_u * suv) / det

    center = (mean_x + uc, mean_z + vc)
    radii = [math.hypot(x - center[0], z - center[1]) for x, z in points]
    radius = sum(radii) / n
    rms = math.sqrt(sum((r - radius) ** 2 for r in radii) / n)

    return {
        "center": center,
        "radius": radius,
        "rms_error": rms,
        "turn_sign": signed_turn(points),
    }


def unwrap_arc_angles(
    points: list[tuple[float, float]],
    center: tuple[float, float],
    turn_sign: int,
) -> list[float]:
    """Unwrap point angles around a center to follow the chain direction."""
    if not points:
        return []
    cx, cz = center
    angles = [math.atan2(points[0][1] - cz, points[0][0] - cx)]
    for x, z in points[1:]:
        angle = math.atan2(z - cz, x - cx)
        prev = angles[-1]
        while angle - prev > math.pi:
            angle -= math.tau
        while angle - prev < -math.pi:
            angle += math.tau
        if turn_sign >= 0 and angle < prev:
            angle += math.tau
        elif turn_sign < 0 and angle > prev:
            angle -= math.tau
        angles.append(angle)
    return angles


def fit_arc_to_chain(points: list[tuple[float, float]]) -> dict | None:
    """Fit a constant-radius arc to a chain of X/Z points."""
    if len(points) < 3:
        return None
    circle = fit_circle(points)
    if not circle or circle["radius"] <= 0.01:
        return None

    center = circle["center"]
    turn_sign = circle["turn_sign"] or 1
    angles = unwrap_arc_angles(points, center, turn_sign)
    cum_lengths = cumulative_lengths(points)
    total_length = cum_lengths[-1] if cum_lengths else 0.0
    if total_length <= 0.01:
        return None

    start_angle = angles[0]
    end_angle = angles[-1]
    delta_angle = end_angle - start_angle
    if abs(delta_angle) <= 1e-6:
        return None

    tangent_sign = 1.0 if delta_angle >= 0.0 else -1.0
    radius = float(circle["radius"])
    fitted_points = []
    for distance in cum_lengths:
        t = distance / total_length
        angle = start_angle + delta_angle * t
        x = center[0] + radius * math.cos(angle)
        z = center[1] + radius * math.sin(angle)
        dx = -math.sin(angle) * tangent_sign
        dz = math.cos(angle) * tangent_sign
        rot_y = math.degrees(math.atan2(dx, dz)) % 360.0
        fitted_points.append((x, z, rot_y))

    chord = math.hypot(points[-1][0] - points[0][0], points[-1][1] - points[0][1])
    arc_length = abs(delta_angle) * radius
    return {
        "center": center,
        "radius": radius,
        "rms_error": float(circle["rms_error"]),
        "turn_sign": turn_sign,
        "start_angle": start_angle,
        "end_angle": end_angle,
        "delta_angle_rad": delta_angle,
        "delta_angle_deg": math.degrees(delta_angle),
        "arc_length": arc_length,
        "chord_length": chord,
        "points": fitted_points,
    }


def circumradius(
    a: tuple[float, float],
    b: tuple[float, float],
    c: tuple[float, float],
) -> float | None:
    """Return the circumcircle radius for a point triplet in X/Z."""
    ab = math.hypot(b[0] - a[0], b[1] - a[1])
    bc = math.hypot(c[0] - b[0], c[1] - b[1])
    ca = math.hypot(a[0] - c[0], a[1] - c[1])
    twice_area = abs(
        (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
    )
    if ab <= 1e-6 or bc <= 1e-6 or ca <= 1e-6 or twice_area <= 1e-6:
        return None
    return (ab * bc * ca) / (2.0 * twice_area)


def local_radius_samples(points: list[tuple[float, float]]) -> list[dict]:
    """Return point-local radius estimates from consecutive triples."""
    samples = []
    if len(points) < 3:
        return samples
    for idx in range(1, len(points) - 1):
        radius = circumradius(points[idx - 1], points[idx], points[idx + 1])
        if radius is None:
            continue
        samples.append({"index": idx, "point": points[idx], "radius": radius})
    return samples
