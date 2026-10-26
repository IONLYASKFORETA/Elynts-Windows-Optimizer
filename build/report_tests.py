"""Turns a test run's reports into GitHub annotations, so the result can be read without the job's log.

CI's Test step writes the reports (`--report-xunit-ctrf`, and `--hangdump` for a test that stops moving); the step
after it runs, from the repo root:

    python build/report_tests.py TestResults

It prints an error for each test that hung and each that failed (GitHub keeps 10 errors per step), a notice with the
totals, and a notice naming the skipped tests. A cloud session reads them with
`gh api repos/IONLYASKFORETA/Elynts-Windows-Optimizer/check-runs/<id>/annotations`.
"""
import glob, json, os, sys

folder = sys.argv[1]
root = os.environ.get("GITHUB_WORKSPACE", os.getcwd())
MAX_ERRORS = 10  # GitHub drops error annotations past this many in one step.
MAX_SKIPPED = 40


def escape(text):
    return text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def escape_property(text):
    return escape(text).replace(":", "%3A").replace(",", "%2C")


def annotate(kind, title, message, file=None, line=None):
    where = f"file={escape_property(file)},line={line or 1}," if file else ""
    print(f"::{kind} {where}title={escape_property(title)}::{escape(message)}")


def in_repo(path):
    """The test's file relative to the repo root, as annotations need, or None when it isn't under the root."""
    if not path:
        return None
    try:
        relative = os.path.relpath(path, root)
    except ValueError:  # another drive on Windows
        return None
    return None if relative.startswith("..") else relative.replace(os.sep, "/")


def found(pattern):
    return sorted(glob.glob(os.path.join(folder, "**", pattern), recursive=True))


tests = []
for path in found("*.ctrf"):
    with open(path, encoding="utf-8") as report:
        tests += json.load(report)["results"]["tests"]

# A hang log lists the tests still running when the hang timeout stopped their module: "[00:05:00] Some.Test".
hung = []
for path in found("*_hang.log"):
    with open(path, encoding="utf-8") as log:
        for entry in log:
            elapsed, _, name = entry.strip().removeprefix("[").partition("] ")
            if name:
                hung.append((name, elapsed))

failed = [test for test in tests if test["status"] == "failed"]
skipped = [test for test in tests if test["status"] == "skipped"]
passed = sum(1 for test in tests if test["status"] == "passed")

problems = [("Hung test", f"{name} was still running after {elapsed}, so the hang timeout stopped its tests.", None, None)
            for name, elapsed in hung]
problems += [(test["name"], test.get("message") or "Failed.", in_repo(test.get("filePath")), test.get("line"))
             for test in failed]
for title, message, file, line in problems[:MAX_ERRORS]:
    annotate("error", title, message, file, line)

if not tests and not hung:
    annotate("warning", "Tests", f"No test reports in {folder}, so the tests didn't run or didn't finish.")
    sys.exit(0)

totals = f"{len(tests)} tests: {passed} passed, {len(failed)} failed, {len(skipped)} skipped."
if hung:
    totals += f" {len(hung)} hung, and the tests stopped with them aren't counted."
if len(problems) > MAX_ERRORS:
    totals += f" Only the first {MAX_ERRORS} problems have their own annotation; the job's log has the rest."
annotate("notice", "Tests", totals)

if skipped:
    # One line per test method: a theory's data rows skip together, for the same reason.
    reasons = {}
    for test in skipped:
        method = test["name"].split("(", 1)[0]
        count, reason = reasons.get(method, (0, test.get("message", "")))
        reasons[method] = (count + 1, reason)
    lines = [f"{method}{f' (x{count})' if count > 1 else ''}: {reason}"
             for method, (count, reason) in sorted(reasons.items())]
    if len(lines) > MAX_SKIPPED:
        lines = lines[:MAX_SKIPPED] + [f"and {len(lines) - MAX_SKIPPED} more"]
    annotate("notice", "Skipped tests", "\n".join(lines))
