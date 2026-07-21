#!/usr/bin/env python3
"""
Kivi log visualizer — parses a `dotnet run --project Kivi.App -- --metrics`
console log (captured via Tee-Object) and prints a readable terminal report:
dictation outcomes (success/error), per-stage timing, and resource usage.

Usage:
    python scripts/log_visualizer.py logs/run-20260720-114400.log
"""
from __future__ import annotations

import os
import re
import sys
from dataclasses import dataclass, field


# ---------------------------------------------------------------------------
# Loading (auto-detect UTF-16 vs UTF-8 — Tee-Object writes UTF-16 by default)
# ---------------------------------------------------------------------------

def load_text(path: str) -> str:
    with open(path, "rb") as f:
        raw = f.read()
    if raw.startswith(b"\xff\xfe") or raw.startswith(b"\xfe\xff"):
        return raw.decode("utf-16")
    if b"\x00" in raw[:200]:
        return raw.decode("utf-16-le", errors="replace")
    return raw.decode("utf-8", errors="replace")


# ---------------------------------------------------------------------------
# Parsing
# ---------------------------------------------------------------------------

STATE_RE = re.compile(r"state -> (\w+)")
METRIC_BLOCK_RE = re.compile(
    r"Metric Name: ([\w.]+)(?:, Unit: ([\w%]+))?.*?(?=\nMetric Name:|\Z)", re.S
)
GAUGE_VALUE_RE = re.compile(r"\nValue: ([\d.]+)\s*(?:\n|$)")
HISTOGRAM_RE = re.compile(
    r"stage: (\w+)\s*\nValue: Sum: ([\d.]+)\s*Count: (\d+)\s*Min: ([\d.]+)\s*Max: ([\d.]+)"
)
TOTAL_HISTOGRAM_RE = re.compile(
    r"Metric Name: kivi\.dictation\.total\.duration.*?Value: Sum: ([\d.]+)\s*Count: (\d+)\s*Min: ([\d.]+)\s*Max: ([\d.]+)",
    re.S,
)


@dataclass
class StageStats:
    count: int = 0
    sum_ms: float = 0.0
    min_ms: float = 0.0
    max_ms: float = 0.0

    @property
    def avg_ms(self) -> float:
        return self.sum_ms / self.count if self.count else 0.0


@dataclass
class ParsedLog:
    states: list[str] = field(default_factory=list)
    stage_stats: dict[str, StageStats] = field(default_factory=dict)
    total_stats: StageStats | None = None
    rss_samples: list[float] = field(default_factory=list)
    cpu_samples: list[float] = field(default_factory=list)
    exceptions_samples: list[int] = field(default_factory=list)
    gc_gen0: int = 0
    gc_gen1: int = 0
    gc_gen2: int = 0
    gc_pause_ns: float = 0.0
    thread_pool_threads: int = 0
    lock_contention: int = 0


