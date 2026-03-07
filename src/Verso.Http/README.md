# Verso.Http

HTTP request extension for [Verso](https://github.com/DataficationSDK/Verso) notebooks.

## Overview

Send REST API requests directly in notebook cells using `.http` file syntax, the same format supported by VS Code's REST Client and JetBrains HTTP Client. Responses are formatted with status badges, timing, collapsible headers, and pretty-printed JSON.

### Features

- **`.http` file syntax** with support for all HTTP methods (GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS)
- **Variable interpolation** with `@name = value` declarations and `{{name}}` references
- **Dynamic variables** (`{{$guid}}`, `{{$timestamp}}`, `{{$randomInt}}`, `{{$datetime}}`, `{{$processEnv}}`)
- **Named request chaining** via `# @name` and response references (`{{name.response.body.$.path}}`)
- **Multiple requests per cell** separated by `###`
- **Request directives** (`# @no-redirect`, `# @no-cookie-jar`)
- **Query continuation** (lines starting with `?` or `&` append to the URL)
- **Magic commands** for base URL (`#!http-set-base`), default headers (`#!http-set-header`), and timeout (`#!http-set-timeout`)
- **IntelliSense** with HTTP method and header completions, diagnostics for unresolved variables, and hover info
- **Cross-kernel integration** where response data (`httpResponse`, `httpStatus`) is shared to C#, F#, and other cells

## Installation

```shell
dotnet add package Verso.Http
```

This package depends only on [Verso.Abstractions](https://www.nuget.org/packages/Verso.Abstractions).

## Quick Start

```
#!http-set-base https://api.example.com
```

```http
GET /users
Accept: application/json
```
