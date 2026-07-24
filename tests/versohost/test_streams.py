"""Output buffering, coalescing, and request attribution."""

import io
import sys

import pytest

import versohost


@pytest.fixture
def router(channel):
    created = versohost.OutputRouter(channel)
    yield created
    created.stop()


def test_nothing_is_sent_until_a_flush(router, channel):
    router.write("stdout", "buffered")

    assert channel.messages == []


def test_a_flush_emits_what_was_buffered(router, channel):
    router.write("stdout", "hello")
    router.flush()

    assert channel.of_type("stream") == [
        {"type": "stream", "req_id": 0, "name": "stdout", "text": "hello"}
    ]


def test_consecutive_writes_to_one_stream_coalesce(router, channel):
    router.write("stdout", "one ")
    router.write("stdout", "two ")
    router.write("stdout", "three")
    router.flush()

    # One event rather than three keeps a chatty loop from flooding the notebook with blocks.
    assert len(channel.of_type("stream")) == 1
    assert channel.stream_text() == "one two three"


def test_interleaved_streams_keep_their_order(router, channel):
    router.write("stdout", "first")
    router.write("stderr", "second")
    router.write("stdout", "third")
    router.flush()

    events = channel.of_type("stream")
    assert [(e["name"], e["text"]) for e in events] == [
        ("stdout", "first"),
        ("stderr", "second"),
        ("stdout", "third"),
    ]


def test_a_large_write_flushes_without_waiting(router, channel):
    router.write("stdout", "x" * (versohost.FLUSH_THRESHOLD_CHARS + 1))

    # Waiting for the timer would hold a big write for no reason and grow the buffer unbounded.
    assert channel.stream_text() == "x" * (versohost.FLUSH_THRESHOLD_CHARS + 1)


def test_an_empty_write_produces_nothing(router, channel):
    router.write("stdout", "")
    router.flush()

    assert channel.messages == []


def test_output_is_attributed_to_the_active_request(router, channel):
    router.set_request(7)
    router.write("stdout", "during")
    router.flush()

    assert channel.of_type("stream")[0]["req_id"] == 7


def test_changing_request_flushes_the_previous_one(router, channel):
    router.set_request(1)
    router.write("stdout", "first cell")
    router.set_request(2)
    router.write("stdout", "second cell")
    router.flush()

    events = channel.of_type("stream")
    assert [(e["req_id"], e["text"]) for e in events] == [
        (1, "first cell"),
        (2, "second cell"),
    ]


def test_a_dead_channel_does_not_break_the_writer(router):
    class Broken:
        def write(self, message):
            raise OSError("connection reset")

    broken = versohost.OutputRouter(Broken())
    broken.write("stdout", "anything")

    # User code printing after the host has gone must not raise inside their cell.
    broken.flush()


# --- the file-like surface user code sees ---

def test_writer_reports_the_character_count(router):
    writer = versohost.StreamWriter(router, "stdout")

    assert writer.write("hello") == 5


def test_writer_rejects_non_text(router):
    writer = versohost.StreamWriter(router, "stdout")

    with pytest.raises(TypeError):
        writer.write(b"bytes")


def test_writer_is_not_a_terminal(router):
    # Progress bars check this to decide whether to emit cursor control sequences.
    assert versohost.StreamWriter(router, "stderr").isatty() is False


def test_writer_has_no_file_descriptor(router):
    # The real descriptor belongs to the raw pipe, which is read separately and is not correlated.
    with pytest.raises(io.UnsupportedOperation):
        versohost.StreamWriter(router, "stdout").fileno()


def test_writelines_sends_every_line(router, channel):
    versohost.StreamWriter(router, "stdout").writelines(["a\n", "b\n"])
    router.flush()

    assert channel.stream_text() == "a\nb\n"


def test_print_reaches_the_channel(router, channel):
    writer = versohost.StreamWriter(router, "stdout")
    original = sys.stdout
    sys.stdout = writer
    try:
        print("from print")
    finally:
        sys.stdout = original

    router.flush()
    assert channel.stream_text() == "from print\n"
