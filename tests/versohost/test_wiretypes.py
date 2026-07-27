"""Coverage for the values that cross the boundary as something other than plain JSON."""

import datetime
import decimal
import uuid

import pytest

import _versohost_vars as variable_support
import _versohost_wiretypes as wiretypes


# --- records ---

def test_a_record_answers_both_spellings():
    row = wiretypes.hydrate({"Time": 1, "Price": 2})

    assert row.Time == 1
    assert row["Price"] == 2
    assert dict(row) == {"Time": 1, "Price": 2}


def test_a_record_offers_its_fields_for_completion():
    # dir() cannot see what __getattr__ resolves, and the editor's completion reads the live
    # object. Without this a cell would be offered dict's methods and none of the fields.
    row = wiretypes.hydrate({"Time": 1, "Price": 2})

    assert "Time" in dir(row)
    assert "Price" in dir(row)


def test_records_nested_in_a_list_are_wrapped():
    rows = wiretypes.hydrate([{"a": 1}, {"a": 2}])

    assert [row.a for row in rows] == [1, 2]


def test_an_ordinary_mapping_that_borrows_a_key_name_is_not_a_tag():
    # Only an object of exactly the two reserved keys is an envelope.
    value = wiretypes.hydrate({wiretypes.TYPE_KEY: "datetime", "other": 1})

    assert value["other"] == 1
    assert value[wiretypes.TYPE_KEY] == "datetime"


# --- dates ---

def test_a_date_arrives_as_a_real_datetime():
    stamp = wiretypes.hydrate(
        wiretypes.tag(wiretypes.TAG_DATETIME, "2026-04-16T08:10:30.250000"))

    assert isinstance(stamp, datetime.datetime)
    assert stamp.year == 2026 and stamp.minute == 10


def test_a_date_keeps_the_names_the_previous_kernel_used():
    stamp = wiretypes.hydrate(
        wiretypes.tag(wiretypes.TAG_DATETIME, "2026-04-16T08:10:30.250000"))

    assert stamp.Year == 2026
    assert stamp.Month == 4
    assert stamp.Hour == 8
    assert stamp.Minute == 10
    assert stamp.Second == 30
    # Whole milliseconds, 0 to 999, which is what the other side counts in.
    assert stamp.Millisecond == 250
    assert stamp.microsecond == 250000


def test_a_date_carrying_an_offset_arrives_aware():
    stamp = wiretypes.hydrate(
        wiretypes.tag(wiretypes.TAG_DATETIME, "2026-04-16T08:00:00.000000+00:00"))

    assert stamp.tzinfo is not None
    assert stamp.utcoffset() == datetime.timedelta(0)


def test_day_of_week_counts_from_sunday():
    # Sunday is 0 on the other side, where Monday is 0 in Python.
    sunday = wiretypes.hydrate(
        wiretypes.tag(wiretypes.TAG_DATETIME, "2026-04-19T00:00:00.000000"))

    assert sunday.weekday() == 6
    assert sunday.DayOfWeek == 0


# --- the other tagged forms ---

@pytest.mark.parametrize("tag_name, wire, expected", [
    (wiretypes.TAG_DECIMAL, "1.005", decimal.Decimal("1.005")),
    (wiretypes.TAG_GUID, "00000000-0000-0000-0000-000000000001",
     uuid.UUID("00000000-0000-0000-0000-000000000001")),
    (wiretypes.TAG_BIGINT, "1180591620717411303424", 2 ** 70),
    (wiretypes.TAG_BYTES, "AQID", b"\x01\x02\x03"),
    (wiretypes.TAG_TIMESPAN, 5400, datetime.timedelta(minutes=90)),
])
def test_tagged_values_rebuild_into_python_types(tag_name, wire, expected):
    assert wiretypes.hydrate(wiretypes.tag(tag_name, wire)) == expected


def test_an_unknown_tag_degrades_to_its_value():
    # A newer peer may name a form this host does not have. Its plain value beats refusing to run.
    assert wiretypes.hydrate(wiretypes.tag("something-later", "text")) == "text"


