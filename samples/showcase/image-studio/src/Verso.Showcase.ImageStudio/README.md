# Verso.Showcase.ImageStudio

A sample layout extension for [Verso](https://versonotebooks.com) that presents a notebook as a
layered image document: a canvas in the middle, a layer panel on the right, and a tool palette on
the left. It is one of the Verso showcase layouts, built to demonstrate what the layout system can do.

## What it shows

The document and the editor are the same `.verso` file viewed two ways. Open it in the Notebook
layout and it is a notebook; switch to Image Studio and it is an image editor. The layer stack is
persisted with the notebook, and any layer can be driven by a kernel variable, so editing and
re-running a code cell repaints it live. The whole editor runs inside the host's sandboxed frame and
themes itself through the host's `--verso-*` CSS variables, so it follows every Verso theme.

## Installation

Install it from the Verso **Extensions** pane: search for `Verso.Showcase`, install
**Verso.Showcase.ImageStudio**, then choose **Image Studio** from the layout picker. A notebook can
also declare it as a required extension so it installs automatically on open.

## Using it

With the layout active you can add layers (solid, gradients, checkerboard, stripes, dots, rings,
text, and procedural), set each layer's opacity and blend mode, and reorder the stack. Add a
procedural layer and bind it to a kernel variable to draw a layer from code in any language the
notebook runs.

To save your work, use the host **Export** menu: while Image Studio is the active layout it adds
**PNG Image** (the rasterized canvas) and **SVG Image** (a resolution-independent re-emit of the
vector layers) alongside the notebook's other export formats.

## License

MIT. This package bundles no third-party libraries.
