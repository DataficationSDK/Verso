# HTTP Requests

The HTTP cell type lets you call REST APIs directly from a notebook, with a readable request syntax, variables, request chaining, and rich response rendering. Set a cell's type to HTTP (or its language to `http`) and write requests in it.

## Writing a request

A request is a method and URL, optional header lines, then a blank line and an optional body:

```
GET https://httpbin.org/get
Accept: application/json
```

```
POST https://httpbin.org/post
Content-Type: application/json

{
  "name": "Verso",
  "features": ["notebooks", "http", "sql"]
}
```

Running the cell sends the request and renders the response below it: a status badge, the elapsed time, a collapsible list of response headers, and the body. When the body is JSON it is shown as an interactive tree.

## Variables

Define cell-local variables with `@name = value` and reference them anywhere with `{{name}}`. Variables can build on each other:

```
@hostname = httpbin.org
@baseUrl = https://{{hostname}}

GET {{baseUrl}}/get
Accept: application/json
```

Verso also provides dynamic variables that resolve at send time, including `{{$guid}}`, `{{$timestamp}}`, `{{$datetime <format>}}`, `{{$randomInt <min> <max>}}`, and `{{$processEnv <NAME>}}` for reading an environment variable.

## Several requests in one cell

Separate multiple requests with a line of three or more `#` characters:

```
GET https://httpbin.org/get

###

GET https://httpbin.org/headers
```

## Naming and chaining requests

Give a request a name with a `# @name` comment, then reference its response in a later request. This is how you use a value returned by one call in the next:

```
# @name createUser
POST https://httpbin.org/post
Content-Type: application/json

{ "username": "verso_user" }

###

# Reuse a field from the first response
GET {{createUser.response.body.$.url}}
```

You can read response fields (`{{name.response.body.$.field}}`) and response headers (`{{name.response.headers.HeaderName}}`).

## A shared base URL and headers

Rather than repeat a host on every request, set a base URL and default headers with magic commands, then use relative paths:

```
#!http-set-base https://httpbin.org
#!http-set-header Accept application/json

GET /headers
```

`#!http-set-timeout` adjusts the request timeout. A relative URL with no base configured is an error, so set the base first.

Per-request directives tune behavior: `# @no-redirect` stops the client following redirects, and `# @no-cookie-jar` disables the cookie jar for that request.

## When a request fails the cell

A response outside the 2xx range fails the cell. The status line and the body are still rendered above the failure, because they are usually the whole point of looking, and the failure is added after them so the evidence reads before the verdict.

There is no setting to turn this off. If a request is expected to come back 4xx or 5xx, and the cell should not be marked failed for it, read `httpStatus` in a following cell and decide there:

```csharp
var status = Variables.Get<string>("httpStatus");
if (status != "404") throw new Exception($"Expected a 404, got {status}.");
```

## Using a response elsewhere

After a request runs, the kernel writes the response body and status into the shared variable store, so a cell in another language can pick them up:

```csharp
var body = Variables.Get<string>("httpResponse");
var status = Variables.Get<string>("httpStatus");
```

That makes it easy to fetch data over HTTP in one cell and process it in C#, Python, or SQL in the next. See [Language Kernels](language-kernels.md) for how the variable store works across languages.
