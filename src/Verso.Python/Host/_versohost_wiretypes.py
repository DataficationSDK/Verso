"""The Python side of the shared variable wire format.

Values that JSON has no form of its own for arrive tagged, and are rebuilt here into the
Python types a cell would expect: a real ``datetime`` for a date, a ``Decimal`` that kept its
exactness, a mapping that answers both ``row["Time"]`` and ``row.Time``.

The wrappers subclass the standard types rather than imitating them, so ``isinstance`` checks,
arithmetic, and libraries that infer a column type from the values all behave as they would for
anything else. What the wrappers add is the naming a notebook written against the previous
in-process kernel already used, so that such a notebook keeps running unchanged.
"""

import base64
import datetime
import decimal
import uuid

TYPE_KEY = "__verso_type__"
VALUE_KEY = "__verso_value__"

TAG_DATETIME = "datetime"
TAG_DATETIMEOFFSET = "datetimeoffset"
TAG_DATE = "date"
TAG_TIME = "time"
TAG_TIMESPAN = "timespan"
TAG_DECIMAL = "decimal"
TAG_GUID = "guid"
TAG_BIGINT = "bigint"
TAG_BYTES = "bytes"
TAG_FLOAT = "float"
TAG_UNAVAILABLE = "unavailable"

# Matches the depth limit both sides apply to a single value.
MAX_DEPTH = 100


class VersoUnavailableError(RuntimeError):
    """Raised when a cell uses a variable that could not be shared with Python."""


class VersoUnavailable(object):
    """
    Stands where a variable that could not cross would have been.

    Printing it explains itself and never raises, so looking at the name tells you what happened.
    Using it for anything else raises, because a quiet stand-in that answered like a real value
    would be worse than the absence it replaces.
    """

    __slots__ = ("_verso_name", "_verso_reason")

    def __init__(self, name, reason):
        object.__setattr__(self, "_verso_name", name or "The value")
        object.__setattr__(self, "_verso_reason", reason or "it has no JSON form")

    def __repr__(self):
        return "<%s was not shared with Python: %s>" % (self._verso_name, self._verso_reason)

    __str__ = __repr__

    def _unavailable(self):
        return VersoUnavailableError(
            "%s was not shared with Python because it is %s. It is still available to the "
            "cells in the language that produced it." % (self._verso_name, self._verso_reason))

    # Every ordinary use raises. Dunder lookups skip __getattr__, so each has to be named.
    def __getattr__(self, name):
        raise self._unavailable()

    def __setattr__(self, name, value):
        raise self._unavailable()

    def __getitem__(self, key):
        raise self._unavailable()

    def __setitem__(self, key, value):
        raise self._unavailable()

    def __iter__(self):
        raise self._unavailable()

    def __len__(self):
        raise self._unavailable()

    def __call__(self, *args, **kwargs):
        raise self._unavailable()

    def __bool__(self):
        raise self._unavailable()

    # Equality is deliberately left alone. It is used by tooling that merely inspects a scope,
    # and raising there would break reading the namespace rather than using the value.


class VersoRecord(dict):
    """
    A mapping that also answers attribute access.

    An object from another language has named fields, and both spellings are natural to reach
    them by: ``row["Time"]`` is what the value is, and ``row.Time`` is what the field was called
    where it was written. Being a real dict is what lets it go straight into a DataFrame.
    """

    __slots__ = ()

    def __getattr__(self, name):
        try:
            return self[name]
        except KeyError:
            raise AttributeError(name)

    def __setattr__(self, name, value):
        self[name] = value

    def __delattr__(self, name):
        try:
            del self[name]
        except KeyError:
            raise AttributeError(name)

    def __dir__(self):
        # Without this the completion list would be dict's own methods and none of the fields,
        # because dir() cannot see what __getattr__ resolves. The editor reads the live object.
        names = list(super(VersoRecord, self).__dir__())
        names.extend(key for key in self.keys() if isinstance(key, str))
        return names


