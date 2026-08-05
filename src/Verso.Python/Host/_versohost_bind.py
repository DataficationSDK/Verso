"""
Projecting a widget's trait into the notebook's shared variable store.

A widget is a live object and the store holds data, so the two are joined by watching rather
than by sharing: the trait is observed, and each change is reduced and sent out under the name
the author chose. A value arriving the other way is set back onto the trait, which runs the
author's own callbacks and moves the view, exactly as an assignment in a cell would.

Nothing here imports a widget library. ``traitlets`` is duck-typed through the two methods every
object with observable traits provides, and ``ipywidgets`` is only consulted through
:data:`sys.modules` to recognise a trait that holds another widget. A binding is therefore
possible against anything observable, and an environment with neither library loaded pays
nothing for this module being present.
"""

import sys

import _versohost_vars as variable_support
import _versohost_wiretypes as wiretypes


# Frames raised by a trait changing are not answers to a request, and this is the id reserved
# for a message the host sends on its own behalf.
UNSOLICITED_REQUEST_ID = 0

# What one projected value may occupy once reduced. Deliberately smaller than the limit a cell's
# whole scope is published under: that one covers a single snapshot taken once per cell, and this
# one covers a value re-sent on every change, which for a dragged control is many times a second.
LIMIT_BYTES = 1024 * 1024

_writer = None
_reporter = None

# By variable name, matching the store, which treats names without regard to case. Lower-cased
# keys are what makes the two agree about which entry a name means.
_bindings = {}


class _Binding(object):
    """One trait watched under one name."""

    __slots__ = ("name", "expression", "trait", "widget_id", "owner", "handler", "refused")

    def __init__(self, name, expression, trait, widget_id, owner, handler):
        self.name = name
        self.expression = expression
        self.trait = trait
        self.widget_id = widget_id
        self.owner = owner
        self.handler = handler

        # Whether the last change was too large to send. Held so that a control producing one
        # oversize value per frame explains itself once rather than on every frame.
        self.refused = False


# --- installation -------------------------------------------------------------------------


def install(writer, reporter=None):
    """
    Take the writer projected values travel over, and the reporter refusals are explained through.
    """
    global _writer, _reporter

    _writer = writer
    _reporter = reporter


def reset():
    """Stop watching everything. Undoes :func:`install`, and is what a new session starts from."""
    global _writer, _reporter

    for binding in list(_bindings.values()):
        _stop_watching(binding)

    _bindings.clear()
    _writer = None
    _reporter = None


def describe():
    """Every binding, as plain dictionaries, in the order they were made."""
    return [
        {
            "name": binding.name,
            "expression": binding.expression,
            "trait": binding.trait,
            "widget_id": binding.widget_id,
        }
        for binding in _bindings.values()
    ]


# --- binding ------------------------------------------------------------------------------


def bind(scope, expression, trait, name=None):
    """
    Start projecting one trait, and report what happened.

    The result is always a dictionary carrying a ``status`` of ``ok`` or ``error``. A refusal
    carries a ``reason`` written for the author of the cell, because every way this can fail is
    something they can put right: a name that is not in scope, an object with no such trait, a
    trait holding another widget, or a value too large to keep sending.
    """
    if not expression:
        return _refused("no object was named before the trait.")
    if not trait:
        return _refused("no trait was named.")

    try:
        owner = eval(expression, scope)  # the author's own expression, in the author's own scope
    except Exception as error:
        return _refused("'%s' could not be evaluated: %s" % (expression, error))

    if not callable(getattr(owner, "has_trait", None)) or not callable(getattr(owner, "observe", None)):
        return _refused(
            "'%s' is a %s, which has no observable traits."
            % (expression, type(owner).__name__))

    try:
        known = owner.has_trait(trait)
    except Exception:
        known = False

    if not known:
        return _refused(
            "'%s' has no trait named '%s'. %s"
            % (expression, trait, _suggest(owner)))

    try:
        current = getattr(owner, trait)
    except Exception as error:
        return _refused("'%s.%s' could not be read: %s" % (expression, trait, error))

    holds_widget = _widget_reason(current)
    if holds_widget is not None:
        return _refused(
            "'%s.%s' %s, and a widget is a live object rather than data. "
            "Bind a trait of it instead." % (expression, trait, holds_widget))

    reduced, fitted = variable_support.reduce_one(current, LIMIT_BYTES)
    if not fitted:
        return _refused(
            "'%s.%s' holds a %s that is over the %s a projected value may occupy."
            % (expression, trait, type(current).__name__,
               variable_support.format_bytes(LIMIT_BYTES)))

    chosen = name or trait
    widget_id = _identify(owner)

    # The same trait bound again replaces what was there rather than being watched twice, whether
    # it is renamed or rebound under the name it already had. Both are reported, so the managing
    # side can drop the entry it holds under the old name.
    replaced = _remove_matching(widget_id, trait, chosen)

    handler = _make_handler(chosen)
    binding = _Binding(chosen, expression, trait, widget_id, owner, handler)

    try:
        owner.observe(handler, names=trait)
    except Exception as error:
        return _refused("'%s.%s' could not be watched: %s" % (expression, trait, error))

    _bindings[_key(chosen)] = binding

    return {
        "status": "ok",
        "name": chosen,
        "expression": expression,
        "trait": trait,
        "widget_id": widget_id,
        "value": reduced,
        "replaced": replaced,
    }


