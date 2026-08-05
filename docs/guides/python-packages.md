# Python Packages

Packages install into the interpreter your cells actually run in, so an import on the next line finds them without a restart. Which interpreter that is comes from [Python Interpreters](python-interpreters.md).

There are three ways to get a package into a notebook: ask for it explicitly, let an import ask for it, or declare it once for the whole notebook.

## Installing explicitly

```python
#!pip requests pandas>=2
import requests
```

`#!pip` runs before the rest of the cell, so the import below it succeeds on the first run. It takes the same package specifiers and options pip does.

A line that names only packages the environment already has does nothing and says nothing, so leaving it at the top of a cell you run over and over costs neither an installer process nor two lines of output every time. That applies to plain names. A version specifier such as `pandas>=2`, an extra, or any option such as `--upgrade` is passed to the installer whatever is installed, because only the installer can say whether it is satisfied.

If `uv` is on your `PATH`, Verso uses it, which is considerably faster. Set `verso.python.useUv` to false to always use pip and the standard library.

Two familiar notebook shorthands also work in a Python cell:

```python
%pip install requests      # runs pip against this notebook's interpreter
!echo hello                # runs a shell command
```

## What an install shows

Installing one package brings in its dependencies, and both pip and uv narrate every step of that. A cell reports the result instead:

```
Installing k3d into Python 3.13.3 (managed environment) at /home/you/.verso/python/envs/3.13.3.
Installed k3d 2.17.0 and 23 dependencies.
```

That output is saved with the notebook, so whoever opens the file next sees it too, which is why it is kept to what the install actually did.

To see every distribution by name, clear **Hide Installation Output** under Packages in the notebook's settings. The report then names the dependencies and their versions, and anything the environment already had:

```
Installed k3d 2.17.0 and 23 dependencies.
Dependencies: comm 0.2.3, decorator 5.2.1, deepcomparer 0.4.0, ipython 9.15.0, ...
Already present: numpy 2.5.1.
```

Two things are never hidden. An install that fails reports the installer's output in full, because that output is the only account of why it failed. So does a dependency conflict, which pip reports while still exiting successfully, since an environment that no longer agrees with itself is worth hearing about when it happens.

The `%pip` and `!` forms above are shell commands you typed, and they show exactly what the command printed.

## Installing on import

By default, a cell that imports something the environment does not have will offer to install it rather than simply failing.

```python
import pandas as pd
```

If `pandas` is missing, a dialog appears titled "Package Install Required". It names the distribution that will be installed, the import that asked for it, and the interpreter it will go into. Approving installs it and the cell then runs normally. Declining runs the cell anyway, so it fails at the import exactly as it would have without the offer, and the same cell will not ask again during the session.

The distribution name is often not the import name. Verso knows the common cases, so `import cv2` offers `opencv-python`, `import PIL` offers `Pillow`, and `import sklearn` offers `scikit-learn`.

An import wrapped in `try` is never installed. A cell that guards an import has already said it can run without the module, so installing it would answer a question the cell did not ask.

Dynamic imports cannot be seen ahead of time. If `importlib.import_module` raises `ModuleNotFoundError`, Verso offers the install then. If the cell had not yet written any output it is re-run for you; if it had, the install is reported and you are asked to run the cell again, because output that has already appeared cannot be taken back.

An import of something inside a package you already have is never offered. `from pandas.frame import DataFrame` fails because pandas has no `frame` module, not because pandas is missing, and the error says so on its own. The exception is a namespace package such as `ruamel` or `google.cloud`, whose parts are published separately, where the part that is missing is worth installing.

### Policies

`verso.python.autoInstall` controls all of this.

| Value | Behaviour |
|-------|-----------|
| `prompt` | Ask first, listing the exact distributions and the environment. The default. |
| `auto` | Install recognized distributions without asking. |
| `off` | Never scan a cell's imports and never install. |

Under `auto`, a name Verso only guessed at is reported rather than installed. Guessing means falling back to using the import name as the distribution name, and that is precisely the case worth being careful about: a typo like `import pandsa` produces a plausible package name that somebody may well have published. Recognized names install silently; guesses are named in the output so you can install them deliberately.

The command line never installs anything unless asked. `verso run` uses `off`, and `verso run --auto-install` selects `auto`.

## Declaring dependencies

For a notebook that always needs the same packages, declare them once instead of putting a `#!pip` line in a cell.

Open the notebook's settings, find the Python kernel's **Dependencies** setting under Packages, and list the requirements one per entry in the form pip accepts, such as `pandas>=2`. They are saved with the notebook and checked once before the first cell runs, under the same policy as an import. Anything already present is left alone.

A requirement list travels between machines sensibly, which is why this one is saved into the notebook while the interpreter path and the install policy are not.

Because it travels, a declared requirement may only name a package. Two kinds of entry are refused rather than installed, and reported once when they are:

- An installer option, meaning anything starting with a hyphen, such as `--index-url` or `--find-links`.
- A location to install from, meaning a direct reference like `name @ https://example.com/pkg.whl`, a version control reference like `git+https://example.com/pkg.git`, a bare URL, or a filesystem path.

Both decide where code is fetched from, and that is a decision for whoever runs the notebook rather than for the file they opened. Ordinary requirements are untouched, including extras, version specifiers and environment markers, so `pandas[performance]>=2; python_version < "3.12"` installs normally. If you do mean one of the refused forms, run the install yourself with `#!pip`, which is a visible line in a cell and still passes anything through.

The same rule applies to the inline form below.

You can also declare requirements inline, using the standard script metadata format, at the top of the first cell you run:

```python
# /// script
# requires-python = ">=3.11"
# dependencies = ["requests", "rich"]
# ///

import requests
```

`requires-python` is checked and reported whenever the running interpreter does not satisfy it, whatever the install policy is, because that is worth knowing even where nothing may be installed. It is a note rather than an error: use `#!python` to select a different interpreter if you want to act on it.

## See also

- [Python Interpreters](python-interpreters.md)
- [Language Kernels](language-kernels.md)
- [Managing Extensions](managing-extensions.md)
