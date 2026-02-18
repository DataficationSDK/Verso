using Verso.Abstractions;
using Verso.Extensions;
using Verso.Testing.Stubs;
using Verso.Testing.Fakes;

namespace Verso.Tests.Extensions;

[TestClass]
public sealed class ExtensionEnableDisableTests
{
    private ExtensionHost _host = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _host = new ExtensionHost();
        await _host.LoadBuiltInExtensionsAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _host.DisposeAsync();
    }

    [TestMethod]
    public async Task DisableExtension_ExcludesFromKernelQuery()
    {
        var kernels = _host.GetKernels();
        Assert.IsTrue(kernels.Count > 0, "Should have at least one kernel");

        var kernelExtId = kernels[0].ExtensionId;
        await _host.DisableExtensionAsync(kernelExtId);

        var filtered = _host.GetKernels();
        Assert.IsFalse(filtered.Any(k => k.ExtensionId == kernelExtId));
    }

    [TestMethod]
    public async Task EnableExtension_RestoresToKernelQuery()
    {
        var kernels = _host.GetKernels();
        var kernelExtId = kernels[0].ExtensionId;

        await _host.DisableExtensionAsync(kernelExtId);
        Assert.IsFalse(_host.GetKernels().Any(k => k.ExtensionId == kernelExtId));

        await _host.EnableExtensionAsync(kernelExtId);
        Assert.IsTrue(_host.GetKernels().Any(k => k.ExtensionId == kernelExtId));
    }

    [TestMethod]
    public async Task DisabledExtension_StillInGetLoadedExtensions()
    {
        var allBefore = _host.GetLoadedExtensions();
        Assert.IsTrue(allBefore.Count > 0);

        var extId = allBefore[0].ExtensionId;
        await _host.DisableExtensionAsync(extId);

        var allAfter = _host.GetLoadedExtensions();
        Assert.IsTrue(allAfter.Any(e => e.ExtensionId == extId),
            "Disabled extension should still appear in GetLoadedExtensions");
    }

    [TestMethod]
    public void GetExtensionInfos_ReflectsStatusAndCapabilities()
    {
        var infos = _host.GetExtensionInfos();
        Assert.IsTrue(infos.Count > 0);

        foreach (var info in infos)
        {
            Assert.IsFalse(string.IsNullOrEmpty(info.ExtensionId));
            Assert.IsFalse(string.IsNullOrEmpty(info.Name));
            Assert.AreEqual(ExtensionStatus.Enabled, info.Status);
            Assert.IsTrue(info.Capabilities.Count > 0);
        }
    }

    [TestMethod]
    public async Task GetExtensionInfos_ShowsDisabledStatus()
    {
        var infos = _host.GetExtensionInfos();
        var extId = infos[0].ExtensionId;

        await _host.DisableExtensionAsync(extId);

        var updated = _host.GetExtensionInfos();
        var disabled = updated.First(i => i.ExtensionId == extId);
        Assert.AreEqual(ExtensionStatus.Disabled, disabled.Status);
    }

    [TestMethod]
    public async Task EnableNonExistentExtension_Throws()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _host.EnableExtensionAsync("nonexistent.extension"));
    }

    [TestMethod]
    public async Task DisableNonExistentExtension_Throws()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _host.DisableExtensionAsync("nonexistent.extension"));
    }

    [TestMethod]
    public async Task OnExtensionStatusChanged_FiresOnDisable()
    {
        string? firedId = null;
        ExtensionStatus? firedStatus = null;
        _host.OnExtensionStatusChanged += (id, status) =>
        {
            firedId = id;
            firedStatus = status;
        };

        var extId = _host.GetLoadedExtensions()[0].ExtensionId;
        await _host.DisableExtensionAsync(extId);

        Assert.AreEqual(extId, firedId);
        Assert.AreEqual(ExtensionStatus.Disabled, firedStatus);
    }

    [TestMethod]
    public async Task OnExtensionStatusChanged_FiresOnEnable()
    {
        var extId = _host.GetLoadedExtensions()[0].ExtensionId;
        await _host.DisableExtensionAsync(extId);

        string? firedId = null;
        ExtensionStatus? firedStatus = null;
        _host.OnExtensionStatusChanged += (id, status) =>
        {
            firedId = id;
            firedStatus = status;
        };

        await _host.EnableExtensionAsync(extId);

        Assert.AreEqual(extId, firedId);
        Assert.AreEqual(ExtensionStatus.Enabled, firedStatus);
    }

    [TestMethod]
    public async Task DisablingOneExtension_DoesNotAffectOthers()
    {
        var allInfos = _host.GetExtensionInfos();
        if (allInfos.Count < 2)
        {
            Assert.Inconclusive("Need at least 2 extensions to test isolation");
            return;
        }

        var firstId = allInfos[0].ExtensionId;
        var secondId = allInfos[1].ExtensionId;

        await _host.DisableExtensionAsync(firstId);

        var updated = _host.GetExtensionInfos();
        var second = updated.First(i => i.ExtensionId == secondId);
        Assert.AreEqual(ExtensionStatus.Enabled, second.Status);
    }

    [TestMethod]
    public async Task DisableExtension_ExcludesFromThemeQuery()
    {
        var themes = _host.GetThemes();
        if (themes.Count == 0)
        {
            Assert.Inconclusive("No themes loaded");
            return;
        }

        var themeExtId = themes[0].ExtensionId;
        await _host.DisableExtensionAsync(themeExtId);

        var filtered = _host.GetThemes();
        Assert.IsFalse(filtered.Any(t => t.ExtensionId == themeExtId));
    }

    [TestMethod]
    public async Task DisableExtension_ExcludesFromFormatterQuery()
    {
        var formatters = _host.GetFormatters();
        if (formatters.Count == 0)
        {
            Assert.Inconclusive("No formatters loaded");
            return;
        }

        var fmtExtId = formatters[0].ExtensionId;
        await _host.DisableExtensionAsync(fmtExtId);

        var filtered = _host.GetFormatters();
        Assert.IsFalse(filtered.Any(f => f.ExtensionId == fmtExtId));
    }

    [TestMethod]
    public async Task DisableExtension_ExcludesFromSerializerQuery()
    {
        var serializers = _host.GetSerializers();
        if (serializers.Count == 0)
        {
            Assert.Inconclusive("No serializers loaded");
            return;
        }

        var serExtId = serializers[0].ExtensionId;
        await _host.DisableExtensionAsync(serExtId);

        var filtered = _host.GetSerializers();
        Assert.IsFalse(filtered.Any(s => s.ExtensionId == serExtId));
    }
}
