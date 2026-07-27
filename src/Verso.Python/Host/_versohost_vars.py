"""Variable exchange between the notebook's shared store and the cell namespace.

Values arrive as ordinary Python objects decoded from the request and are set into the
scope as they are. Going the other way, each name is reduced to something JSON can carry,
under the same depth and cycle limits the in-process kernel applied, with a byte budget so
one enormous value cannot stall a cell or overflow a frame.
"""

import base64
import datetime
import decimal
import math
import sys
import types
import uuid

import _versohost_wiretypes as wiretypes

# A self-referential value such as globals() would recurse forever, and a value that is
# merely very deep defeats identity tracking because every node is distinct. Both limits
# are needed, and both match what the in-process kernel used.
MAX_DEPTH = 100

DEFAULT_LIMIT_BYTES = 8 * 1024 * 1024

# Where .NET's own integer range ends, past which a value travels as digits rather than a number.
_INT64_BOUND = 2 ** 63

# Rough serialized cost of the scalar forms, used to spend the budget without building the
# JSON text first. Exactness does not matter: the budget is a guard rail, not an accountant.
_NULL_COST = 4
_BOOL_COST = 5
_NUMBER_COST = 24
_CONTAINER_COST = 2
_ITEM_COST = 1


class _TooLarge(Exception):
    """Raised inside a reduction once a value has spent its budget."""


# What was injected, by name, as (the object placed in the scope, the wire value it came from).
# Holding the object is what lets a later cell tell "still exactly what we were given" from
# "replaced by something the cell computed", without reducing the value again to find out.
_injected = {}


def apply_inject(scope, values):
    """
    Set store values into the scope.

    The managing side filters these, and the same rule is applied again here: a double
    underscore prefix covers the names the interpreter owns and the session keys Verso
    itself writes. ``__builtins__`` is how a cell reaches every builtin there is, so a store
    entry by that name must not be able to take it away. A single underscore is left alone,
    because other languages use it for ordinary variables and one of those arriving here is
    a variable a cell asked for.
    """
    if not isinstance(values, dict):
        return

    for name, value in values.items():
        if isinstance(name, str) and name and not name.startswith("__"):
            hydrated = wiretypes.hydrate(value, name)
            scope[name] = hydrated
            _injected[name] = (hydrated, value)


def forget_injected():
    """Drop what is remembered about injected values, for a scope that has been reset."""
    _injected.clear()


def collect(scope, limit_bytes=DEFAULT_LIMIT_BYTES, total_limit_bytes=None):
    """
    Reduce the publishable names in a scope. Returns the reduced mapping and the names that
    were replaced by a descriptor because they did not fit.
    """
    if not limit_bytes or limit_bytes <= 0:
        limit_bytes = DEFAULT_LIMIT_BYTES
    if total_limit_bytes is None:
        # Several large-but-legal values must still leave the frame comfortably inside the
        # transport ceiling, so the run as a whole is bounded as well as each value.
        total_limit_bytes = limit_bytes * 4

    variables = {}
    oversized = []
    remaining = total_limit_bytes

    for name in list(scope.keys()):
        value = scope.get(name, None)
        if not is_publishable(name, value):
            continue

        # Still the object we were handed, so the cell did not produce it and the store already
        # holds the original in the type it was written in. Reporting the reduced form instead
        # would replace that with whatever JSON could describe: an int written in F# would come
        # back a long, and a later typed read of the name would find nothing. Sending back what
        # arrived, unchanged, is what lets the other side recognize it and leave the store alone.
        remembered = _injected.get(name)
        if remembered is not None and remembered[0] is value:
            variables[name] = remembered[1]
            continue

        allowance = min(limit_bytes, remaining)
        budget = [allowance]
        try:
            variables[name] = _reduce(value, 0, set(), budget)
        except _TooLarge:
            variables[name] = _descriptor(value, limit_bytes, allowance >= limit_bytes)
            oversized.append(name)
            continue

        remaining -= allowance - budget[0]

    return variables, oversized


