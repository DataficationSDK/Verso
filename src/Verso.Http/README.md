# Verso.Http

HTTP request extension for Verso notebooks. Send REST API requests using `.http` file syntax directly in notebook cells.

## Features

- `.http` file syntax (compatible with VS Code REST Client / JetBrains HTTP Client)
- Variable interpolation with `{{variable}}` syntax
- Dynamic variables (`$guid`, `$timestamp`, `$randomInt`, etc.)
- Named request chaining via `# @name` and response references
- Multiple requests per cell separated by `###`
- Magic commands for base URL, default headers, and timeout configuration
