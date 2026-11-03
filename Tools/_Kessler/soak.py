#!/usr/bin/env python3
"""
Soak test for a persistent Kessler server.

Starts the server, starts a round (which loads the persistent sector, or generates one on the first run), and records
the tick time monitor's log lines (entity system time per tick, memory) into a CSV file until the time is up or you
press Ctrl+C. Then it shuts the server down cleanly, which saves the sector, and prints a summary.

    python Tools/_Kessler/soak.py --hours 24
    python Tools/_Kessler/soak.py --hours 24 --activity "200 10" --activity-after 10

Run it from the repository root after building (dotnet build --configuration Release). Everything the server prints
goes to soak-server.log; one CSV row per interval goes to soak.csv. You can join the server with a client while it
runs (localhost, port 1212 unless --port is given).
"""
import argparse
import csv
import os
import re
import subprocess
import sys
import threading
import time

STATS = re.compile(
    r"Tick stats over (?P<ticks>\d+) ticks: entity systems mean (?P<mean>[\d.]+) ms, p50 (?P<p50>[\d.]+), "
    r"p95 (?P<p95>[\d.]+), p99 (?P<p99>[\d.]+), max (?P<max>[\d.]+); tick period (?P<period>[\d.]+) ms; "
    r"(?P<over>\d+) over budget; working set (?P<ws>\d+) MB, managed heap (?P<heap>\d+) MB, gen2 GCs (?P<gc2>\d+)")
FIELDS = ["elapsed_min", "ticks", "mean_ms", "p50_ms", "p95_ms", "p99_ms", "max_ms", "period_ms", "over_budget",
          "working_set_mb", "managed_mb", "gen2_gcs", "phase"]
EVENTS = re.compile(r"(Loaded persistent sector|Saved persistent sector|Verified loaded sector|"
                    r"Loaded sector differs|persistent sector could not|Saving the persistent sector|Spawned \d+ mobs)")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--hours", type=float, default=24, help="how long to run (default 24)")
    p.add_argument("--interval", type=int, default=300, help="seconds per CSV row (default 300)")
    p.add_argument("--configuration", default="Release", help="build configuration to run (default Release)")
    p.add_argument("--data-dir", default="soak-data", help="server data folder; keeps the save between runs")
    p.add_argument("--map", default="Dusk")
    p.add_argument("--port", type=int, default=1212)
    p.add_argument("--activity", help='run "persistenceactivity <mobs> <fires>" with these arguments, e.g. "200 10"')
    p.add_argument("--activity-after", type=float, default=10, help="minutes after round start (default 10)")
    p.add_argument("--save-every", type=float, default=0, help="minutes between sectorsave commands (default off)")
    p.add_argument("--out", default="soak.csv")
    p.add_argument("--log", default="soak-server.log")
    a = p.parse_args()

    os.makedirs(a.data_dir, exist_ok=True)
    cmd = ["dotnet", "run", "--project", "Content.Server", "--configuration", a.configuration, "--no-build", "--",
           "--data-dir", os.path.abspath(a.data_dir),
           "--cvar", f"net.port={a.port}",
           "--cvar", f"game.map={a.map}",
           "--cvar", "game.lobbyenabled=true",
           "--cvar", "kessler.persistence.enabled=true",
           # An empty server pauses itself and runs no game logic, which would make the measurements meaningless.
           "--cvar", "game.auto_pause_empty=false",
           "--cvar", f"kessler.diagnostics.tick_log_seconds={a.interval}"]
    print("Starting:", " ".join(cmd))
    server = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              text=True, encoding="utf-8", errors="replace", bufsize=1)

    state = {"round_started": None, "phase": "idle", "done": False, "events": []}
    rows = []
    reader = threading.Thread(target=read_output, args=(server, a, state, rows), daemon=True)
    reader.start()

    def send(line):
        try:
            server.stdin.write(line + "\n")
            server.stdin.flush()
        except (BrokenPipeError, OSError):
            pass

    deadline = time.time() + a.hours * 3600
    sent_start = activity_sent = False
    next_save = None
    try:
        while server.poll() is None and time.time() < deadline:
            time.sleep(1)
            # The server stops reading its console after two seconds of silence, so keep talking to it.
            send("")
            if not sent_start and state.get("ready"):
                send("startround")
                sent_start = True
            started = state["round_started"]
            if started is None:
                continue
            minutes = (time.time() - started) / 60
            if a.activity and not activity_sent and minutes >= a.activity_after:
                send(f"persistenceactivity {a.activity}")
                state["phase"] = "activity"
                activity_sent = True
            if a.save_every > 0:
                next_save = next_save or started + a.save_every * 60
                if time.time() >= next_save:
                    send("sectorsave")
                    next_save += a.save_every * 60
    except KeyboardInterrupt:
        print("\nStopping...")

    if server.poll() is None:
        send("shutdown")
        try:
            server.wait(timeout=600)
        except subprocess.TimeoutExpired:
            print("Server did not shut down in 10 minutes; killing it.")
            server.kill()
    reader.join(timeout=10)
    summarize(rows, state["events"], a)