def parse_log(text: str) -> ParsedLog:
    result = ParsedLog()
    result.states = STATE_RE.findall(text)

    # Stage duration histograms — take the LAST occurrence per stage since
    # OTel's console exporter re-prints cumulative totals on every export.
    for stage, s, c, mn, mx in HISTOGRAM_RE.findall(text):
        st = result.stage_stats.setdefault(stage, StageStats())
        st.count, st.sum_ms, st.min_ms, st.max_ms = int(c), float(s), float(mn), float(mx)

    total_matches = TOTAL_HISTOGRAM_RE.findall(text)
    if total_matches:
        s, c, mn, mx = total_matches[-1]
        result.total_stats = StageStats(count=int(c), sum_ms=float(s), min_ms=float(mn), max_ms=float(mx))

    for block_match in re.finditer(
        r"Metric Name: (kivi\.process\.rss|kivi\.process\.cpu|process\.runtime\.dotnet\.exceptions\.count)"
        r".*?\nValue: ([\d.]+)",
        text,
        re.S,
    ):
        name, value = block_match.groups()
        if name == "kivi.process.rss":
            result.rss_samples.append(float(value))
        elif name == "kivi.process.cpu":
            result.cpu_samples.append(float(value))
        elif name.endswith("exceptions.count"):
            result.exceptions_samples.append(int(float(value)))

    gc_blocks = re.findall(
        r"Metric Name: process\.runtime\.dotnet\.gc\.collections\.count.*?(?=\nMetric Name:|\Z)", text, re.S
    )
    if gc_blocks:
        last = gc_blocks[-1]
        for gen, value in re.findall(r"generation: (gen\d)\s*\nValue: (\d+)", last):
            if gen == "gen0":
                result.gc_gen0 = int(value)
            elif gen == "gen1":
                result.gc_gen1 = int(value)
            elif gen == "gen2":
                result.gc_gen2 = int(value)

    gc_duration = re.findall(r"Metric Name: process\.runtime\.dotnet\.gc\.duration.*?Value: ([\d.]+)", text, re.S)
    if gc_duration:
        result.gc_pause_ns = float(gc_duration[-1])

    threads = re.findall(
        r"Metric Name: process\.runtime\.dotnet\.thread_pool\.threads\.count.*?Value: (\d+)", text, re.S
    )
    if threads:
        result.thread_pool_threads = int(threads[-1])

    contention = re.findall(
        r"Metric Name: process\.runtime\.dotnet\.monitor\.lock_contention\.count.*?Value: (\d+)", text, re.S
    )
    if contention:
        result.lock_contention = int(contention[-1])

    return result


# ---------------------------------------------------------------------------
# Rendering
# ---------------------------------------------------------------------------

def _enable_windows_vt_mode() -> bool:
    """Try to turn on ANSI escape processing in the Windows console. Returns
    True if VT mode is (now) usable, False if colors should be disabled."""
    if os.name != "nt":
        return True
    try:
        import ctypes

        kernel32 = ctypes.windll.kernel32
        ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004
        STD_OUTPUT_HANDLE = -11
        handle = kernel32.GetStdHandle(STD_OUTPUT_HANDLE)
        mode = ctypes.c_uint32()
        if not kernel32.GetConsoleMode(handle, ctypes.byref(mode)):
            return False
        new_mode = mode.value | ENABLE_VIRTUAL_TERMINAL_PROCESSING
        return bool(kernel32.SetConsoleMode(handle, new_mode))
    except Exception:
        return False


_USE_COLOR = ("--no-color" not in sys.argv) and _enable_windows_vt_mode()


class C:
    RESET = "\033[0m" if _USE_COLOR else ""
    BOLD = "\033[1m" if _USE_COLOR else ""
    DIM = "\033[2m" if _USE_COLOR else ""
    RED = "\033[31m" if _USE_COLOR else ""
    GREEN = "\033[32m" if _USE_COLOR else ""
    YELLOW = "\033[33m" if _USE_COLOR else ""
    CYAN = "\033[36m" if _USE_COLOR else ""
    MAGENTA = "\033[35m" if _USE_COLOR else ""


def bar(value: float, max_value: float, width: int = 30) -> str:
    if max_value <= 0:
        return " " * width
    filled = int(round((value / max_value) * width))
    filled = max(0, min(width, filled))
    return "#" * filled + "-" * (width - filled)


def section(title: str) -> None:
    print()
    print(f"{C.BOLD}{C.CYAN}{'=' * 70}{C.RESET}")
    print(f"{C.BOLD}{C.CYAN}{title}{C.RESET}")
    print(f"{C.BOLD}{C.CYAN}{'=' * 70}{C.RESET}")