def is_publishable(name, value):
    """Non-underscore, non-module, non-callable names, matching the in-process kernel."""
    if not isinstance(name, str) or not name or name.startswith("_"):
        return False
    if value is None:
        return False
    # A stand-in for something that never arrived has nothing to send back, and its repr
    # deliberately does not raise, so without this it would publish over the real value.
    if isinstance(value, wiretypes.VersoUnavailable):
        return False
    if isinstance(value, types.ModuleType):
        return False
    if callable(value):
        return False
    return True


def _reduce(value, depth, seen, budget):
    if value is None:
        _charge(budget, _NULL_COST)
        return None

    # Checked before int because bool is a subclass of it.
    if isinstance(value, bool):
        _charge(budget, _BOOL_COST)
        return value

    if isinstance(value, int):
        _charge(budget, _NUMBER_COST)
        # Past the range .NET can hold as an integer, digits carry it exactly where a JSON
        # number would be read back through a double and quietly lose its low end.
        if -_INT64_BOUND <= value < _INT64_BOUND:
            return value
        return wiretypes.tag(wiretypes.TAG_BIGINT, str(value))

    if isinstance(value, float):
        _charge(budget, _NUMBER_COST)
        # JSON has no NaN or infinity. Python's encoder writes bare tokens for them, which
        # the managing side cannot parse, so they travel tagged instead.
        if math.isfinite(value):
            return value
        if math.isnan(value):
            return wiretypes.tag(wiretypes.TAG_FLOAT, "NaN")
        return wiretypes.tag(
            wiretypes.TAG_FLOAT, "Infinity" if value > 0 else "-Infinity")

    if isinstance(value, str):
        _charge(budget, len(value) + 2)
        return value

    # Pandas' own missing values answer isinstance checks for the types they stand in for, so a
    # missing date would otherwise be asked to format itself. Absent is what they mean.
    if _is_missing(value):
        _charge(budget, _NULL_COST)
        return None

    # Checked before date, which datetime subclasses, and before the containers, since none of
    # these are one. Each keeps its own kind rather than arriving as the text it prints as.
    if isinstance(value, datetime.datetime):
        _charge(budget, _NUMBER_COST + _CONTAINER_COST)
        name = (wiretypes.TAG_DATETIMEOFFSET if value.tzinfo is not None
                else wiretypes.TAG_DATETIME)
        return wiretypes.tag(name, value.isoformat(timespec="microseconds"))

    if isinstance(value, datetime.date):
        _charge(budget, _NUMBER_COST + _CONTAINER_COST)
        return wiretypes.tag(wiretypes.TAG_DATE, value.isoformat())

    if isinstance(value, datetime.time):
        _charge(budget, _NUMBER_COST + _CONTAINER_COST)
        return wiretypes.tag(wiretypes.TAG_TIME, value.isoformat(timespec="microseconds"))

    if isinstance(value, datetime.timedelta):
        _charge(budget, _NUMBER_COST + _CONTAINER_COST)
        return wiretypes.tag(wiretypes.TAG_TIMESPAN, value.total_seconds())

    if isinstance(value, decimal.Decimal):
        _charge(budget, _NUMBER_COST + _CONTAINER_COST)
        return wiretypes.tag(wiretypes.TAG_DECIMAL, str(value))

    if isinstance(value, uuid.UUID):
        _charge(budget, _NUMBER_COST + _CONTAINER_COST)
        return wiretypes.tag(wiretypes.TAG_GUID, str(value))

    if isinstance(value, (bytes, bytearray)):
        encoded = base64.b64encode(bytes(value)).decode("ascii")
        _charge(budget, len(encoded) + _CONTAINER_COST)
        return wiretypes.tag(wiretypes.TAG_BYTES, encoded)

    if depth >= MAX_DEPTH:
        return None

    if isinstance(value, (list, tuple, set, frozenset)):
        return _reduce_sequence(value, depth, seen, budget)

    if isinstance(value, dict):
        return _reduce_mapping(value, depth, seen, budget)

    plain, recognized = _to_plain(value)
    if recognized:
        return _reduce(plain, depth, seen, budget)

    return _fallback(value, budget)


