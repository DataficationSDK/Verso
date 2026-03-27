# Verso.JavaScript

JavaScript and TypeScript language kernels for Verso notebooks.

Uses Node.js (subprocess) when available for full npm and module support, with Jint (pure .NET) as an in-process fallback for JavaScript in environments without Node. TypeScript requires Node.js and auto-installs the TypeScript compiler on first use.

## Features

- **JavaScript kernel**: Full ES2024+, `require()`, dynamic `import()`, top-level `await`, npm packages via `#!npm`
- **TypeScript kernel**: Automatic transpilation via the TypeScript compiler API, same Node.js execution environment as JavaScript
- **Jint fallback** (JavaScript only): Pure .NET ES2024 interpreter, no external dependencies required
- **Cross-kernel variables**: Share values between JavaScript/TypeScript and other language kernels (C#, Python, F#)
- **Console capture**: `console.log`/`console.error` output rendered as cell outputs
- **Variable persistence**: Variables declared with `var` or assigned to `globalThis` persist across cells

## Getting Started

Verso.JavaScript is included with Verso Blazor Server and the VS Code extension. JavaScript and TypeScript cells are available immediately with no setup required.

```javascript
// JavaScript cell
var greeting = "Hello from JavaScript!";
console.log(greeting);
```

```typescript
// TypeScript cell
interface User { name: string; age: number }
var user: User = { name: "Alice", age: 30 };
console.log(`Hello, ${user.name}!`);
```

## npm Packages

Use `#!npm` to install packages from a cell. Packages are stored in `~/.verso/node/` and available via `require()` in subsequent cells.

```javascript
#!npm lodash
```

```javascript
const _ = require('lodash');
console.log(_.capitalize('hello world'));
```

## Kernels

### JavaScript (`javascript`)

- **Node.js mode**: Spawns a persistent Node.js subprocess. Variables persist in the V8 global context across cells. Supports `require()`, dynamic `import()`, and top-level `await`.
- **Jint mode**: Falls back automatically when Node.js is not installed. Supports standard ES2024 features. Does not support `require`/`import`, `async`/`await` at top level, or npm packages.

### TypeScript (`typescript`)

- Requires Node.js (no Jint fallback)
- Transpiles cells using `ts.transpileModule()` with ES2022 target and CommonJS module output
- The `typescript` npm module is auto-installed silently on first use
- Shares the same Node.js subprocess and variable scope as JavaScript cells
- Type annotations are stripped at transpile time; any valid TypeScript compiles and runs

## Magic Commands

- `#!npm <packages>` - Install npm packages (Node.js mode only). Packages are available immediately in subsequent cells via `require()`.

## Variable Sharing

Variables defined in JavaScript or TypeScript cells are automatically published to the shared variable store after each execution. Other kernels can read them:

```csharp
// C# cell
var value = Variables.Get<long>("myJsVariable");
```

Variables from other kernels are injected into the JavaScript global scope before each cell execution. Only JSON-serializable values cross the boundary (functions, Symbols, and other non-serializable types are excluded).

## Limitations

- Static `import` declarations are not supported in cells. Use `const { x } = await import('y')` instead.
- `let` and `const` declarations are scoped to the cell. Use `var` or bare assignment for cross-cell persistence.
- TypeScript type checking is limited to transpile-time diagnostics (no cross-cell type awareness).
- Jint mode does not support Node.js APIs, npm packages, or async/await at the top level.
