# Verso.Python

Python language kernel extension for [Verso](https://github.com/DataficationSDK/Verso) notebooks.

## Overview

Embeds CPython via pythonnet with a persistent scope across cells. IntelliSense is powered by jedi with a fallback to rlcompleter. Requires **Python 3.8-3.12** installed on the system (Python 3.13+ is not yet supported by pythonnet).

### Features

- **Python execution** with persistent state, stdout/stderr capture, and full traceback formatting
- **IntelliSense** via jedi (completions, diagnostics, hover with type inference and docstrings), with rlcompleter fallback
- **Bidirectional variable sharing** between Python and other kernels (C#, F#, SQL, PowerShell)
- **Matplotlib integration** with automatic figure capture (no explicit `plt.show()` required)
- **`display()` function** supporting `_repr_html_()`, `_repr_png_()`, `_repr_svg_()` output types
- **Last-expression capture** with rich output for objects implementing `_repr_html_()`
- **Virtual environment support** via the `#!pip` magic command with automatic venv management
- **Auto-detection** of Python installations across macOS, Linux, and Windows
- **Configurable settings** for default imports, startup code, variable sharing, and Python library path

## Installation

```shell
dotnet add package Verso.Python
```

This package depends on [Verso.Abstractions](https://www.nuget.org/packages/Verso.Abstractions) and `pythonnet`.

## Quick Start

```python
import json
data = {"name": "Verso", "version": 1}
print(json.dumps(data, indent=2))
```

Install packages with the `#!pip` magic command:

```
#!pip pandas matplotlib
```
