"""
The message channel a widget's Python half uses to talk to the view drawing it.

The ``comm`` package exists to be replaced. ``comm.create_comm`` and ``comm.get_comm_manager``
are module-level names whose own documentation says a kernel is expected to substitute its own,
and ``BaseComm`` leaves exactly one method abstract: ``publish_msg``, which sends one message to
whatever is on the other side. Filling that in and swapping the two names is the whole of the
Python side. Every widget already asks for a comm and is already registered with a manager; today
it is handed one that silently discards what it is given.

Nothing outside the standard library is imported when this module loads. ``comm`` is reached in
:func:`install` and ``ipywidgets`` in :func:`ensure_targets`, and the absence of either is a
reason to decline rather than to fail, matching the rule the display module follows.
"""

import base64
import json
import sys
import threading


# Frames a widget raises are not answers to a request, and this is the id reserved for a message
# the host sends on its own behalf. Spelled out here rather than imported, so this module stays
# something the entry script imports and never something that imports it back.
UNSOLICITED_REQUEST_ID = 0

# What one comm message may occupy once written, buffers included. The transport underneath is
# newline-delimited JSON with a ceiling that does not degrade: a frame past it raises, which
# fails the connection rather than the message. An array-backed widget is the ordinary way to
# reach that size, so the refusal happens here, where the widget and the trait are still known
# and the session survives to say so.
MAX_MESSAGE_BYTES = 8 * 1024 * 1024

# The two targets a widget front end opens. The first carries one widget model, the second is how
# a newly mounted view asks for the state of every model at once.
WIDGET_TARGET = "jupyter.widget"
WIDGET_CONTROL_TARGET = "jupyter.widget.control"

_writer = None
_reporter = None
_manager = None
_comm_class = None
_targets_registered = False

# The channel whose message is being answered, held per thread. A reply belongs to the view that
# asked for it, and a widget updated from a background thread is answering nobody, so a plain
# global would let one thread address the other thread's message.
_answering = threading.local()


# --- installation -------------------------------------------------------------------------


def install(writer, reporter=None):
    """
    Take over comm creation for this interpreter and return whether it was possible.

    ``writer`` is called with one frame to send. ``reporter`` is called with a line of text when
    a message is refused, and is optional: without one a refusal is silent, which is the right
    behaviour for a caller that has nowhere to write.

    Called before any user code runs, because a widget opens its comm while it is being
    constructed. Substituting later would leave every widget built before then holding the
    discarding stand-in.
    """
    global _writer, _reporter, _manager, _comm_class

    try:
        import comm
        from comm.base_comm import BaseComm, CommManager
    except Exception:
        # No comm package means no widget library either, since one depends on the other.
        return False

    _writer = writer
    _reporter = reporter
    _manager = CommManager()
    _comm_class = _build_comm_class(BaseComm)

    comm.create_comm = _comm_class
    comm.get_comm_manager = _get_manager
    return True


def is_installed():
    return _manager is not None


def reset():
    """Undo :func:`install`. Exists for tests, which run several sessions in one interpreter."""
    global _writer, _reporter, _manager, _comm_class, _targets_registered

    try:
        import comm

        # Put back the package's own implementations rather than something equivalent, so an
        # interpreter that has been reset is indistinguishable from one never installed into.
        comm.create_comm = getattr(comm, "_create_comm", comm.create_comm)
        comm.get_comm_manager = getattr(comm, "_get_comm_manager", comm.get_comm_manager)
    except Exception:
        pass

    _writer = None
    _reporter = None
    _manager = None
    _comm_class = None
    _targets_registered = False


def _get_manager():
    return _manager


def ensure_targets():
    """
    Register the widget targets, once, and report whether they are in place.

    A target is what a front end names when it opens a comm from its side, so this is only
    needed once something is drawing. It is attempted lazily for the reason the whole module is
    lazy: importing ``ipywidgets`` costs a noticeable part of a session's startup and is wasted
    on a session that never shows one.
    """
    global _targets_registered

    if _targets_registered:
        return True
    if _manager is None:
        return False

    try:
        import ipywidgets
    except Exception:
        return False

    try:
        _manager.register_target(WIDGET_TARGET, ipywidgets.Widget.handle_comm_opened)
        _manager.register_target(WIDGET_CONTROL_TARGET, ipywidgets.Widget.handle_control_comm_opened)
    except Exception:
        return False

    _targets_registered = True
    return True


