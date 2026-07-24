"""Shared fixtures for the host script tests.

The script under test is an embedded resource of the Verso.Python assembly rather than an
installed package, so it is imported straight from the source tree.
"""

import os
import sys

import pytest

# The script is imported from the source tree, so byte-code caching would drop a __pycache__
# directory next to it. Disabled before the import rather than ignored afterwards.
sys.dont_write_bytecode = True

HOST_DIRECTORY = os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", "..", "src", "Verso.Python", "Host")
)

if HOST_DIRECTORY not in sys.path:
    sys.path.insert(0, HOST_DIRECTORY)

import versohost  # noqa: E402  (the path has to be set up before the import)


class RecordingChannel:
    """Stands in for the socket channel and keeps everything the host tried to send."""

    def __init__(self):
        self.messages = []

    def write(self, message):
        self.messages.append(message)

    def of_type(self, message_type):
        return [m for m in self.messages if m.get("type") == message_type]

    def stream_text(self, name=None):
        return "".join(
            m["text"] for m in self.of_type("stream")
            if name is None or m["name"] == name
        )


@pytest.fixture
def channel():
    return RecordingChannel()


@pytest.fixture
def session(channel):
    """A host session wired to a recording channel, without redirecting the real streams."""
    created = versohost.HostSession(channel)
    yield created
    created.close()


@pytest.fixture
def execute(session, channel):
    """Run a cell and return its terminal result message."""
    def run(code, request_id=None):
        if request_id is None:
            run.counter += 1
            request_id = run.counter
        session.execute({"type": "execute", "id": request_id, "code": code})
        return channel.of_type("execute_result")[-1]

    run.counter = 0
    return run
