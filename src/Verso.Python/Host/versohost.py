"""Verso Python host.

Runs the user's Python interpreter as a subprocess of the Verso kernel and speaks a
small newline-delimited JSON protocol over a loopback socket. This module establishes
the connection and lifecycle; execution, display, and completion handling layer on top
of it. It depends only on the standard library and targets CPython 3.8 and newer.
"""

import argparse
import json
import os
import platform
import queue
import socket
import sys
import threading
import traceback

PROTOCOL_VERSION = 1
MAX_FRAME_BYTES = 64 * 1024 * 1024
_RECV_CHUNK = 65536


class SocketClosed(Exception):
    """Raised when the peer closes the connection mid-frame."""


class ProtocolError(Exception):
    """Raised when a frame violates the wire contract."""


class FrameChannel:
    """Reads and writes newline-delimited JSON frames over a socket."""

    def __init__(self, sock):
        self._sock = sock
        self._buffer = bytearray()
        self._send_lock = threading.Lock()

    def read(self):
        """Read one message. Return a dict, or None at a clean end of stream."""
        while True:
            newline = self._buffer.find(b"\n")
            if newline != -1:
                line = bytes(self._buffer[:newline])
                del self._buffer[: newline + 1]
                if len(line) > MAX_FRAME_BYTES:
                    raise ProtocolError("incoming frame exceeds the size limit")
                return json.loads(line.decode("utf-8"))

            if len(self._buffer) > MAX_FRAME_BYTES:
                raise ProtocolError("incoming frame exceeds the size limit")

            chunk = self._sock.recv(_RECV_CHUNK)
            if not chunk:
                if self._buffer:
                    raise SocketClosed("connection closed mid-frame")
                return None
            self._buffer.extend(chunk)

    def write(self, message):
        """Serialize and send one message as a single frame."""
        data = json.dumps(message, ensure_ascii=False).encode("utf-8")
        if len(data) > MAX_FRAME_BYTES:
            raise ProtocolError("outgoing frame exceeds the size limit")
        frame = data + b"\n"
        with self._send_lock:
            self._sock.sendall(frame)


def _parse_endpoint(value):
    host, _, port = value.rpartition(":")
    if not host or not port:
        raise ValueError("expected host:port for --connect")
    return host, int(port)


def _hello_payload(token):
    return {
        "type": "hello",
        "token": token,
        "protocol": PROTOCOL_VERSION,
        "python": {
            "version": platform.python_version(),
            "executable": sys.executable,
            "implementation": platform.python_implementation(),
        },
        "capabilities": [],
    }


def _run(channel):
    """Feed inbound messages onto a queue and dispatch them on the main thread."""
    inbox = queue.Queue()

    def reader():
        try:
            while True:
                message = channel.read()
                if message is None:
                    break
                inbox.put(message)
        except (OSError, SocketClosed, ProtocolError, ValueError):
            pass
        finally:
            inbox.put(None)  # sentinel: stream ended

    reader_thread = threading.Thread(target=reader, name="verso-host-reader", daemon=True)
    reader_thread.start()

    while True:
        message = inbox.get()
        if message is None:
            break
        if message.get("type") == "shutdown":
            break
        # Other message types are handled by later layers; ignore them here.


def main(argv=None):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--connect", required=True)
    args, _ = parser.parse_known_args(argv)

    token = os.environ.get("VERSO_PYHOST_TOKEN", "")
    host, port = _parse_endpoint(args.connect)

    sock = socket.create_connection((host, port))
    try:
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        channel = FrameChannel(sock)

        # Authenticate and exchange bootstrap configuration before entering the loop.
        channel.write(_hello_payload(token))
        hello_ok = channel.read()
        if hello_ok is None or hello_ok.get("type") != "hello_ok":
            # The peer rejected the handshake or dropped the connection.
            return 0

        _run(channel)
    finally:
        try:
            sock.close()
        except OSError:
            pass
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:
        traceback.print_exc()
        sys.exit(1)