def _is_missing(value):
    """Whether a value is one of pandas' stand-ins for a value that is not there."""
    pandas = sys.modules.get("pandas")
    if pandas is None:
        return False
    return value is getattr(pandas, "NaT", None) or value is getattr(pandas, "NA", None)


def _to_plain(value):
    """
    Reduce an array or frame to the ordinary Python shapes, when those libraries are in use.

    Recognized by asking for the module that is already imported rather than importing one, so a
    notebook that never loads them pays nothing and one that does needs no configuring.
    """
    numpy = sys.modules.get("numpy")
    if numpy is not None:
        if isinstance(value, numpy.ndarray):
            return value.tolist(), True
        if isinstance(value, numpy.generic):
            kind = getattr(getattr(value, "dtype", None), "kind", "")
            if kind in ("M", "m"):
                # A date or duration held in nanoseconds answers item() with a count of them
                # rather than with a date, so it is narrowed to microseconds first. Microseconds
                # are as fine as Python's own datetime goes.
                try:
                    unit = "datetime64[us]" if kind == "M" else "timedelta64[us]"
                    return value.astype(unit).item(), True
                except Exception:
                    return value.item(), True
            return value.item(), True

    pandas = sys.modules.get("pandas")
    if pandas is not None:
        if isinstance(value, pandas.DataFrame):
            # Rows of named cells, which is the shape a table is read back from.
            return value.to_dict(orient="records"), True
        if isinstance(value, (pandas.Series, pandas.Index)):
            return value.tolist(), True

    return None, False


def _reduce_sequence(value, depth, seen, budget):
    identity = id(value)
    if identity in seen:
        return None

    _charge(budget, _CONTAINER_COST)
    seen.add(identity)
    try:
        reduced = []
        for item in value:
            _charge(budget, _ITEM_COST)
            reduced.append(_reduce(item, depth + 1, seen, budget))
        return reduced
    finally:
        seen.discard(identity)


def _reduce_mapping(value, depth, seen, budget):
    identity = id(value)
    if identity in seen:
        return None

    _charge(budget, _CONTAINER_COST)
    seen.add(identity)
    try:
        reduced = {}
        for key, item in value.items():
            # JSON keys are strings; a non-string key takes the form it prints as, which
            # is what the in-process kernel produced too.
            name = key if isinstance(key, str) else _safe_str(key)
            _charge(budget, len(name) + _ITEM_COST + 2)
            reduced[name] = _reduce(item, depth + 1, seen, budget)
        return reduced
    finally:
        seen.discard(identity)


def _fallback(value, budget):
    """Anything JSON cannot describe publishes the way it prints."""
    text = _safe_repr(value)
    _charge(budget, len(text) + 2)
    return text


def _charge(budget, amount):
    budget[0] -= amount
    if budget[0] < 0:
        raise _TooLarge()


def _descriptor(value, limit_bytes, hit_own_limit):
    kind = type(value).__name__
    if hit_own_limit:
        return "<%s, over the %s limit, not shared>" % (kind, format_bytes(limit_bytes))
    return "<%s, publish size limit reached, not shared>" % kind


def format_bytes(count):
    size = float(count)
    for unit in ("bytes", "KB", "MB", "GB"):
        if size < 1024 or unit == "GB":
            if unit == "bytes":
                return "%d bytes" % int(size)
            if size == int(size):
                return "%d %s" % (int(size), unit)
            return "%.1f %s" % (size, unit)
        size /= 1024.0
    return "%d bytes" % count


def _safe_repr(value):
    try:
        return repr(value)
    except Exception:
        return "<unprintable %s object>" % type(value).__name__


def _safe_str(value):
    try:
        return str(value)
    except Exception:
        return "<unprintable %s key>" % type(value).__name__
