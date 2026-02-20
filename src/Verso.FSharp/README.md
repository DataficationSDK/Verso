# Verso.FSharp

F# Interactive language kernel extension for [Verso](https://github.com/DataficationSDK/Verso) notebooks.

## Overview

Full F# scripting powered by FSharp.Compiler.Service. Latest F# language version, persistent state across cells, IntelliSense, diagnostics, hover info, and NuGet package references.

### Features

- **F# Interactive session** with configurable warning level, language version, and default opens
- **IntelliSense** — dot-completion, type signatures, documentation, and error diagnostics
- **NuGet references** via `#r "nuget: PackageName"` with dual-path resolution (FSI built-in or Verso fallback)
- **Script directives** — `#r`, `#load`, `#I`, `#nowarn`, and `#time`
- **Bidirectional variable sharing** between F# and other kernels
- **Rich data formatting** for discriminated unions, records, options, results, maps, sets, tuples, and collections
- **Configurable settings** — warning level, language version, private binding visibility, collection display limits
- **Polyglot Notebooks migration** — automatic conversion of `#!fsharp`/`#!f#`, `#!set`, and `#!share` patterns during `.ipynb` import

## Installation

```shell
dotnet add package Verso.FSharp
```

This package depends on [Verso.Abstractions](https://www.nuget.org/packages/Verso.Abstractions) and `FSharp.Compiler.Service`.

## Quick Start

```fsharp
#r "nuget: Newtonsoft.Json"
open Newtonsoft.Json

let data = {| Name = "Verso"; Version = 1 |}
JsonConvert.SerializeObject(data, Formatting.Indented)
```