class _PascalCaseMixin(object):
    """The names the previous in-process kernel exposed, kept so existing cells still run."""

    __slots__ = ()

    @property
    def Year(self):
        return self.year

    @property
    def Month(self):
        return self.month

    @property
    def Day(self):
        return self.day

    @property
    def DayOfWeek(self):
        # Sunday is 0 in .NET; Monday is 0 in Python.
        return (self.weekday() + 1) % 7


class _ClockMixin(object):
    __slots__ = ()

    @property
    def Hour(self):
        return self.hour

    @property
    def Minute(self):
        return self.minute

    @property
    def Second(self):
        return self.second

    @property
    def Millisecond(self):
        # .NET counts whole milliseconds, 0 to 999, where Python counts microseconds.
        return self.microsecond // 1000


class VersoDateTime(datetime.datetime, _PascalCaseMixin, _ClockMixin):
    __slots__ = ()

    @classmethod
    def wrap(cls, value):
        return cls(
            value.year, value.month, value.day, value.hour, value.minute,
            value.second, value.microsecond, value.tzinfo)


class VersoDate(datetime.date, _PascalCaseMixin):
    __slots__ = ()

    @classmethod
    def wrap(cls, value):
        return cls(value.year, value.month, value.day)


class VersoTime(datetime.time, _ClockMixin):
    __slots__ = ()

    @classmethod
    def wrap(cls, value):
        return cls(value.hour, value.minute, value.second, value.microsecond, value.tzinfo)


def is_tagged(value):
    """Whether a decoded value is the two-key envelope rather than an ordinary mapping."""
    return (
        isinstance(value, dict)
        and len(value) == 2
        and TYPE_KEY in value
        and VALUE_KEY in value
    )


def hydrate(value, name=None, depth=0):
    """Rebuild wire values into Python types, leaving anything untagged as it arrived."""
    if depth >= MAX_DEPTH:
        return value

    if isinstance(value, dict):
        if is_tagged(value):
            return _decode(value.get(TYPE_KEY), value.get(VALUE_KEY), name)
        return VersoRecord(
            (key, hydrate(item, name, depth + 1)) for key, item in value.items())

    if isinstance(value, list):
        return [hydrate(item, name, depth + 1) for item in value]

    return value


def _decode(tag, value, name):
    try:
        if tag == TAG_UNAVAILABLE:
            return VersoUnavailable(name, value)
        if tag == TAG_DATETIME or tag == TAG_DATETIMEOFFSET:
            return VersoDateTime.wrap(_parse_datetime(value))
        if tag == TAG_DATE:
            return VersoDate.wrap(_parse_datetime(value).date())
        if tag == TAG_TIME:
            return VersoTime.wrap(_parse_datetime("1970-01-01T" + value).timetz())
        if tag == TAG_TIMESPAN:
            return datetime.timedelta(seconds=float(value))
        if tag == TAG_DECIMAL:
            return decimal.Decimal(value)
        if tag == TAG_GUID:
            return uuid.UUID(value)
        if tag == TAG_BIGINT:
            return int(value)
        if tag == TAG_BYTES:
            return base64.b64decode(value)
        if tag == TAG_FLOAT:
            return float(value)
    except Exception:
        # A value that will not rebuild is still worth more as its text than as an error that
        # stops the cell before it starts.
        return value

    # A tag this host does not know comes from a newer peer. Its plain value is the best
    # available reading of it, and is better than refusing to run.
    return value


def _parse_datetime(text):
    try:
        return datetime.datetime.fromisoformat(text)
    except ValueError:
        # fromisoformat accepted a narrower grammar before 3.11. The wire format is chosen to
        # stay inside it, but parsing explicitly costs little and removes the dependency.
        for pattern in ("%Y-%m-%dT%H:%M:%S.%f%z", "%Y-%m-%dT%H:%M:%S.%f", "%Y-%m-%dT%H:%M:%S"):
            try:
                return datetime.datetime.strptime(text, pattern)
            except ValueError:
                continue
        raise


def tag(name, value):
    """Build the envelope for a value travelling back to the store."""
    return {TYPE_KEY: name, VALUE_KEY: value}
