# Extensions

Verso's extension system lets third-party authors ship language kernels, cell renderers, themes, layouts, magic commands, toolbar actions, formatters, and more. Every built-in feature uses the same public interfaces available to extensions, so what you can do with an extension matches what the platform itself can do.

The docs below cover everything an extension author needs, from a first `dotnet new verso-extension` to packaging and publishing.

## Getting started

- **[Getting Started](getting-started.md)** — scaffold an extension project, register it with the host, and run it locally.

## Reference

- **[Extension Interfaces](extension-interfaces.md)** — the eleven interfaces in `Verso.Abstractions` and the capability surface each exposes.
- **[Context Reference](context-reference.md)** — `IVersoContext`, `IExtensionHostContext`, and the services an extension can reach through them.

## Authoring guides

- **[Layouts](layouts.md)** — write a custom layout extension. Covers `ILayoutEngine`, the `data-cell-slot` slot-mount pattern, data-attribute event routing, the `ILayoutInteractionHandler` capability, the re-render protocol, and how to style against the host theme. Targeted at inline (in-page) layouts.
- **[Theme Authoring](theme-authoring.md)** — define color palettes, typography, and the layout-extension theme tokens that custom layouts inherit through CSS variables on `:root`.

## Workflow

- **[Testing Extensions](testing-extensions.md)** — the `Verso.Testing` library, `StubVersoContext`, and patterns for unit-testing each capability.
- **[Packaging and Publishing](packaging-and-publishing.md)** — produce a NuGet package, distribute through public or private feeds, and version your extension across host releases.
- **[Best Practices](best-practices.md)** — state management, thread safety, security considerations, and conventions that play well with the broader ecosystem.