def render(log: ParsedLog) -> None:
    # --- Dictation outcomes ---------------------------------------------
    section("DICTATION OUTCOMES")
    n_error = log.states.count("Error")
    n_pasted = log.states.count("Pasting")
    n_listen = log.states.count("Listening")
    total_attempts = n_pasted + n_error
    print(f"  Recording attempts (hotkey releases): {C.BOLD}{n_listen}{C.RESET}")
    print(f"  Successful pastes:                    {C.GREEN}{n_pasted}{C.RESET}")
    print(f"  Failed (Error state):                 {C.RED if n_error else C.GREEN}{n_error}{C.RESET}")
    if total_attempts:
        rate = n_pasted / total_attempts * 100
        color = C.GREEN if rate >= 90 else C.YELLOW if rate >= 60 else C.RED
        print(f"  Success rate:                          {color}{rate:.0f}%{C.RESET}")
    if log.exceptions_samples:
        total_ex = log.exceptions_samples[-1]
        if total_ex:
            print(f"  {C.RED}.NET exceptions thrown (session total): {total_ex}{C.RESET}")
            print(f"  {C.DIM}(exception details are not logged by Kivi.App today - only the count){C.RESET}")

    # --- Stage timing -----------------------------------------------------
    section("STAGE TIMING (per-dictation pipeline)")
    order = ["record", "stt", "cleanup", "paste"]
    known_max = max((s.max_ms for k, s in log.stage_stats.items()), default=0)
    if not log.stage_stats:
        print(f"  {C.DIM}no stage-duration metrics found in this log{C.RESET}")
    for stage in order:
        st = log.stage_stats.get(stage)
        if not st:
            continue
        print(
            f"  {stage:9s} count={st.count:>3d}  avg={st.avg_ms:7.1f}ms  "
            f"min={st.min_ms:7.1f}ms  max={st.max_ms:7.1f}ms  "
            f"{C.MAGENTA}{bar(st.avg_ms, known_max)}{C.RESET}"
        )
    if log.total_stats:
        st = log.total_stats
        print(
            f"\n  {C.BOLD}{'total':9s} count={st.count:>3d}  avg={st.avg_ms:7.1f}ms  "
            f"min={st.min_ms:7.1f}ms  max={st.max_ms:7.1f}ms{C.RESET}"
        )

    # --- Resource usage -----------------------------------------------------
    section("RESOURCE USAGE (process-wide)")
    if log.rss_samples:
        print(
            f"  RSS (memory):  first={log.rss_samples[0]:6.1f}MB  "
            f"last={log.rss_samples[-1]:6.1f}MB  min={min(log.rss_samples):6.1f}MB  "
            f"max={max(log.rss_samples):6.1f}MB  ({len(log.rss_samples)} samples)"
        )
        budget_color = C.GREEN if max(log.rss_samples) < 100 else C.RED
        print(f"  {budget_color}<100MB budget: {'OK' if max(log.rss_samples) < 100 else 'EXCEEDED'}{C.RESET}")
    if log.cpu_samples:
        print(
            f"  CPU:           first={log.cpu_samples[0]:5.2f}%   "
            f"last={log.cpu_samples[-1]:5.2f}%   min={min(log.cpu_samples):5.2f}%   "
            f"max={max(log.cpu_samples):5.2f}%   ({len(log.cpu_samples)} samples)"
        )

    # --- .NET runtime health -----------------------------------------------
    section(".NET RUNTIME HEALTH")
    print(f"  GC collections:      gen0={log.gc_gen0}  gen1={log.gc_gen1}  gen2={log.gc_gen2}")
    if log.gc_pause_ns:
        print(f"  Total GC pause time:  {log.gc_pause_ns / 1_000_000:.2f}ms")
    print(f"  Thread pool threads:  {log.thread_pool_threads}")
    contention_color = C.RED if log.lock_contention else C.GREEN
    print(f"  Lock contention:      {contention_color}{log.lock_contention}{C.RESET}")

    print()


def main() -> int:
    args = [a for a in sys.argv[1:] if a != "--no-color"]
    if len(args) != 1:
        print(f"usage: {sys.argv[0]} <path-to-log-file> [--no-color]")
        return 1
    path = args[0]
    try:
        text = load_text(path)
    except FileNotFoundError:
        print(f"error: file not found: {path}")
        return 1
    log = parse_log(text)
    print(f"{C.BOLD}Kivi log report - {path}{C.RESET}")
    render(log)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
