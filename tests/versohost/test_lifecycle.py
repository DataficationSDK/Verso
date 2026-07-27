"""Coverage for the host leaving when the process that launched it does."""

import os
import subprocess
import sys
import time

import pytest

import versohost


class Enough(Exception):
    """Raised by a test's own sleep to end a loop that is designed never to end."""


def answers(*values):
    """A parent id reader that returns, or raises, each value in turn."""
    remaining = list(values)

    def read():
        value = remaining.pop(0)
        if isinstance(value, BaseException):
            raise value
        return value

    return read


def test_a_changed_parent_id_ends_the_process():
    left_with = []

    versohost.watch_parent(
        7,
        interval=0,
        get_parent_id=answers(7, 7, 1),
        sleep=lambda _: None,
        exit_process=left_with.append)

    assert left_with == [versohost.EXIT_PARENT_GONE]


def test_an_unchanged_parent_id_keeps_the_watch_waiting():
    waits = []

    def sleep(seconds):
        waits.append(seconds)
        if len(waits) == 3:
            raise Enough

    def leave(code):
        pytest.fail("left while the launching process was still there")

    with pytest.raises(Enough):
        versohost.watch_parent(
            7, interval=0.5, get_parent_id=lambda: 7, sleep=sleep, exit_process=leave)

    # The interval is honoured rather than spun on, which matters for a thread that outlives
    # every cell in the session.
    assert waits == [0.5, 0.5, 0.5]


def test_a_parent_id_that_cannot_be_read_is_not_taken_as_a_departure():
    left_with = []

    versohost.watch_parent(
        7,
        interval=0,
        get_parent_id=answers(OSError("not now"), 7, 1),
        sleep=lambda _: None,
        exit_process=left_with.append)

    # It left on the third answer, the one that actually differed, not on the failure to read.
    assert left_with == [versohost.EXIT_PARENT_GONE]


def test_the_pid_the_kernel_passed_is_the_one_watched(monkeypatch):
    monkeypatch.setenv("VERSO_PYHOST_PARENT", "4242")

    assert versohost._expected_parent_id() == 4242


@pytest.mark.parametrize("given", ["", "not a pid", "0", "-1"])
def test_a_pid_that_was_not_passed_falls_back_to_the_live_reading(monkeypatch, given):
    # A host started by hand has no kernel to have been told by, and watching its actual parent
    # is better than not watching at all.
    monkeypatch.setenv("VERSO_PYHOST_PARENT", given)

    assert versohost._expected_parent_id() == os.getppid()


def test_no_watch_is_started_where_a_parent_id_would_never_change():
    assert versohost.start_parent_watchdog(platform_name="nt") is None


def test_the_watch_runs_on_a_daemon_thread():
    # A non-daemon thread would hold up an orderly shutdown for the length of one interval, and
    # this thread has nothing to finish that anyone is waiting for.
    thread = versohost.start_parent_watchdog(
        interval=3600, platform_name="posix", expected_parent_id=os.getppid())

    assert thread is not None
    assert thread.daemon
    assert thread.is_alive()


LAUNCHER = """
import os
import subprocess
import sys

# Stands in for the kernel: starts the host, tells it which pid to watch exactly as the kernel
# does, reports it, and exits without waiting, which is what leaves the host with a parent that is
# gone. The host's streams are discarded so it holds no pipe of ours open.
child = subprocess.Popen(
    [sys.executable, "-B", "-c", sys.argv[1]],
    env=dict(os.environ, VERSO_PYHOST_PARENT=str(os.getpid())),
    stdout=subprocess.DEVNULL,
    stderr=subprocess.DEVNULL)
print(child.pid, flush=True)
"""

HOST = """
import sys
import time

sys.path.insert(0, {host_directory!r})
import versohost

versohost.start_parent_watchdog(interval=0.1)

# Stands in for a cell that never returns to the dispatch loop, which is the one case the socket
# closing cannot cover.
while True:
    time.sleep(0.05)
"""


def is_running(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


@pytest.mark.skipif(
    os.name != "posix", reason="the watch reads a reparenting that only POSIX performs")
def test_a_host_whose_launcher_has_exited_leaves_on_its_own():
    host_source = HOST.format(host_directory=os.path.dirname(versohost.__file__))

    launcher = subprocess.run(
        [sys.executable, "-B", "-c", LAUNCHER, host_source],
        capture_output=True,
        text=True,
        timeout=60,
        check=True)

    pid = int(launcher.stdout.strip())
    try:
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            if not is_running(pid):
                return
            time.sleep(0.1)

        pytest.fail("the host outlived the process that launched it")
    finally:
        try:
            os.kill(pid, 9)
        except OSError:
            pass
