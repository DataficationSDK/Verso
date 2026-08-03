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

// The interface language is not set here. WebAssembly fetches satellite assemblies and picks
// its globalization data while the runtime boots, and changing culture afterwards is rejected
// unless the whole ICU dataset is downloaded. So the host page passes the language to
// Blazor.start as applicationCulture instead, and a language change reloads the app.
await builder.Build().RunAsync();