def test_a_tag_that_will_not_parse_degrades_to_its_value():
    assert wiretypes.hydrate(wiretypes.tag(wiretypes.TAG_DATETIME, "not a date")) == "not a date"


# --- values that could not cross ---

def test_an_unavailable_value_explains_itself_without_raising():
    missing = wiretypes.hydrate(
        wiretypes.tag(wiretypes.TAG_UNAVAILABLE, "a function"), name="callback")

    assert "callback" in repr(missing)
    assert "a function" in str(missing)


@pytest.mark.parametrize("use", [
    lambda v: v.field,
    lambda v: v["key"],
    lambda v: list(v),
    lambda v: len(v),
    lambda v: v(),
    lambda v: bool(v),
])
def test_using_an_unavailable_value_raises_with_the_reason(use):
    missing = wiretypes.hydrate(
        wiretypes.tag(wiretypes.TAG_UNAVAILABLE, "a function"), name="callback")

    with pytest.raises(wiretypes.VersoUnavailableError) as raised:
        use(missing)

    assert "callback" in str(raised.value)


def test_an_unavailable_value_is_never_published_back():
    scope = {}
    variable_support.apply_inject(
        scope, {"callback": wiretypes.tag(wiretypes.TAG_UNAVAILABLE, "a function")})

    variables, _ = variable_support.collect(scope)

    assert "callback" not in variables


# --- the round trip that keeps the store's own types ---

def test_an_untouched_value_returns_exactly_as_it_arrived():
    # The other side recognizes its own bytes and leaves the store alone. Anything reduced from
    # the hydrated object instead would differ, and an int stored elsewhere would come back wide.
    wire = [{"Time": wiretypes.tag(wiretypes.TAG_DATETIME, "2026-04-16T08:00:00.000000"),
             "Price": 2.0}]
    scope = {}
    variable_support.apply_inject(scope, {"readings": wire})

    variables, _ = variable_support.collect(scope)

    assert variables["readings"] == wire


def test_a_rebound_name_is_published():
    scope = {}
    variable_support.apply_inject(scope, {"count": 1})
    scope["count"] = 2

    variables, _ = variable_support.collect(scope)

    assert variables["count"] == 2


# --- values from the array and frame libraries ---

try:
    import numpy
    import pandas
except ImportError:  # pragma: no cover - depends on the interpreter the suite runs against
    numpy = None
    pandas = None

# Marked per test rather than skipped at import, so that a machine without these libraries still
# runs everything above instead of silently skipping the whole file.
needs_frames = pytest.mark.skipif(
    numpy is None or pandas is None,
    reason="only exercised where the array and frame libraries are installed")


@needs_frames
def test_an_array_publishes_as_nested_lists():
    variables, _ = variable_support.collect({"grid": numpy.array([[1, 2], [3, 4]])})

    assert variables["grid"] == [[1, 2], [3, 4]]


@needs_frames
def test_a_nanosecond_date_publishes_as_a_date():
    # Nanoseconds is what a frame holds dates in by default, and item() answers a count of them
    # rather than a date, so this is the case that would quietly publish a very large integer.
    variables, _ = variable_support.collect(
        {"when": numpy.datetime64("2026-04-16T08:00:00", "ns")})

    assert variables["when"] == wiretypes.tag(
        wiretypes.TAG_DATETIME, "2026-04-16T08:00:00.000000")


@needs_frames
def test_a_frame_publishes_as_rows():
    frame = pandas.DataFrame({"Id": [1, 2], "Val": [1.5, 2.5]})

    variables, _ = variable_support.collect({"frame": frame})

    assert variables["frame"] == [{"Id": 1, "Val": 1.5}, {"Id": 2, "Val": 2.5}]


@needs_frames
def test_a_frames_dates_keep_their_kind():
    frame = pandas.DataFrame({"When": pandas.to_datetime(["2026-04-16"])})

    variables, _ = variable_support.collect({"frame": frame})

    assert variables["frame"][0]["When"] == wiretypes.tag(
        wiretypes.TAG_DATETIME, "2026-04-16T00:00:00.000000")


@needs_frames
def test_a_missing_value_publishes_as_nothing():
    variables, _ = variable_support.collect({"absent": pandas.NaT})

    assert variables["absent"] is None
