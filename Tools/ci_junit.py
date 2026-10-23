#!/usr/bin/env python3
"""Run a command and write a JUnit XML report for GitLab unit test reports."""

import argparse
import subprocess
import sys
import time
from xml.sax.saxutils import quoteattr


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--name", required=True, help="Test suite and test case name")
    parser.add_argument("--output", required=True, help="JUnit XML file to write")
    parser.add_argument("command", nargs=argparse.REMAINDER, help="Command to run, after --")
    args = parser.parse_args()

    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    start = time.monotonic()
    result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    elapsed = time.monotonic() - start

    # Keep the job log intact.
    sys.stdout.write(result.stdout)

    name = quoteattr(args.name)
    failed = result.returncode != 0
    body = ""
    if failed:
        message = quoteattr(f"{args.name} failed with exit code {result.returncode}")
        # Strip characters that are illegal in XML 1.0.
        text = "".join(c for c in result.stdout if c in "\t\n\r" or ord(c) >= 0x20)
        text = text.replace("]]>", "]]]]><![CDATA[>")
        body = f"<failure message={message}><![CDATA[{text}]]></failure>"

    with open(args.output, "w", encoding="utf-8") as f:
        f.write(
            '<?xml version="1.0" encoding="UTF-8"?>\n'
            f'<testsuites><testsuite name={name} tests="1" failures="{int(failed)}" time="{elapsed:.3f}">'
            f'<testcase classname={name} name={name} time="{elapsed:.3f}">{body}</testcase>'
            "</testsuite></testsuites>\n"
        )

    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
