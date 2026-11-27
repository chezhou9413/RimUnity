import csv
import json
import math
import statistics
import sys
from collections import defaultdict
from pathlib import Path

RUNTIME_ASSEMBLIES = ("RimUnity", "RimUnity.Core", "RimUnity.Spine")
WINDOW_ACQUIRE_METRICS = ("acquire_before_window_ms", "window_acquire_ms")


def csv_rows(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        yield from csv.DictReader(stream)


def stats(values):
    ordered = sorted(values)
    if not ordered:
        raise ValueError("No data for metric.")
    return {
        "mean": statistics.mean(ordered),
        "p95": ordered[math.ceil(len(ordered) * .95) - 1],
        "p99": ordered[math.ceil(len(ordered) * .99) - 1],
        "max": ordered[-1],
    }


def read_run(result):
    completion = json.loads(result.read_text(encoding="utf-8-sig"))
    if not completion["Passed"]:
        raise ValueError(f"Game reported a failed sample: {result}")
    runtime_hashes = completion.get("RuntimeHashes")
    prefix = Path(completion["Report"])
    report = json.loads(prefix.with_suffix(".json").read_text(encoding="utf-8-sig"))
    if report["StopReason"] != "采样时间结束":
        raise ValueError(f"Incomplete sample: {prefix}")
    frames = {int(row["frame"]): row for row in csv_rows(prefix.with_suffix(".frames.csv"))}
    windows = defaultdict(dict)
    for row in csv_rows(prefix.with_suffix(".windows.csv")):
        windows[int(row["frame"])][row["window"]] = float(row["elapsed_ms"])
    waits = {int(row["frame"]): row for row in csv_rows(prefix.with_suffix(".waits.csv"))}
    render_file = prefix.with_suffix(".render.csv")
    render = list(csv_rows(render_file)) if render_file.is_file() else []
    for row in render:
        if "swapchain_acquire_ms" in row:
            # Older captures measured the same window method, including held images.
            row["acquire_before_window_ms"] = row.pop("acquire_before_swapchain_ms")
            row["window_acquire_ms"] = row.pop("swapchain_acquire_ms")
    split_acquire = [row for row in render if int(row.get("window_acquires", 0)) > 0]
    render_ms = {
        key: stats([float(row[key]) for row in render])
        for key in ("total_ms", "acquire_ms", "submit_present_ms", "pace_ms", "other_ms")
    } if render else {}
    if split_acquire:
        render_ms.update({
            key: stats([float(row[key]) for row in split_acquire])
            for key in WINDOW_ACQUIRE_METRICS
        })
    complete = [frame for frame in frames if len(windows[frame]) == 4]
    if not complete:
        raise ValueError(f"No complete frames: {prefix}")
    complete_set = set(complete)
    intervals = [float(frames[frame]["interval_ms"]) for frame in complete]
    coverage = [frame for frame in complete if frame in waits]
    inconsistent = sum(float(waits[frame]["throttle_ms"]) > windows[frame]["EnqueueToPrePoll"] + .05
                       for frame in coverage)
    wait_metrics = {
        name: stats([float(waits[frame][name]) for frame in coverage])
        for name in ("throttle_ms", "render_backlog_ms", "frame_sleep_ms")
    } if coverage else {}
    top = []
    for frame in sorted(complete, key=lambda key: float(frames[key]["interval_ms"]), reverse=True)[:8]:
        host = windows[frame]
        wait = waits.get(frame, {})
        row = {key: float(value) for key, value in wait.items() if key.endswith("_ms")}
        top.append({"frame": frame, "interval_ms": float(frames[frame]["interval_ms"]),
                    "largest_window": max(host, key=host.get), "windows": host, "waits": row,
                    "render_overlap": render_overlap(wait, render)})
    stage_self = defaultdict(float)
    stage_file = prefix.with_suffix(".stages.csv")
    if stage_file.is_file():
        for row in csv_rows(stage_file):
            if int(row["frame"]) in complete_set:
                stage_self[row["stage"]] += float(row["self_ms"])
    family_file = prefix.with_suffix(".families.csv")
    family_self = {
        f"{row['family']}/{row['name']}/{row['stage']}": float(row["mean_self_ms_per_frame"])
        for row in csv_rows(family_file)
        if row["stage"] == "SimulationSystemStep"
    } if family_file.is_file() else {}
    keys = ("Scenario", "Concurrency", "StressEmissionRule", "StressMaxParticlesPerSystem",
            "BloomEnabled", "BloomLayerMask", "BloomIntensity", "BloomThreshold", "BloomSoftKnee",
            "BloomScatter", "MapSize", "CameraRootSize", "Resolution", "CpuMode",
            "RequestedSeconds", "WarmupSeconds", "RandomSeed", "RuntimeIsStandalone", "GameVersion",
            "ParticleUploadMode", "ParticleAnalysisBuild", "CpuStageSchema")
    settings = {key: report["Configuration"].get(key) for key in keys}
    settings["MapHdrMode"] = completion.get("MapHdrMode")
    settings["RenderTrace"] = completion.get("RenderTrace")
    return {
        "report": str(prefix),
        "runtime": report["Configuration"]["RimUnityVersion"],
        "runtime_file_version": report["Configuration"].get("RuntimeVersion"),
        "runtime_sha256": runtime_hashes.get("RimUnity") if isinstance(runtime_hashes, dict) else completion.get("RuntimeHash"),
        "runtime_hashes": runtime_hashes,
        "settings": settings,
        "frames": len(complete),
        "wait_probe_frames": len(coverage),
        "wait_probe_boundary_mismatches": inconsistent,
        "frame_ms": stats(intervals),
        "bridge_ms": stats([windows[frame]["Bridge"] for frame in complete]),
        "windows_ms": {name: stats([windows[frame][name] for frame in complete])
                       for name in windows[complete[0]]},
        "waits_ms": wait_metrics,
        "render_trace_frames": len(render),
        "window_acquire_trace_frames": len(split_acquire),
        "render_ms": render_ms,
        "over_33ms": sum(value > 33.333 for value in intervals),
        "workload": {key: statistics.mean(int(frames[frame][key]) for frame in complete)
                     for key in ("instances", "systems", "particles", "trail_points", "submitted_vertices", "submitted_indices")},
        "top_stages_self_ms": {name: value / len(complete)
                               for name, value in sorted(stage_self.items(), key=lambda item: -item[1])[:8]},
        "family_simulation_self_ms": family_self,
        "slowest": top,
    }


def render_overlap(wait, render):
    start = int(wait.get("backlog_start_ticks", 0))
    end = int(wait.get("backlog_end_ticks", 0))
    if not render or end <= start:
        return {}
    ms_per_tick = float(wait["render_backlog_ms"]) / (end - start)
    matches = []
    last_completion = 0
    for packet in render:
        overlap = min(end, int(packet["end_ticks"])) - max(start, int(packet["start_ticks"]))
        if overlap > 0:
            completion = int(packet.get("queue_complete_end_ticks", 0))
            if start <= completion <= end:
                last_completion = max(last_completion, completion)
            matches.append({"host_frame": int(packet["host_frame"]), "overlap_ms": overlap * ms_per_tick,
                            **{key: float(value) for key, value in packet.items() if key.endswith("_ms")
                               and (key not in WINDOW_ACQUIRE_METRICS
                                    or int(packet.get("window_acquires", 0)) > 0)}})
    return {
        "render_packet_overlap_ms": sum(row["overlap_ms"] for row in matches),
        "after_last_queue_completion_ms": (end - last_completion) * ms_per_tick if last_completion else None,
        "frames": sorted(matches, key=lambda row: -row["overlap_ms"])[:3],
    }


def compare(runs):
    names = set(runs)
    if names == {"Profile"}:
        return {"status": "inspection"}
    if names not in ({"A1", "A2"}, {"A1", "A2", "B1", "B2"}):
        return {"status": "incomplete", "completed": sorted(names)}
    all_runs = list(runs.values())
    required_settings = {"MapHdrMode": str, "RenderTrace": bool, "ParticleAnalysisBuild": bool, "CpuStageSchema": int}
    for name, run in runs.items():
        settings = run["settings"]
        for key, value_type in required_settings.items():
            value = settings.get(key)
            if type(value) is not value_type or (key in {"MapHdrMode", "CpuStageSchema"} and not value):
                return {"status": "measurement_settings_missing", "run": name, "setting": key}
    if any(run["settings"] != all_runs[0]["settings"] for run in all_runs[1:]):
        return {"status": "settings_mismatch"}
    for key in all_runs[0]["workload"]:
        values = [run["workload"][key] for run in all_runs]
        tolerance = 0 if key in {"instances", "systems"} else .02
        if (max(values) - min(values)) / max(1, statistics.mean(values)) > tolerance:
            return {"status": "workload_mismatch", "metric": key, "values": values}
    a = [runs[name] for name in ("A1", "A2")]
    b = [runs[name] for name in ("B1", "B2")] if "B1" in runs else []
    for name, run in runs.items():
        hashes = run["runtime_hashes"]
        for assembly in RUNTIME_ASSEMBLIES:
            if not isinstance(hashes, dict) or not isinstance(hashes.get(assembly), str) or len(hashes[assembly]) != 64:
                return {"status": "runtime_hashes_missing", "run": name, "assembly": assembly}
    for label, group in (("A", a), ("B", b)):
        if not group:
            continue
        for assembly in RUNTIME_ASSEMBLIES:
            if group[0]["runtime_hashes"][assembly] != group[1]["runtime_hashes"][assembly]:
                return {"status": "runtime_mismatch", "group": label, "assembly": assembly}
    rows = {}
    groups = {
        "frame_ms": lambda run: run["frame_ms"],
        "bridge_ms": lambda run: run["bridge_ms"],
        "bridge_to_enqueue_ms": lambda run: run["windows_ms"]["BridgeToEnqueue"],
    }
    waits_complete = all(run["wait_probe_frames"] == run["frames"] and not run["wait_probe_boundary_mismatches"]
                         for run in all_runs)
    if waits_complete:
        groups["render_backlog_ms"] = lambda run: run["waits_ms"]["render_backlog_ms"]
    def add_comparison(name, metric):
        first = [metric(run) for run in a]
        second = [metric(run) for run in b]
        spread = max(abs(first[0] - first[1]), abs(second[0] - second[1]) if second else 0)
        delta = statistics.mean(second) - statistics.mean(first) if second else None
        verdict = ("within_repeat_spread" if abs(delta) <= spread else
                   "candidate_faster" if delta < 0 else "candidate_slower") if delta is not None else None
        rows[name] = {"baseline": first, "candidate": second, "delta_ms": delta,
                      "repeat_spread_ms": spread, "verdict": verdict}

    for measurement, metric in groups.items():
        for quantile in ("mean", "p95", "p99"):
            add_comparison(f"{measurement}/{quantile}", lambda run: metric(run)[quantile])
    measured_stages = set.intersection(*(set(run["top_stages_self_ms"]) for run in all_runs))
    for stage in sorted(measured_stages):
        add_comparison(f"stage_self_ms/{stage}", lambda run: run["top_stages_self_ms"][stage])
    measured_families = set.intersection(*(set(run["family_simulation_self_ms"]) for run in all_runs))
    for family in sorted(measured_families):
        add_comparison(f"family_self_ms/{family}", lambda run: run["family_simulation_self_ms"][family])
    return {"status": "compared" if b else "baseline_repeat",
            "diagnostic_only": all_runs[0]["settings"]["ParticleAnalysisBuild"],
            "waits_complete": waits_complete, "metrics": rows}


def main(root):
    files = sorted(root.glob("*/rimunity-performance-result.json"))
    if not files:
        raise ValueError(f"No completed lab runs in {root}")
    runs = {file.parent.name: read_run(file) for file in files}
    summary = {"runs": runs, "comparison": compare(runs)}
    target = root / "summary.json"
    target.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    for name, run in runs.items():
        print(f"{name} frames={run['frames']} wait-probes={run['wait_probe_frames']} "
              f"frame_mean={run['frame_ms']['mean']:.3f}ms frame_p99={run['frame_ms']['p99']:.3f}ms "
              f"bridge_mean={run['bridge_ms']['mean']:.3f}ms")
    print(f"{summary['comparison']['status']}: {target}")
    return 0 if summary["comparison"]["status"] in {"inspection", "baseline_repeat", "compared"} else 2


if __name__ == "__main__":
    sys.exit(main(Path(sys.argv[1])))