def read_output(server, a, state, rows):
    new_file = not os.path.exists(a.out)
    with open(a.log, "a", encoding="utf-8") as log, open(a.out, "a", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=FIELDS)
        if new_file:
            writer.writeheader()
        for line in server.stdout:
            log.write(line)
            if "-> Ready" in line:
                state["ready"] = True
            if "NFAdventure rule started" in line or "Started round" in line:
                state["round_started"] = state["round_started"] or time.time()
            if EVENTS.search(line):
                state["events"].append(line.strip())
                print(line.strip())
            m = STATS.search(line)
            if m and state["round_started"] is not None:
                row = {
                    "elapsed_min": round((time.time() - state["round_started"]) / 60, 1),
                    "ticks": int(m["ticks"]), "mean_ms": float(m["mean"]), "p50_ms": float(m["p50"]),
                    "p95_ms": float(m["p95"]), "p99_ms": float(m["p99"]), "max_ms": float(m["max"]),
                    "period_ms": float(m["period"]), "over_budget": int(m["over"]),
                    "working_set_mb": int(m["ws"]), "managed_mb": int(m["heap"]), "gen2_gcs": int(m["gc2"]),
                    "phase": state["phase"],
                }
                writer.writerow(row)
                out.flush()
                rows.append(row)
                print(f"{row['elapsed_min']:7.1f} min  mean {row['mean_ms']:.2f} ms  p99 {row['p99_ms']:.2f}  "
                      f"max {row['max_ms']:.2f}  over budget {row['over_budget']}  "
                      f"{row['working_set_mb']} MB  [{row['phase']}]")


def summarize(rows, events, a):
    print("\n=== Soak summary ===")
    for e in events:
        print(e)
    if not rows:
        print("No tick statistics were recorded.")
        return
    for phase in sorted({r["phase"] for r in rows}):
        rs = [r for r in rows if r["phase"] == phase]
        mean = sum(r["mean_ms"] for r in rs) / len(rs)
        print(f"{phase}: {len(rs)} rows, mean {mean:.2f} ms, worst p99 {max(r['p99_ms'] for r in rs):.2f} ms, "
              f"worst max {max(r['max_ms'] for r in rs):.2f} ms, over budget {sum(r['over_budget'] for r in rs)} ticks")
    print(f"working set: first {rows[0]['working_set_mb']} MB, last {rows[-1]['working_set_mb']} MB, "
          f"max {max(r['working_set_mb'] for r in rows)} MB")
    print(f"managed heap: first {rows[0]['managed_mb']} MB, last {rows[-1]['managed_mb']} MB")
    print(f"CSV: {a.out}, server log: {a.log}")


if __name__ == "__main__":
    sys.exit(main())
