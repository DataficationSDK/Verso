"""Finds the imports a cell needs and reports which of them this environment cannot satisfy.

The managing process cannot answer either question: the interpreter running the cells owns
the import path, the site directories, and the installed distribution metadata. Depends only
on the standard library and targets CPython 3.8 and newer.
"""

import ast
import importlib
import importlib.util
import sys

# Names that are part of the language rather than something installable.
_LANGUAGE_MODULES = frozenset(("__future__", "__main__"))

# Characters that end a distribution name inside a PEP 508 requirement string. Version
# specifiers, extras, markers, and direct URL references all begin with one of them.
_REQUIREMENT_TERMINATORS = "[]<>=!~;,() @"

# ast.TryStar arrived with except* in 3.11 and guards its body the same way ast.Try does.
_TRY_NODES = tuple(
    node for node in (getattr(ast, "Try", None), getattr(ast, "TryStar", None)) if node
)


def scan(code):
    """
    Report the root modules the cell imports that this interpreter cannot resolve.

    Each entry is ``{"module": name, "optional": bool}``. A cell that does not parse reports
    nothing: a scan must never be the reason a cell fails to run, and the execution path
    reports the syntax error with a caret anyway.
    """
    try:
        tree = ast.parse(code or "")
    except (SyntaxError, ValueError):
        return []

    found = _collect_imports(tree)
    if not found:
        return []

    missing = []
    for name in sorted(found):
        if not _resolvable(name):
            missing.append({"module": name, "optional": found[name]})

    return missing


def unsatisfied(requirements):
    """
    Report which of the given PEP 508 requirement strings name a distribution this
    environment does not have installed.

    A distribution that is present satisfies its requirement even when the version specifier
    cannot be evaluated here. Reporting it as unsatisfied instead would ask the user to
    approve an install on every session for something already in place.
    """
    if not requirements:
        return []

    pending = []
    for requirement in requirements:
        if not isinstance(requirement, str) or not requirement.strip():
            continue

        name = _distribution_name(requirement)
        if not name:
            continue

        if not _distribution_present(name):
            pending.append(requirement.strip())

    return pending


# --- import discovery ---


def _collect_imports(tree):
    """
    Map every imported root module to whether every one of its imports was guarded.

    A module imported inside a ``try`` is reported optional, because a cell that already
    handles its absence is not asking for it to be installed. One unguarded import anywhere
    makes the module required, which is why the flag is combined rather than overwritten.

    Descent is explicit rather than ast.walk so the guard flag can be carried down. Imports
    nested in function and class bodies are reached the same way as top-level ones, which is
    what makes a lazily imported module visible before the function is ever called.
    """
    found = {}
    _visit(tree, False, found)
    return found


def _visit(node, guarded, found):
    if isinstance(node, ast.Import):
        _record([alias.name for alias in node.names], guarded, found)
        return

    if isinstance(node, ast.ImportFrom):
        # A relative import resolves against the cell's own package, so there is nothing
        # to install for it.
        if not node.level and node.module:
            _record([node.module], guarded, found)
        return

    if isinstance(node, _TRY_NODES):
        # Only the body is guarded. An import in an except or finally clause runs
        # unconditionally once that clause is reached.
        for child in node.body:
            _visit(child, True, found)
        for attribute in ("handlers", "orelse", "finalbody"):
            for child in getattr(node, attribute, []) or []:
                _visit(child, guarded, found)
        return

    for child in ast.iter_child_nodes(node):
        _visit(child, guarded, found)


def _record(names, guarded, found):
    for name in names:
        root = _root_module(name)
        if not root or root in _LANGUAGE_MODULES:
            continue
        found[root] = found.get(root, True) and guarded


def _root_module(name):
    if not isinstance(name, str):
        return None
    return name.split(".", 1)[0].strip()


def _resolvable(name):
    """Whether the module can be found without importing it."""
    if name in sys.modules:
        return True

    try:
        return importlib.util.find_spec(name) is not None
    except Exception:
        # A broken entry on the path, or a parent package that raises on import, both surface
        # here. Neither is something an install would fix, so the module counts as present.
        return True


# --- declared requirements ---


def _distribution_name(requirement):
    text = requirement.strip()

    # A direct reference ("name @ https://...") keeps only the part before the marker.
    for index, character in enumerate(text):
        if character in _REQUIREMENT_TERMINATORS:
            return text[:index].strip()

    return text


def _distribution_present(name):
    try:
        import importlib.metadata as metadata
    except ImportError:
        try:
            import importlib_metadata as metadata  # type: ignore
        except ImportError:
            # Nothing here can answer the question, so nothing is reported. The import that
            # actually needs the distribution still reaches the backstop.
            return True

    try:
        metadata.distribution(name)
        return True
    except Exception:
        # PackageNotFoundError, and anything a malformed dist-info raises on the way.
        return False
