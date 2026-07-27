using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Host.Dto;
using Verso.Host.Handlers;
using Verso.Host.Protocol;

namespace Verso.Host.Tests;

/// <summary>
/// Covers what a consent request carries to the client. The kind and the target decide how the
/// dialog words itself, so they have to survive the trip: a package request that arrives without
/// them renders as an extension request and tells the user the wrong thing.
/// </summary>
[TestClass]
public class ConsentNotificationTests
{
    private static async Task<(HostSession Session, NotebookSession Notebook, List<string> Sent)>
        OpenAsync()
    {
        var sent = new List<string>();
        var session = new HostSession(sent.Add);

        var openParams = JsonSerializer.SerializeToElement(
            new NotebookOpenParams { Content = "" }, JsonRpcMessage.SerializerOptions);
        var result = await NotebookHandler.HandleOpenAsync(session, openParams);

        return (session, session.GetSession(result.NotebookId), sent);
    }

    /// <summary>The params of the most recent consent notification the client would have seen.</summary>
    private static JsonElement PayloadOf(List<string> sent)
    {
        var notification = sent
            .Select(text => JsonDocument.Parse(text).RootElement)
            .Last(root => root.TryGetProperty("method", out var method)
                && method.GetString() == MethodNames.ExtensionConsentRequest);

        return notification.GetProperty("params");
    }

    private static JsonElement FirstItem(JsonElement payload)
        => payload.GetProperty("extensions").EnumerateArray().First();

    [TestMethod]
    public async Task APackageRequestCarriesItsKindAndTarget()
    {
        var (session, notebook, sent) = await OpenAsync();
        await using (session)
        {
            var request = notebook.RequestConsentAsync(
                new[]
                {
                    new ExtensionConsentInfo("opencv-python", null, "import cv2")
                    {
                        Kind = ConsentKind.Package,
                        Target = "Python 3.13.3 at /envs/demo/bin/python",
                    },
                },
                CancellationToken.None);

            var item = FirstItem(PayloadOf(sent));
            Assert.AreEqual("opencv-python", item.GetProperty("packageId").GetString());
            Assert.AreEqual("package", item.GetProperty("kind").GetString());
            Assert.AreEqual("import cv2", item.GetProperty("source").GetString());
            Assert.AreEqual(
                "Python 3.13.3 at /envs/demo/bin/python", item.GetProperty("target").GetString());

            notebook.ResolveConsent(PayloadOf(sent).GetProperty("requestId").GetString()!, false);
            Assert.IsFalse(await request);
        }
    }

    [TestMethod]
    public async Task AnExtensionRequestStillReportsItsOwnKind()
    {
        var (session, notebook, sent) = await OpenAsync();
        await using (session)
        {
            var request = notebook.RequestConsentAsync(
                new[] { new ExtensionConsentInfo("Acme.Verso.Widgets", "1.2.0") },
                CancellationToken.None);

            var item = FirstItem(PayloadOf(sent));
            Assert.AreEqual("extension", item.GetProperty("kind").GetString());
            Assert.AreEqual("cell", item.GetProperty("source").GetString());
            Assert.AreEqual("1.2.0", item.GetProperty("version").GetString());

            // There is no environment to name for an extension, and the serializer drops a
            // null rather than writing it.
            Assert.IsTrue(
                !item.TryGetProperty("target", out var target)
                || target.ValueKind == JsonValueKind.Null);

            notebook.ResolveConsent(PayloadOf(sent).GetProperty("requestId").GetString()!, true);
            Assert.IsTrue(await request);
        }
    }

    [TestMethod]
    public async Task ACancelledRequestIsRefused()
    {
        var (session, notebook, _) = await OpenAsync();
        await using (session)
        {
            using var cts = new CancellationTokenSource();
            var request = notebook.RequestConsentAsync(
                new[] { new ExtensionConsentInfo("Acme.Verso.Widgets", null) }, cts.Token);

            cts.Cancel();

            Assert.IsFalse(await request);
        }
    }

    // --- setting values from the wire ---

    [TestMethod]
    public void AListSettingArrivesAsAList()
    {
        // Without array handling the value reaches the extension as one string of brackets and
        // quotes, which is not what any list setting means.
        var value = SettingsHandler.ReadSettingValue(
            JsonSerializer.SerializeToElement(new[] { "pandas>=2", "httpx" }));

        var entries = value as List<string?>;
        Assert.IsNotNull(entries);
        CollectionAssert.AreEqual(new[] { "pandas>=2", "httpx" }, entries!.ToArray());
    }

    [TestMethod]
    public void AnEmptyListArrivesAsAnEmptyList()
    {
        var value = SettingsHandler.ReadSettingValue(
            JsonSerializer.SerializeToElement(Array.Empty<string>()));

        Assert.AreEqual(0, ((List<string?>)value!).Count);
    }

    [TestMethod]
    public void TheOtherSettingTypesAreUnchanged()
    {
        Assert.AreEqual("preview",
            SettingsHandler.ReadSettingValue(JsonSerializer.SerializeToElement("preview")));
        Assert.AreEqual(3L,
            SettingsHandler.ReadSettingValue(JsonSerializer.SerializeToElement(3)));
        Assert.AreEqual(1.5,
            SettingsHandler.ReadSettingValue(JsonSerializer.SerializeToElement(1.5)));
        Assert.AreEqual(true,
            SettingsHandler.ReadSettingValue(JsonSerializer.SerializeToElement(true)));
        Assert.IsNull(
            SettingsHandler.ReadSettingValue(JsonSerializer.SerializeToElement((string?)null)));
    }
}
