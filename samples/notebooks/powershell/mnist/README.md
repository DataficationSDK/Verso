# MNIST with Micrograd in PowerShell

`mnist-ps.verso` takes the scalar automatic differentiation engine from the neighboring
[micrograd sample](../micrograd/README.md) and points it at real data: handwritten digits from
MNIST. It normalizes each 28 by 28 image into a 784 element vector, one-hot encodes the label,
and trains a small classifier where every single multiply and add is a node in an expression
graph.

It exists to show that the engine is a real one, not a toy that only works on four hand-picked
rows. It is not a sensible way to train a digit classifier, and the notebook says so.

## What it shows

- **Reusing helper scripts across notebooks.** The engine is not copied. The notebook pulls
  `value.ps1`, `neuronHelper.ps1`, and `helpers.ps1` straight from the micrograd folder with
  `#!import ../micrograd/...`, which resolves relative to this notebook's own location.
- **Getting real data into a notebook.** `PSMnist` downloads and expands the dataset, renders a
  digit as inline SVG so you can see what you are training on, and converts images and labels
  into training samples.
- **A linear output layer.** `[MLP]::new(784, @(16, 10), $false)` keeps `tanh` on the hidden
  neurons but leaves the ten output scores linear, which is what you want when the prediction is
  the highest scoring index.
- **Iterative backpropagation.** Each example's loss graph is large enough that a recursive walk
  is a poor idea, so training calls `Invoke-ValueBackward`, which orders the graph with an
  explicit stack.

## Requirements

`PSMnist` targets PowerShell 7.4 and declares `CompatiblePSEditions = 'Core'`. Verso's PowerShell
kernel can load it, but **install it from PowerShell 7 rather than Windows PowerShell 5.1**.
Installing from 5.1 places the module in a directory the kernel does not scan, and 5.1's bundled
PowerShellGet does not understand `-AllowPrerelease`.

From a `pwsh` terminal:

```powershell
Install-Module PSMnist -RequiredVersion 0.1.0-beta1 -AllowPrerelease -Scope CurrentUser
```

This notebook needs only `PSMnist`. It does not draw computation graphs, so it does not need the
PSGraph modules that the micrograd notebook uses.

The embedded kernel does not provide package management commands, so install the module before
starting Verso. The notebook's first code cell checks for it and prints the exact command above
if it is missing.

**Network access.** The first run downloads the MNIST training files and caches them under
`$HOME/.psmnist/mnist`. Later runs read from that cache and need no network. If the machine sits
behind a restrictive proxy or has no outbound access, the download cell is the one that fails.

## Running

Open `mnist-ps.verso` in any Verso host and run the cells in order. Three knobs control how much
work it does:

| Variable | Default | Effect |
|---|---|---|
| `$sampleCount` | 10 | How many digits are prepared and trained on. |
| `$epochs` | 3 | Passes over those digits. |
| `$learningRate` | 0.001 | Step size for the parameter update. |

Keep them small to start. A single forward pass over a 784 input network builds roughly twenty
five thousand graph nodes, and every one of them allocates a closure, so raising `$sampleCount`
or `$epochs` costs time quickly. Accuracy on ten digits over three epochs is not meant to be
impressive; the point is that the gradients are real and the loss moves.