def unbind(name):
    """Stop projecting one name. Reports whether there was anything to stop."""
    binding = _bindings.pop(_key(name), None) if name else None
    if binding is None:
        return False

    _stop_watching(binding)
    return True


def apply(name, value):
    """
    Set a value that arrived from another kernel onto the trait it was projected from.

    Setting a trait runs every callback the author registered for it, so this belongs on the
    thread cells run on and never on the thread that reads the socket.

    No echo is suppressed here. The assignment raises the trait's own change notification, which
    is sent back out, and the managing side recognises it as the value it just sent and stops
    there. Going through the same path both ways is what makes a value the trait coerced, or
    refused, visible to the other kernels rather than silently diverging from what they hold.
    """
    binding = _bindings.get(_key(name)) if name else None
    if binding is None:
        return False

    try:
        setattr(binding.owner, binding.trait, wiretypes.hydrate(value, name))
        return True
    except Exception as error:
        _report(
            "'%s' could not be set on %s.%s: %s. The widget still holds what it held, and "
            "the shared variable holds what the other kernel wrote."
            % (name, binding.expression, binding.trait, error))
        return False


# --- watching -----------------------------------------------------------------------------


def _make_handler(name):
    """A change callback bound to one name rather than to the widget, which may hold several."""

    def handle(change):
        try:
            new = change["new"] if isinstance(change, dict) else getattr(change, "new", None)
        except Exception:
            return
        _publish(name, new)

    return handle


def _publish(name, value):
    binding = _bindings.get(_key(name))
    if binding is None:
        return

    writer = _writer
    if writer is None:
        return

    reduced, fitted = variable_support.reduce_one(value, LIMIT_BYTES)
    if not fitted:
        if not binding.refused:
            binding.refused = True
            _report(
                "'%s' now holds a %s that is over the %s a projected value may occupy, so the "
                "other kernels still see what it held before."
                % (name, type(value).__name__, variable_support.format_bytes(LIMIT_BYTES)))
        return

    binding.refused = False

    try:
        writer({
            "type": "bind_update",
            "req_id": UNSOLICITED_REQUEST_ID,
            "name": binding.name,
            "value": reduced,
        })
    except Exception:
        # The connection has gone, which the parts of the host that own it already report. A
        # trait changing is not where an author should first hear that their kernel died, and
        # raising here would surface inside whatever assigned the trait.
        pass


def _stop_watching(binding):
    try:
        binding.owner.unobserve(binding.handler, names=binding.trait)
    except Exception:
        # An object that has already been torn down cannot be unwatched, and does not need to be.
        pass


def _remove_matching(widget_id, trait, name):
    """
    Drop the binding this one supersedes, and name it. The same trait of the same object is only
    ever projected once, so binding it again under a second name moves it rather than doubling it.
    """
    replaced = None

    for binding in list(_bindings.values()):
        if binding.widget_id == widget_id and binding.trait == trait:
            replaced = binding.name
            unbind(binding.name)

    unbind(name)
    return replaced


# --- inspection ---------------------------------------------------------------------------


def _identify(owner):
    """
    What a binding is anchored to.

    A widget's model id is the name its view already addresses it by, and it is the same id its
    comm carries, so a binding and a live view agree about which object they mean. An observable
    object that is not a widget has no such name, and its identity in this interpreter serves the
    one purpose the anchor has: telling the same object rebound from a different one.
    """
    model_id = getattr(owner, "model_id", None)
    if isinstance(model_id, str) and model_id:
        return model_id

    return "object-%x" % id(owner)


def _widget_reason(value):
    """
    Why a value cannot be projected, when it holds a widget, and None when it does not.

    ``ipywidgets`` is read from the modules already loaded rather than imported: a widget-valued
    trait can only exist in an interpreter that has the library, so looking it up costs nothing
    and finding it absent is itself the answer.
    """
    module = sys.modules.get("ipywidgets")
    widget_type = getattr(module, "Widget", None) if module is not None else None
    if widget_type is None:
        return None

    try:
        if isinstance(value, widget_type):
            return "holds a %s" % type(value).__name__
        if isinstance(value, (list, tuple)) and any(isinstance(item, widget_type) for item in value):
            return "holds widgets"
    except Exception:
        return None

    return None


def _suggest(owner, limit=6):
    """The trait names the object does have, which is what a misspelling needs to see."""
    try:
        names = [name for name in owner.trait_names() if not name.startswith("_")]
    except Exception:
        return ""

    if not names:
        return ""

    names.sort()
    shown = ", ".join(names[:limit])
    if len(names) > limit:
        shown += ", ..."

    return "It has: %s." % shown


def _key(name):
    return name.lower() if isinstance(name, str) else name


def _refused(reason):
    return {"status": "error", "reason": reason}


def _report(text):
    reporter = _reporter
    if reporter is None:
        return
    try:
        reporter(text)
    except Exception:
        pass
