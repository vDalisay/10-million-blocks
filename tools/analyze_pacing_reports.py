#!/usr/bin/env python3
"""Summarize debug pacing reports produced by PacingTelemetryRecorder.

Usage:
    python tools/analyze_pacing_reports.py <report-file-or-directory> [...]

The parser intentionally accepts both report_version 1 and newer key=value reports and ignores
unknown fields so playtest logs remain useful as telemetry evolves.
"""

from __future__ import annotations

import argparse
import statistics
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


@dataclass(frozen=True)
class Report:
    path: Path
    values: dict[str, str]

    @property
    def world(self) -> str:
        return self.values.get("world", "unknown")

    @property
    def completed(self) -> bool:
        return self.values.get("completed", "false").lower() == "true"

    def number(self, key: str) -> float | None:
        raw = self.values.get(key)
        if raw is None or raw in {"", "none", "already_present_at_session_start"}:
            return None
        try:
            return float(raw.replace(",", "."))
        except ValueError:
            return None


def parse_report(path: Path) -> Report | None:
    values: dict[str, str] = {}
    try:
        text = path.read_text(encoding="utf-8-sig")
    except (OSError, UnicodeError) as exc:
        print(f"warning: could not read {path}: {exc}")
        return None

    for raw_line in text.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("[") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key = key.strip()
        if key and key not in values:
            values[key] = value.strip()

    if "world" not in values or "active_session_seconds" not in values:
        return None
    return Report(path=path, values=values)


def discover(inputs: Iterable[str]) -> list[Path]:
    paths: list[Path] = []
    for input_value in inputs:
        candidate = Path(input_value).expanduser()
        if candidate.is_dir():
            paths.extend(sorted(candidate.glob("*.txt")))
        elif candidate.is_file():
            paths.append(candidate)
        else:
            print(f"warning: not found: {candidate}")
    return paths


def fmt_seconds(value: float | None) -> str:
    if value is None:
        return "—"
    minutes, seconds = divmod(max(0.0, value), 60.0)
    if minutes >= 60:
        hours, minutes = divmod(int(minutes), 60)
        return f"{hours:d}h {minutes:d}m {seconds:02.0f}s"
    return f"{int(minutes):d}m {seconds:02.0f}s"


def mean(values: Iterable[float | None]) -> float | None:
    present = [value for value in values if value is not None]
    return statistics.fmean(present) if present else None


def integer(report: Report, key: str) -> int:
    value = report.number(key)
    return 0 if value is None else int(round(value))


def print_report_summary(reports: list[Report]) -> None:
    groups: dict[str, list[Report]] = defaultdict(list)
    for report in reports:
        groups[report.world].append(report)

    print("# 10 Million Blocks pacing summary")
    print()
    print(f"Reports parsed: **{len(reports)}** across **{len(groups)}** world(s).")
    print()
    print("| World | Runs | Completed | Avg session | Avg longest action gap | Avg manual share | Avg resources end | Stops / relocations |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|")

    for world, world_reports in sorted(groups.items()):
        completed = sum(1 for report in world_reports if report.completed)
        avg_seconds = mean(report.number("active_session_seconds") for report in world_reports)
        avg_gap = mean(report.number("longest_observed_decision_gap_seconds") for report in world_reports)

        shares: list[float] = []
        for report in world_reports:
            manual = report.number("blocks_manual_run") or 0.0
            automated = report.number("blocks_automated_run") or 0.0
            other = report.number("blocks_other_sources_run") or 0.0
            total = manual + automated + other
            if total > 0:
                shares.append(manual * 100.0 / total)
        manual_share = statistics.fmean(shares) if shares else None
        resources = mean(report.number("resources_end") for report in world_reports)
        stops = sum(integer(report, "automation_stops_session") for report in world_reports)
        relocations = sum(integer(report, "automation_relocations_session") for report in world_reports)

        print(
            f"| {world} | {len(world_reports)} | {completed} | {fmt_seconds(avg_seconds)} | "
            f"{fmt_seconds(avg_gap)} | "
            f"{('—' if manual_share is None else f'{manual_share:.1f}%')} | "
            f"{('—' if resources is None else f'{resources:,.0f}')} | {stops} / {relocations} |"
        )

    print()
    print("## Completed runs")
    print()
    completed_reports = [report for report in reports if report.completed]
    if not completed_reports:
        print("No completed-run reports found yet.")
    else:
        print("| World | Active session | Manual | Automated | Other | First automation | Longest action gap | Resources |")
        print("|---|---:|---:|---:|---:|---:|---:|---:|")
        for report in sorted(completed_reports, key=lambda item: (item.world, item.path.name)):
            first = report.values.get("first_automation_placement_seconds", "—")
            if first not in {"—", "none", "already_present_at_session_start"}:
                try:
                    first = fmt_seconds(float(first.replace(",", ".")))
                except ValueError:
                    pass
            print(
                f"| {report.world} | {fmt_seconds(report.number('active_session_seconds'))} | "
                f"{integer(report, 'blocks_manual_run'):,} | {integer(report, 'blocks_automated_run'):,} | "
                f"{integer(report, 'blocks_other_sources_run'):,} | {first} | "
                f"{fmt_seconds(report.number('longest_observed_decision_gap_seconds'))} | "
                f"{integer(report, 'resources_end'):,} |"
            )

    long_gaps = [
        report
        for report in reports
        if report.number("longest_observed_decision_gap_seconds") is not None
        and (report.number("longest_observed_decision_gap_seconds") or 0.0) >= 60.0
    ]
    print()
    print("## Action-gap flags")
    print()
    if not long_gaps:
        print("No observed action gap of 60 seconds or longer in the supplied reports.")
    else:
        for report in sorted(
            long_gaps,
            key=lambda item: item.number("longest_observed_decision_gap_seconds") or 0.0,
            reverse=True,
        ):
            gap = report.number("longest_observed_decision_gap_seconds")
            print(f"- {report.world}: {fmt_seconds(gap)} — `{report.path.name}`")
        print()
        print("These are action-gap signals, not automatic boredom judgments. Review the corresponding playtest context before changing balance.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", nargs="+", help="Pacing report .txt file(s) or directories containing them")
    args = parser.parse_args()

    paths = discover(args.inputs)
    reports = [report for path in paths if (report := parse_report(path)) is not None]
    if not reports:
        print("No valid pacing reports found.")
        return 1

    print_report_summary(reports)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