def _build_comm_class(base_comm):
    """
    Assemble the comm class over whichever ``BaseComm`` this environment provides.

    Built here rather than written out at module scope, which would need ``comm`` imported to
    define the class at all and would make this module unimportable in an environment without it.
    """

    class VersoComm(base_comm):
        """A comm whose messages travel over the host's own socket."""

        def __init__(self, *args, **keys):
            super().__init__(*args, **keys)

            # The first widget to be built is the earliest moment an output area can exist,
            # and so the last moment before one could be entered. Imported here rather than
            # at module scope because the display module imports this one.
            try:
                import _versohost_display as display_support

                display_support.install_output_capture()
            except Exception:
                # A widget still works without it; only collecting inside one is lost.
                pass

        def publish_msg(self, msg_type, data=None, metadata=None, buffers=None, **keys):
            frame = {
                "type": "comm",
                "req_id": UNSOLICITED_REQUEST_ID,
                "comm_id": self.comm_id,
                "msg_type": msg_type,
                "data": data,
                "metadata": metadata,
                "buffers": _encode_buffers(buffers),
                "target_name": keys.get("target_name"),
            }

            # Present only when this message answers one that arrived on a channel. Without it
            # the message belongs to the session rather than to any one view, which is what an
            # ordinary trait assignment in a cell produces, and the host decides who sees it.
            channel_id = getattr(_answering, "channel_id", None)
            if channel_id is not None:
                frame["channel_id"] = channel_id

            _send(frame, self.comm_id, data)

    return VersoComm


# --- sending ------------------------------------------------------------------------------


