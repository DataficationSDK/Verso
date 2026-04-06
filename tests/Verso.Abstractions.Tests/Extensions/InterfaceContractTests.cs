namespace Verso.Abstractions.Tests.Extensions;

[TestClass]
public class InterfaceContractTests
{
    [TestMethod]
    [DataRow(typeof(ILanguageKernel))]
    [DataRow(typeof(ICellRenderer))]
    [DataRow(typeof(ICellType))]
    [DataRow(typeof(IToolbarAction))]
    [DataRow(typeof(IDataFormatter))]
    [DataRow(typeof(IMagicCommand))]
    [DataRow(typeof(INotebookSerializer))]
    [DataRow(typeof(ITheme))]
    [DataRow(typeof(ILayoutEngine))]
    [DataRow(typeof(ICellPropertyProvider))]
    public void ExtensionInterface_InheritsIExtension(Type extensionType)
    {
        Assert.IsTrue(typeof(IExtension).IsAssignableFrom(extensionType),
            $"{extensionType.Name} should inherit IExtension");
    }

    [TestMethod]
    public void IExtension_IsItselfExtension()
    {
        Assert.IsTrue(typeof(IExtension).IsAssignableFrom(typeof(IExtension)));
    }

    [TestMethod]
    [DataRow(typeof(IExecutionContext))]
    [DataRow(typeof(ICellRenderContext))]
    [DataRow(typeof(IToolbarActionContext))]
    [DataRow(typeof(IFormatterContext))]
    [DataRow(typeof(IMagicCommandContext))]
    public void SpecializedContext_InheritsIVersoContext(Type contextType)
    {
        Assert.IsTrue(typeof(IVersoContext).IsAssignableFrom(contextType),
            $"{contextType.Name} should inherit IVersoContext");
    }

    [TestMethod]
    public void ILanguageKernel_InheritsIAsyncDisposable()
    {
        Assert.IsTrue(typeof(IAsyncDisposable).IsAssignableFrom(typeof(ILanguageKernel)));
    }

    [TestMethod]
    public void AllTenExtensionInterfaces_ArePresent()
    {
        var extensionInterfaces = typeof(IExtension).Assembly
            .GetTypes()
            .Where(t => t.IsInterface && typeof(IExtension).IsAssignableFrom(t))
            .ToList();

        // IExtension + 11 derived = 12
        Assert.AreEqual(12, extensionInterfaces.Count,
            $"Expected 11 extension interfaces, found: {string.Join(", ", extensionInterfaces.Select(i => i.Name))}");
    }

    [TestMethod]
    public void AllFiveSpecializedContexts_ArePresent()
    {
        var contextInterfaces = typeof(IVersoContext).Assembly
            .GetTypes()
            .Where(t => t.IsInterface && t != typeof(IVersoContext) && typeof(IVersoContext).IsAssignableFrom(t))
            .ToList();

        Assert.AreEqual(5, contextInterfaces.Count,
            $"Expected 5 specialized contexts, found: {string.Join(", ", contextInterfaces.Select(i => i.Name))}");
    }

    [TestMethod]
    public void IMagicCommandContext_SuppressExecution_HasSetter()
    {
        var prop = typeof(IMagicCommandContext).GetProperty(nameof(IMagicCommandContext.SuppressExecution));
        Assert.IsNotNull(prop);
        Assert.IsTrue(prop!.CanRead);
        Assert.IsTrue(prop.CanWrite, "SuppressExecution must have a setter");
    }

    [TestMethod]
    public void ICellRenderer_DefinesCollapsesInputOnExecute()
    {
        var prop = typeof(ICellRenderer).GetProperty(nameof(ICellRenderer.CollapsesInputOnExecute));
        Assert.IsNotNull(prop, "ICellRenderer should define CollapsesInputOnExecute property");
        Assert.AreEqual(typeof(bool), prop!.PropertyType);
    }

    [TestMethod]
    public void ICellRenderer_DefinesDefaultVisibility()
    {
        var prop = typeof(ICellRenderer).GetProperty(nameof(ICellRenderer.DefaultVisibility));
        Assert.IsNotNull(prop, "ICellRenderer should define DefaultVisibility property");
        Assert.AreEqual(typeof(CellVisibilityHint), prop!.PropertyType);
    }

    [TestMethod]
    public void ILayoutEngine_DefinesSupportedVisibilityStates()
    {
        var prop = typeof(ILayoutEngine).GetProperty(nameof(ILayoutEngine.SupportedVisibilityStates));
        Assert.IsNotNull(prop, "ILayoutEngine should define SupportedVisibilityStates property");
        Assert.AreEqual(typeof(IReadOnlySet<CellVisibilityState>), prop!.PropertyType);
    }

    [TestMethod]
    public void ILayoutEngine_DefinesSupportsPropertiesPanel()
    {
        var prop = typeof(ILayoutEngine).GetProperty(nameof(ILayoutEngine.SupportsPropertiesPanel));
        Assert.IsNotNull(prop, "ILayoutEngine should define SupportsPropertiesPanel property");
        Assert.AreEqual(typeof(bool), prop!.PropertyType);
    }

    [TestMethod]
    public void ICellInteractionHandler_DoesNotInheritIExtension()
    {
        Assert.IsFalse(typeof(IExtension).IsAssignableFrom(typeof(ICellInteractionHandler)),
            "ICellInteractionHandler is supplemental and must not inherit IExtension");
    }

    [TestMethod]
    public void CellInteractionContext_HasRequiredProperties()
    {
        var ctx = new CellInteractionContext
        {
            Region = CellRegion.Output,
            InteractionType = "click",
            Payload = "{\"page\":2}",
            OutputBlockId = "block-1",
            CellId = Guid.NewGuid(),
            ExtensionId = "com.test.handler"
        };

        Assert.AreEqual(CellRegion.Output, ctx.Region);
        Assert.AreEqual("click", ctx.InteractionType);
        Assert.AreEqual("{\"page\":2}", ctx.Payload);
        Assert.AreEqual("block-1", ctx.OutputBlockId);
        Assert.AreEqual("com.test.handler", ctx.ExtensionId);
    }

    [TestMethod]
    public void CellRegion_HasExpectedValues()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(CellRegion), CellRegion.Input));
        Assert.IsTrue(Enum.IsDefined(typeof(CellRegion), CellRegion.Output));
        Assert.AreEqual(2, Enum.GetValues(typeof(CellRegion)).Length);
    }
}
