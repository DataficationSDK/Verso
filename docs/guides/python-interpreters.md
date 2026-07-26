# Python Interpreters

Python cells run in the Python already installed on your machine, as a separate process. Verso does not embed a Python of its own and does not bundle one, so a notebook sees exactly the interpreter, virtual environment, and packages you would get from a terminal. Python 3.8 and newer are supported, including 3.13 and 3.14.

Running Python outside the notebook process is what makes the rest of this possible: a cell can be interrupted, a native crash takes down only the interpreter, and a restart genuinely resets imported module state.

## Which interpreter a notebook uses

Verso looks for an interpreter the first time a Python cell runs, and takes the first candidate that validates. The order is:

| Order | Source | Where it comes from |
|-------|--------|---------------------|
| 1 | Explicit setting | `verso.python.interpreterPath` in VS Code, or `PythonExecutable` when embedding the engine |
| 2 | Environment override | The `VERSO_PYTHON` environment variable |
| 3 | Session selection | `#!python <path>` run earlier in this session |
| 4 | Active virtual environment | `VIRTUAL_ENV` |
| 5 | Active conda environment | `CONDA_PREFIX` |
| 6 | Workspace environment | A `.venv` or `venv` directory beside the notebook |
| 7 | Search path | `python3` or `python` on `PATH` |
| 8 | Well-known locations | Standard install directories for the platform |

A candidate has to answer a version probe and report CPython 3.8 or newer, so a broken entry earlier in the list does not stop a working one later from being found. If nothing validates, the cell reports what was searched and how to install Python for your platform.

Because activation is read from the environment, activating a virtual environment before launching your editor is usually all that is needed.

## Inspecting and changing the interpreter

The `#!python` command works from any Python cell.

```python
#!python
```

Reports the interpreter in use, its version, what kind of environment it is, and which of the sources above selected it.

```python
#!python --list
```

Lists every interpreter Verso discovered and where each was found, which is the quickest way to see what is available before choosing.

```python
#!python /usr/local/bin/python3.13
```

Selects an interpreter and restarts the kernel so the next cell runs against it.

A selection made this way lasts for the session and is not written into the notebook file. An absolute path is meaningful only on the machine that produced it, so a notebook carrying one would break for the next person who opened it. For an interpreter that should persist, set `verso.python.interpreterPath` instead, which is a per-machine setting.

The kernel restarts when you select an interpreter, so variables defined in earlier Python cells are cleared. Values shared into the notebook's variable store by other languages are unaffected.

Stopping the interpreter takes anything it started with it, so a process launched from a cell, such as a shell command left running in the background, ends with the restart rather than outliving it.

## Externally managed installations

Some Python installations, particularly those from a Linux distribution's package manager, mark themselves as externally managed to stop tools from installing into them. Installing a package into one of these is refused by pip itself.

When the selected interpreter is externally managed and something needs to be installed, Verso creates a small environment derived from it, with access to the base interpreter's existing packages, and uses that instead. The notebook keeps the same Python version and can still import everything the base installation provides.

A virtual environment is never substituted this way, even when it was created from an externally managed base. A virtual environment is yours to install into, which is the point of having made one.

## Settings

| Setting | Effect |
|---------|--------|
| `verso.python.interpreterPath` | The interpreter to use. Empty means discover one. |
| `verso.python.autoInstall` | What happens when a cell imports something the environment lacks. See [Python Packages](python-packages.md). |
| `verso.python.useUv` | Use `uv` for installs and environment creation when it is on `PATH`. |

Changes apply the next time a notebook is opened or the kernel is restarted.

## See also

- [Python Packages](python-packages.md)
- [Language Kernels](language-kernels.md)
- [Migrating from Jupyter](../migration/from-jupyter.md)
