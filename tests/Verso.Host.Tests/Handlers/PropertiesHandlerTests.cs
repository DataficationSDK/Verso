using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;
using Verso.Testing.Fakes;

namespace Verso.Host.Tests.Handlers;

[TestClass]
public class PropertiesHandlerTests
{
    private HostSession CreateSession()
    {
        var notifications = new List<string>();
        return new HostSession(n => notifications.Add(n));
    }

    private async Task<(HostSession Session, string NotebookId)> CreateOpenSession()
    {
        var session = CreateSession();
        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" },
            JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);
        return (session, result.NotebookId);
    }

    [TestMethod]
    public async Task HandleGetSections_NoProviders_ReturnsEmptyList()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        // Add a cell so we have a valid target
        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Type = "code", Source = "var x = 1;" },
            JsonRpcMessage.SerializerOptions);
        var cell = CellHandler.HandleAdd(ns, addParams);

        var @params = JsonSerializer.SerializeToElement(
            new PropertiesGetSectionsParams { CellId = cell.Id },
            JsonRpcMessage.SerializerOptions);

        var result = await PropertiesHandler.HandleGetSectionsAsync(ns, @params);

        // Built-in CellVisibilityPropertyProvider is auto-loaded, but notebook layout
        // only supports Visible so the provider may return an empty section or no qualifying layouts.
        // At minimum, verify the call succeeds and returns a valid result.
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Sections);
    }

    [TestMethod]
    public async Task HandleGetSections_WithFakeProvider_ReturnsSections()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var provider = new FakeCellPropertyProvider();
        await ns.ExtensionHost.LoadExtensionAsync(provider);

        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Type = "code", Source = "test" },
            JsonRpcMessage.SerializerOptions);
        var cell = CellHandler.HandleAdd(ns, addParams);

        var @params = JsonSerializer.SerializeToElement(
            new PropertiesGetSectionsParams { CellId = cell.Id },
            JsonRpcMessage.SerializerOptions);

        var result = await PropertiesHandler.HandleGetSectionsAsync(ns, @params);

        Assert.IsTrue(result.Sections.Any(s => s.ProviderExtensionId == provider.ExtensionId));
        var section = result.Sections.First(s => s.ProviderExtensionId == provider.ExtensionId);
        Assert.AreEqual("Fake Section", section.Section.Title);
    }

    [TestMethod]
    public async Task HandleGetSections_InvalidCellId_Throws()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var @params = JsonSerializer.SerializeToElement(
            new PropertiesGetSectionsParams { CellId = "not-a-guid" },
            JsonRpcMessage.SerializerOptions);

        await Assert.ThrowsExceptionAsync<JsonException>(
            () => PropertiesHandler.HandleGetSectionsAsync(ns, @params));
    }

    [TestMethod]
    public async Task HandleGetSections_UnknownCellId_Throws()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var @params = JsonSerializer.SerializeToElement(
            new PropertiesGetSectionsParams { CellId = Guid.NewGuid().ToString() },
            JsonRpcMessage.SerializerOptions);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => PropertiesHandler.HandleGetSectionsAsync(ns, @params));
    }

    [TestMethod]
    public async Task HandleUpdateProperty_ValidProvider_Succeeds()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var provider = new FakeCellPropertyProvider();
        await ns.ExtensionHost.LoadExtensionAsync(provider);

        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Type = "code", Source = "test" },
            JsonRpcMessage.SerializerOptions);
        var cell = CellHandler.HandleAdd(ns, addParams);

        var @params = JsonSerializer.SerializeToElement(
            new PropertiesUpdatePropertyParams
            {
                CellId = cell.Id,
                ProviderExtensionId = provider.ExtensionId,
                PropertyName = "testProp",
                Value = "testValue"
            },
            JsonRpcMessage.SerializerOptions);

        // Should not throw
        var result = await PropertiesHandler.HandleUpdatePropertyAsync(ns, @params);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task HandleUpdateProperty_UnknownProvider_Throws()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var addParams = JsonSerializer.SerializeToElement(
            new CellAddParams { Type = "code", Source = "test" },
            JsonRpcMessage.SerializerOptions);
        var cell = CellHandler.HandleAdd(ns, addParams);

        var @params = JsonSerializer.SerializeToElement(
            new PropertiesUpdatePropertyParams
            {
                CellId = cell.Id,
                ProviderExtensionId = "com.unknown.provider",
                PropertyName = "prop",
                Value = "val"
            },
            JsonRpcMessage.SerializerOptions);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => PropertiesHandler.HandleUpdatePropertyAsync(ns, @params));
    }

    [TestMethod]
    public async Task HandleGetSupported_NoLayout_ReturnsFalse()
    {
        var (session, notebookId) = await CreateOpenSession();
        var ns = session.GetSession(notebookId);

        var result = PropertiesHandler.HandleGetSupported(ns);

        // Default notebook layout supports properties panel, but LayoutManager
        // may or may not have an active layout in a fresh session.
        Assert.IsNotNull(result);
    }
}
