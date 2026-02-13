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

        // IExtension + 9 derived = 10
        Assert.AreEqual(10, extensionInterfaces.Count,
            $"Expected 10 extension interfaces, found: {string.Join(", ", extensionInterfaces.Select(i => i.Name))}");
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
}
