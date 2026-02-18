using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Verso.Blazor.Shared.Services;
using Verso.Blazor.Wasm;
using Verso.Blazor.Wasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// Replace the default NavigationManager — the webview URI scheme
// (vscode-webview://) is not parseable by System.Uri.
builder.Services.AddSingleton<NavigationManager>(new WebviewNavigationManager());

builder.Services.AddSingleton<VsCodeBridge>();
builder.Services.AddSingleton<INotebookService, RemoteNotebookService>();

await builder.Build().RunAsync();
