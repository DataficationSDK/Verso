"""Variable exchange between the notebook's shared store and the cell namespace.

Values arrive as ordinary Python objects decoded from the request and are set into the
scope as they are. Going the other way, each name is reduced to something JSON can carry,
under the same depth and cycle limits the in-process kernel applied, with a byte budget so
one enormous value cannot stall a cell or overflow a frame.
"""

import math
import types

# A self-referential value such as globals() would recurse forever, and a value that is
# merely very deep defeats identity tracking because every node is distinct. Both limits
# are needed, and both match what the in-process kernel used.
MAX_DEPTH = 100

DEFAULT_LIMIT_BYTES = 8 * 1024 * 1024

# Rough serialized cost of the scalar forms, used to spend the budget without building the
# JSON text first. Exactness does not matter: the budget is a guard rail, not an accountant.
_NULL_COST = 4
_BOOL_COST = 5
_NUMBER_COST = 24
_CONTAINER_COST = 2
_ITEM_COST = 1


class _TooLarge(Exception):
    """Raised inside a reduction once a value has spent its budget."""


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
            scope[name] = value


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
        return value

    if isinstance(value, float):
        _charge(budget, _NUMBER_COST)
        # JSON has no NaN or infinity. Python's encoder writes bare tokens for them, which
        # the managing side cannot parse, so they travel in their textual form instead.
        return value if math.isfinite(value) else repr(value)

    if isinstance(value, str):
        _charge(budget, len(value) + 2)
        return value

    if depth >= MAX_DEPTH:
        return None

    if isinstance(value, (list, tuple, set, frozenset)):
        return _reduce_sequence(value, depth, seen, budget)

    if isinstance(value, dict):
        return _reduce_mapping(value, depth, seen, budget)

    return _fallback(value, budget)


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
