"""
Covers the comm channel a widget's Python half talks over.

Two of these need a real ipywidgets and skip themselves without one, the same way the widget
tests in test_display.py do. The rest exercise the module against the ``comm`` package alone,
which is the smaller dependency and the one that decides whether any of this is reachable.
"""

import base64
import json
import sys
import threading

import pytest

import _versohost_comm as comm_support
import versohost


class Wired:
    """An installed comm module, with everything it wrote and everything it refused."""

    def __init__(self, frames, reports):
        self.frames = frames
        self.reports = reports

    def of_type(self, msg_type):
        return [f for f in self.frames if f.get("msg_type") == msg_type]


@pytest.fixture
def wired():
    pytest.importorskip("comm")

    frames = []
    reports = []
    assert comm_support.install(frames.append, reports.append)

    yield Wired(frames, reports)

    # Widgets built during a test hold a comm registered with the manager this installed, and
    # closing them afterwards is what keeps them from reaching for it once it has been replaced.
    widgets = sys.modules.get("ipywidgets")
    if widgets is not None:
        try:
            widgets.Widget.close_all()
        except Exception:
            pass

    comm_support.reset()


def new_comm(**kwargs):
    """Open a comm the way a widget does, through the substituted module-level name."""
    import comm

    kwargs.setdefault("target_name", comm_support.WIDGET_TARGET)
    return comm.create_comm(**kwargs)


# --- what a widget sends ------------------------------------------------------------------


def test_opening_a_comm_writes_a_frame_the_host_can_route(wired):
    opened = new_comm(data={"state": {"value": 3}}, metadata={"version": "2.1.0"})

    assert len(wired.frames) == 1
    frame = wired.frames[0]

    assert frame["type"] == "comm"
    assert frame["req_id"] == comm_support.UNSOLICITED_REQUEST_ID
    assert frame["comm_id"] == opened.comm_id
    assert frame["msg_type"] == "comm_open"
    assert frame["data"] == {"state": {"value": 3}}
    assert frame["metadata"] == {"version": "2.1.0"}
    assert frame["target_name"] == comm_support.WIDGET_TARGET
    assert frame["buffers"] == []


def test_a_message_raised_between_cells_names_no_channel(wired):
    """
    Nothing addresses it, so nothing in the frame does either.

    A trait assigned in a cell belongs to the widget rather than to any one view of it, and the
    host is the part that knows which views exist. Naming a channel here would be a guess.
    """
    opened = new_comm()
    wired.frames.clear()

    opened.send({"method": "update", "state": {"value": 9}})

    assert "channel_id" not in wired.frames[0]


def test_an_answer_carries_the_channel_of_the_message_it_answers(wired):
    opened = new_comm()
    opened.on_msg(lambda msg: opened.send({"answered": msg["content"]["data"]}))
    wired.frames.clear()

    comm_support.dispatch({
        "type": "comm_msg",
        "channel_id": "channel-7",
        "comm_id": opened.comm_id,
        "data": {"asked": True},
    })

    assert wired.frames[0]["channel_id"] == "channel-7"
    assert wired.frames[0]["data"] == {"answered": {"asked": True}}


def test_a_reply_stops_being_addressed_once_the_message_is_answered(wired):
    opened = new_comm()
    opened.on_msg(lambda msg: None)

    comm_support.dispatch({
        "comm_id": opened.comm_id, "channel_id": "channel-7", "data": {},
    })
    wired.frames.clear()

    # Nothing is being answered now, so this is an ordinary unsolicited update again.
    opened.send({"method": "update"})

    assert "channel_id" not in wired.frames[0]


def test_binary_parts_travel_as_base64(wired):
    payload = bytes(range(256))
    opened = new_comm()
    wired.frames.clear()

    opened.send({"method": "update"}, buffers=[payload, memoryview(b"second")])

    buffers = wired.frames[0]["buffers"]
    assert [base64.b64decode(b) for b in buffers] == [payload, b"second"]

    # The whole frame has to survive json.dumps, which is what the transport does to it.
    json.dumps(wired.frames[0])


# --- what the host refuses to send --------------------------------------------------------


def test_an_oversize_message_is_refused_and_names_what_caused_it(wired):
    opened = new_comm()
    wired.frames.clear()

    # Past the limit on its own, so the refusal cannot be an accumulation of anything else.
    opened.send({
        "method": "update",
        "state": {
            "description": "small",
            "mesh": "x" * (comm_support.MAX_MESSAGE_BYTES + 1),
        },
    })

    assert wired.frames == [], "an oversize message was written to the transport anyway"
    assert len(wired.reports) == 1

    report = wired.reports[0]
    assert "mesh" in report, report
    assert "MB" in report, report


def test_a_refused_message_leaves_the_session_usable(wired):
    opened = new_comm()
    opened.send({"state": {"big": "x" * (comm_support.MAX_MESSAGE_BYTES + 1)}})
    wired.frames.clear()

    opened.send({"method": "update", "state": {"value": 1}})

    assert len(wired.frames) == 1
    assert wired.frames[0]["data"]["state"] == {"value": 1}


