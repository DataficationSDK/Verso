# Verso.PowerShell

PowerShell language kernel extension for [Verso](https://github.com/DataficationSDK/Verso) notebooks.

## Overview

Hosts a persistent PowerShell runspace powered by Microsoft.PowerShell.SDK. State persists across cells, pipelines render through `Out-String` for proper formatting, and variables are automatically shared with other kernels.

### Features

- **PowerShell execution** with a persistent runspace, pipeline output, and full error/warning/information stream handling
- **IntelliSense** via `CommandCompletion` (the PowerShell SDK's native tab-completion engine)
- **Parse diagnostics** via the PowerShell AST parser
- **Hover information** with AST-aware context (commands, variables, members)
- **Bidirectional variable sharing** between PowerShell and other kernels (C#, F#, SQL, Python)
- **Format-aware output** that detects PowerShell format objects and pipes them through `Out-String`
- **`Display` function** for explicit output rendering from PowerShell cells
- **`$VersoVariables`** direct access to the shared variable store from PowerShell

## Installation

```shell
dotnet add package Verso.PowerShell
```

This package depends on [Verso.Abstractions](https://www.nuget.org/packages/Verso.Abstractions) and `Microsoft.PowerShell.SDK`.

## Quick Start

```powershell
Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 Name, CPU, WorkingSet
```
