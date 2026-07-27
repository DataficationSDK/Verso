# Verso.Python

Python language kernel extension for [Verso](https://github.com/DataficationSDK/Verso) notebooks.

## Overview

Runs the Python already installed on the machine as a separate process, so a notebook sees the same interpreter, virtual environment, and packages a terminal would. Nothing is embedded and no Python is bundled. Requires **Python 3.8 or newer**, including 3.13 and 3.14.

The interpreter comes from an explicit setting, the `VERSO_PYTHON` variable, a `#!python` selection, an active virtual environment or conda environment, a `.venv` beside the notebook, `PATH`, or a well-known install location, in that order.

### Features

- **Python execution** with persistent state across cells, output streamed as it is produced, and full traceback formatting
- **Interrupt and restart** that genuinely work, including a restart that clears imported module state
- **Crash isolation**: a native crash takes down the interpreter, reports itself, and the next cell runs in a fresh one
- **IntelliSense** answered inside the running interpreter, so it sees the packages that are actually installed and the names earlier cells defined. Uses jedi when available, with standard library fallbacks for completions and syntax diagnostics
- **Bidirectional variable sharing** between Python and every other kernel in the notebook
- **Matplotlib integration** with automatic figure capture, including figures produced part-way through a cell
- **`display()` function** supporting `_repr_html_()`, `_repr_png_()`, `_repr_svg_()`, with an optional MIME type hint (for example `display(obj, "application/json")`)
- **`input()` support**, prompting in the notebook
- **Package management** through `#!pip`, the `%pip` and `!` shell escapes, consent-gated install on import, inline script metadata, and a per-notebook requirement list, all uv-accelerated when uv is present
- **Interpreter selection** with `#!python`, including `#!python --list` to see what was discovered

## Installation

This package ships with Verso and is loaded automatically. It is referenced directly only when embedding the engine in your own application, where it depends on [Verso.Abstractions](https://www.nuget.org/packages/Verso.Abstractions) and nothing else.

## Quick Start

```python
import json
data = {"name": "Verso", "version": 1}
print(json.dumps(data, indent=2))
```

Install packages explicitly:

```python
#!pip pandas matplotlib
```

Or just import them, and approve the prompt:

```python
import pandas as pd
```

Check or change the interpreter:

```python
#!python --list
#!python /usr/local/bin/python3.13
```