def test_a_message_that_cannot_be_written_as_json_is_refused_rather_than_raised(wired):
    opened = new_comm()
    wired.frames.clear()

    opened.send({"method": "update", "state": {"value": object()}})

    assert wired.frames == []
    assert len(wired.reports) == 1


# --- what arrives ---------------------------------------------------------------------------


def test_an_inbound_message_reaches_the_comm_that_owns_it(wired):
    received = []
    opened = new_comm()
    opened.on_msg(lambda msg: received.append((msg["content"]["data"], msg["buffers"])))

    comm_support.dispatch({
        "comm_id": opened.comm_id,
        "data": {"method": "update", "state": {"value": 12}},
        "buffers": [base64.b64encode(b"payload").decode("ascii")],
    })

    assert received == [({"method": "update", "state": {"value": 12}}, [b"payload"])]


def test_a_message_for_a_comm_nobody_opened_is_ignored(wired):
    comm_support.dispatch({"comm_id": "never-opened", "data": {"method": "update"}})

    # Nothing to assert beyond having returned: the manager logs the miss and moves on, and the
    # ordinary way to get one is a view answering a moment after its widget went away.
    assert wired.frames == []


def test_a_message_with_no_comm_is_ignored(wired):
    comm_support.dispatch({"channel_id": "channel-7", "data": {"method": "update"}})

    assert wired.frames == []


def test_a_view_closing_a_comm_closes_the_python_side(wired):
    closed = []
    opened = new_comm()
    opened.on_close(lambda msg: closed.append(msg))
    wired.frames.clear()

    comm_support.dispatch({
        "comm_id": opened.comm_id, "msg_type": "comm_close", "data": {},
    })

    assert len(closed) == 1


# --- the widget library ---------------------------------------------------------------------


def test_targets_are_declined_rather_than_raised_without_ipywidgets(wired, monkeypatch):
    monkeypatch.setitem(sys.modules, "ipywidgets", None)

    assert comm_support.ensure_targets() is False

    # And the substitution itself still stands, so a session in an environment with no widget
    # library carries on exactly as it did before.
    assert comm_support.is_installed()


def test_an_inbound_update_sets_a_trait_and_runs_the_authors_callback(wired):
    ipywidgets = pytest.importorskip("ipywidgets")

    assert comm_support.ensure_targets()

    seen = []
    slider = ipywidgets.IntSlider(value=3)
    slider.observe(lambda change: seen.append(change["new"]), names="value")

    comm_support.dispatch({
        "comm_id": slider.comm.comm_id,
        "channel_id": "channel-7",
        "data": {"method": "update", "state": {"value": 42}},
    })

    assert slider.value == 42
    assert seen == [42]


def test_a_trait_set_in_python_is_written_to_the_transport(wired):
    ipywidgets = pytest.importorskip("ipywidgets")

    slider = ipywidgets.IntSlider(value=3)
    wired.frames.clear()

    slider.value = 8

    updates = [f for f in wired.of_type("comm_msg") if f["comm_id"] == slider.comm.comm_id]
    assert len(updates) == 1
    assert updates[0]["data"]["state"] == {"value": 8}


# --- where an inbound message is handled -----------------------------------------------------


class ScriptedChannel:
    """
    Feeds the dispatch loop a fixed sequence and records what it writes.

    Reads are handed out in order and the last one blocks, which is what a socket that has not
    been closed does and what keeps the reader thread from running past the end of the script.
    """

    def __init__(self, messages):
        self._messages = list(messages)
        self._index = 0
        self._exhausted = threading.Event()
        self.written = []

    def read(self):
        if self._index >= len(self._messages):
            self._exhausted.set()
            return None

        message = self._messages[self._index]
        self._index += 1

        # An entry can be a callable, which is how the script waits for something the main
        # thread does before queueing what comes next.
        if callable(message):
            return message()

        return message

    def write(self, message):
        self.written.append(message)


def test_a_message_arriving_during_a_cell_is_applied_after_it_once_and_in_order(monkeypatch):
    order = []
    started = threading.Event()
    released = threading.Event()

    monkeypatch.setattr(
        comm_support, "dispatch", lambda message: order.append(message["comm_id"]))

    def release_the_cell():
        # Both comm messages are on the queue by the time this runs, and the cell is still
        # inside the wait below, so they arrived during it rather than merely near it.
        started.wait(5)
        released.set()
        return {"type": "shutdown"}

    channel = ScriptedChannel([
        {"type": "execute", "id": 1,
         "code": "_started.set()\n_released.wait(5)\n_order.append('cell')\n"},
        {"type": "comm_msg", "comm_id": "first", "data": {}},
        {"type": "comm_msg", "comm_id": "second", "data": {}},
        release_the_cell,
    ])

    session = versohost.HostSession(channel)
    session.scope["_started"] = started
    session.scope["_released"] = released
    session.scope["_order"] = order

    try:
        versohost._run(channel, session)
    finally:
        session.close()

    assert started.is_set(), "the cell never ran"
    assert order == ["cell", "first", "second"]
