using System.Text;
using Verso.Blazor.Services;

namespace Verso.Blazor.Shared.Tests;

[TestClass]
public sealed class LayoutAssetProviderTests
{
    // The server endpoint registers asset bytes into LayoutAssetCache only while a
    // notebook using the layout is mounted on a live circuit. These tests cover the
    // cold path: regenerating a built-in layout's assets with no notebook open, so the
    // /_verso/layout-assets URL keeps resolving after a fresh start or server restart.

    [TestMethod]
    public async Task TryGenerate_BuiltInNotebookLayout_ReturnsCssWithoutANotebook()
    {
        var provider = new LayoutAssetProvider();

        var result = await provider.TryGenerateAsync(
            "verso.layout.notebook", "notebook", "notebook.css");

        Assert.IsNotNull(result, "Expected the built-in notebook layout to produce notebook.css.");
        Assert.AreEqual("text/css", result.Value.ContentType);
        Assert.IsTrue(result.Value.Content.Length > 0, "Expected non-empty CSS bytes.");
        var css = Encoding.UTF8.GetString(result.Value.Content);
        Assert.IsTrue(
            css.Contains("data-extension-id=\"verso.layout.notebook\""),
            "Expected the CSS to be scoped to the notebook layout root.");
    }

    [TestMethod]
    public async Task TryGenerate_BuiltInNotebookLayout_ReturnsScript()
    {
        var provider = new LayoutAssetProvider();

        var result = await provider.TryGenerateAsync(
            "verso.layout.notebook", "notebook", "notebook.js");

        Assert.IsNotNull(result, "Expected the built-in notebook layout to produce notebook.js.");
        Assert.AreEqual("text/javascript", result.Value.ContentType);
        Assert.IsTrue(result.Value.Content.Length > 0, "Expected non-empty script bytes.");
    }

    [TestMethod]
    public async Task TryGenerate_UnknownAsset_ReturnsNull()
    {
        var provider = new LayoutAssetProvider();

        var result = await provider.TryGenerateAsync(
            "verso.layout.notebook", "notebook", "does-not-exist.css");

        Assert.IsNull(result, "An unknown asset id should not resolve.");
    }

    [TestMethod]
    public async Task TryGenerate_UnknownLayout_ReturnsNull()
    {
        var provider = new LayoutAssetProvider();

        var result = await provider.TryGenerateAsync(
            "verso.layout.bogus", "bogus", "notebook.css");

        Assert.IsNull(result, "An unknown layout identity should not resolve.");
    }
}
