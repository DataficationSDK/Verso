# Micrograd in PowerShell

`micrograd-ps.verso` builds a small automatic differentiation engine in PowerShell, draws the
computation graph it produces, and trains a tiny multi-layer perceptron on the classic toy
dataset. Every arithmetic operation becomes a node that remembers its children and how to push
a gradient back to them, so the whole of backpropagation is visible as data rather than hidden
inside a framework.

The `Value`, `Neuron`, `Layer`, and `MLP` types are a PowerShell port of
[Andrej Karpathy's micrograd](https://github.com/karpathy/micrograd), which is MIT licensed.
The attribution and license notice sit at the top of `value.ps1` and `neuronHelper.ps1`.

## What it shows

- **Operator overloading on a PowerShell class.** `Value` implements `op_Addition`,
  `op_Subtraction`, and `op_Multiply`, so `$a * $b + $c` builds an expression graph. Each
  operator captures a backward closure that knows the local derivative.
- **Rendering a computation graph.** `New-ExpressionGraph` walks the graph into a PSGraph graph
  and `Show-ExpressionGraph` renders it to inline SVG through PSGraphView.
- **Two ways to get a reverse topological order.** The scalar and single-neuron sections build a
  real graph and let `Get-GraphTopologicalSort` order it, which is the clearest way to see what
  backpropagation actually needs. The training sections use `Invoke-ValueBackward`, which
  computes the same order iteratively without building a graph, because a training loop runs it
  hundreds of times.
- **A network from the same parts.** `Neuron`, `Layer`, and `MLP` are ordinary classes built on
  `Value`, and training is the familiar cycle: zero the gradients, forward pass, build the loss,
  backward pass, nudge the parameters.

## Files

| File | Contents |
|---|---|
| `value.ps1` | The `Value` class, its operator overloads, and `Tanh`. |
| `neuronHelper.ps1` | `Neuron`, `Layer`, and `MLP`. |
| `helpers.ps1` | `Zip`, `Sum-Value`, and `Invoke-ValueBackward`. |
| `graphHelper.ps1` | Converts `Value` graphs into PSGraph graphs and renders them. |

The notebook loads these with `#!import`, which resolves each path against the notebook's own
location. That is deliberate: a PowerShell cell has no way to discover its own directory, so a
relative `. ./value.ps1` would resolve against whatever directory the host happened to start in.

## Requirements

The graph modules target PowerShell 7.4 and declare `CompatiblePSEditions = 'Core'`. Verso's
PowerShell kernel is built on the PowerShell SDK 7.4, so it can load them, but **you must install
them from PowerShell 7 rather than Windows PowerShell 5.1**. Installing from 5.1 places the
modules in a directory the kernel does not scan, and 5.1's bundled PowerShellGet does not
understand `-AllowPrerelease`.

From a `pwsh` terminal:

```powershell
Install-Module PSQuickGraph -RequiredVersion 2.6.0-beta1 -AllowPrerelease -Scope CurrentUser
Install-Module PSGraphView  -RequiredVersion 0.2.0-beta1 -AllowPrerelease -Scope CurrentUser
```

Graphviz itself needs no separate install. PSGraphView carries native Graphviz binaries for
Windows, Linux, and macOS, which also makes it a large download the first time.

The embedded kernel does not provide package management commands, so install the modules before
starting Verso. The notebook's first code cell checks for them and prints the exact commands
above if either is missing.

## Running

Open `micrograd-ps.verso` in any Verso host and run the cells in order. The setup and import
cells must run before the rest, because later cells depend on the classes they define.

The 200 iteration training loop near the end takes a few seconds. The final cell renders the
whole loss graph, which is intentionally large: it contains every operation that produced the
scalar loss.

This is a teaching implementation. Making every scalar an object is what makes the mechanics
legible, and it is also why the approach does not scale. Real workloads want vectorized tensors.