def _send(frame, comm_id, data):
    writer = _writer
    if writer is None:
        return

    try:
        encoded = json.dumps(frame, ensure_ascii=False).encode("utf-8")
    except (TypeError, ValueError) as error:
        _refuse(comm_id, data, "could not be written as JSON (%s)" % error)
        return

    # Written out twice, once to measure and once to send. The measurement is what turns an
    # oversize update into a message the author can act on instead of a connection that fails
    # and takes every widget in the session with it.
    if len(encoded) > MAX_MESSAGE_BYTES:
        _refuse(
            comm_id, data,
            "is %.1f MB, over the %d MB limit on a single widget message"
            % (len(encoded) / (1024.0 * 1024.0), MAX_MESSAGE_BYTES // (1024 * 1024)))
        return

    try:
        writer(frame)
    except Exception:
        # The connection is gone, which the parts of the host that own it already report. A
        # widget update is not the place to raise about it, and raising here would surface
        # inside whatever assigned the trait.
        pass


def _encode_buffers(buffers):
    """
    Binary parts as base64 text, which is what a JSON transport can carry.

    A buffer that cannot be read as bytes is dropped rather than allowed to fail the message:
    losing one part of a widget's state draws it wrongly, and losing the message draws nothing.
    """
    if not buffers:
        return []

    encoded = []
    for buffer in buffers:
        try:
            encoded.append(base64.b64encode(bytes(buffer)).decode("ascii"))
        except Exception:
            continue

    return encoded


def _decode_buffers(buffers):
    if not buffers:
        return []

    decoded = []
    for buffer in buffers:
        if isinstance(buffer, (bytes, bytearray, memoryview)):
            decoded.append(bytes(buffer))
            continue
        try:
            decoded.append(base64.b64decode(buffer))
        except Exception:
            continue

    return decoded


def _refuse(comm_id, data, problem):
    reporter = _reporter
    if reporter is None:
        return

    try:
        reporter(
            "A message from %s %s. The widget was not updated in the browser, "
            "and it is still whatever Python last set it to." % (_describe(comm_id, data), problem))
    except Exception:
        pass


def _describe(comm_id, data):
    """Name the widget and the traits behind a message, as far as either can be established."""
    name = _widget_class_name(comm_id)
    subject = "the '%s' widget" % name if name else "a widget"

    traits = _largest_traits(data)
    if not traits:
        return subject

    return "%s (largest: %s)" % (subject, ", ".join(traits))


def _widget_class_name(comm_id):
    """
    The widget's class, when the library is already loaded and still holds it.

    Read from the registry the library keeps rather than from a lookup of our own: the comm id
    is the model id, so the widget is already findable and a second map would only be another
    thing to keep in step. Every part of it is guarded, because it is a private name that exists
    to make a diagnostic better and never to make one possible.
    """
    module = sys.modules.get("ipywidgets.widgets.widget")
    if module is None:
        return None

    try:
        instances = getattr(module, "_instances", None)
        widget = instances.get(comm_id) if instances is not None else None
        return type(widget).__name__ if widget is not None else None
    except Exception:
        return None


def _largest_traits(data, limit=3):
    """The trait names carrying the most, biggest first, which is what an author has to act on."""
    if not isinstance(data, dict):
        return []

    state = data.get("state")
    if not isinstance(state, dict):
        return []

    sized = []
    for name, held in state.items():
        try:
            sized.append((len(json.dumps(held, ensure_ascii=False, default=str)), name))
        except Exception:
            continue

    sized.sort(reverse=True)
    return [name for _, name in sized[:limit]]


# --- receiving ----------------------------------------------------------------------------


def dispatch(message):
    """
    Hand one inbound comm message to the widget that owns it.

    Called on the main thread and never on the reader thread. Setting a trait runs every
    ``@observe`` callback the author registered, which is user code, and running user code on
    the reader thread would run it concurrently with the user's own cell against objects with
    no lock between them. Queueing instead serialises the two, which is also what a Jupyter
    kernel does with the same messages.
    """
    if _manager is None:
        return

    comm_id = message.get("comm_id")
    if not comm_id:
        return

    # A front end opening a comm names a target, and the target has to be registered by then.
    # This is the first point where something is known to be drawing, so it is the lazy trigger.
    ensure_targets()

    envelope = {
        "content": {
            "comm_id": comm_id,
            "data": message.get("data") or {},
            "target_name": message.get("target_name") or WIDGET_TARGET,
        },
        "metadata": message.get("metadata") or {},
        "buffers": _decode_buffers(message.get("buffers")),
    }

    msg_type = message.get("msg_type") or "comm_msg"
    channel_id = message.get("channel_id")
    _answering.channel_id = channel_id
    try:
        if msg_type == "comm_open":
            _manager.comm_open(None, None, envelope)
        elif msg_type == "comm_close":
            _manager.comm_close(None, None, envelope)
        else:
            _manager.comm_msg(None, None, envelope)
    except Exception as error:
        # The manager already catches what a widget's own handler raises and logs it, so
        # reaching here means the message itself was not something it could route.
        _report("A message for a widget could not be delivered: %s" % error)
    finally:
        _answering.channel_id = None
        _acknowledge(channel_id, comm_id, message.get("msg_id"))


def _acknowledge(channel_id, comm_id, msg_id):
    """
    Tell the view its message has been applied.

    A front end sends one change at a time and holds the rest until it hears that the one in
    flight has landed, merging everything that accumulates meanwhile into a single later
    message. Without this it hears nothing, holds everything after the first, and a widget
    stops reaching Python after one interaction.

    Sent after the message has been handled rather than when it arrived, so a view dragging a
    control while a long cell runs waits for the cell instead of filling the queue in front of
    it. That is the throttle doing what it is for.
    """
    if channel_id is None or msg_id is None:
        return

    writer = _writer
    if writer is None:
        return

    try:
        writer({
            "type": "comm",
            "req_id": UNSOLICITED_REQUEST_ID,
            "channel_id": channel_id,
            "comm_id": comm_id,
            "msg_type": "comm_ack",
            "msg_id": msg_id,
        })
    except Exception:
        # The connection is gone. The view stops being told, which stops it sending, which is
        # the right way round: there is nothing left to send to.
        pass


def _report(text):
    reporter = _reporter
    if reporter is None:
        return
    try:
        reporter(text)
    except Exception:
        pass
