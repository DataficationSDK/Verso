using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Host.Protocol;

namespace Verso.Host.Tests;

[TestClass]
public class JsonRpcMessageTests
{
    [TestMethod]
    public void Response_WithResult_ContainsAllFields()
    {
        var json = JsonRpcMessage.Response(1, new { value = "hello" });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.AreEqual(1, root.GetProperty("id").GetInt64());
        Assert.AreEqual("hello", root.GetProperty("result").GetProperty("value").GetString());
    }

    [TestMethod]
    public void Response_WithNullResult_HasNullResult()
    {
        var json = JsonRpcMessage.Response(1, null);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("result").ValueKind);
    }

    [TestMethod]
    public void Error_ContainsErrorObject()
    {
        var json = JsonRpcMessage.Error(42, -32601, "Method not found");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.AreEqual(42, root.GetProperty("id").GetInt64());

        var error = root.GetProperty("error");
        Assert.AreEqual(-32601, error.GetProperty("code").GetInt32());
        Assert.AreEqual("Method not found", error.GetProperty("message").GetString());
    }

    [TestMethod]
    public void Error_WithData_IncludesData()
    {
        var json = JsonRpcMessage.Error(1, -32600, "Invalid", new { detail = "missing field" });
        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");

        Assert.AreEqual("missing field", error.GetProperty("data").GetProperty("detail").GetString());
    }

    [TestMethod]
    public void Notification_HasNoId()
    {
        var json = JsonRpcMessage.Notification("host/ready", new { version = "0.5.0" });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.AreEqual("host/ready", root.GetProperty("method").GetString());
        Assert.AreEqual("0.5.0", root.GetProperty("params").GetProperty("version").GetString());
        Assert.IsFalse(root.TryGetProperty("id", out _));
    }

    [TestMethod]
    public void Parse_Request_ExtractsIdAndMethod()
    {
        var json = @"{""jsonrpc"":""2.0"",""id"":5,""method"":""cell/list"",""params"":{}}";
        var (id, method, @params) = JsonRpcMessage.Parse(json);

        Assert.AreEqual(5L, id);
        Assert.AreEqual("cell/list", method);
        Assert.IsNotNull(@params);
    }

    [TestMethod]
    public void Parse_StringId_ExtractsAsString()
    {
        var json = @"{""jsonrpc"":""2.0"",""id"":""abc"",""method"":""cell/list""}";
        var (id, method, _) = JsonRpcMessage.Parse(json);

        Assert.AreEqual("abc", id);
        Assert.AreEqual("cell/list", method);
    }

    [TestMethod]
    public void Parse_Notification_HasNullId()
    {
        var json = @"{""jsonrpc"":""2.0"",""method"":""host/ready"",""params"":{""version"":""0.5.0""}}";
        var (id, method, @params) = JsonRpcMessage.Parse(json);

        Assert.IsNull(id);
        Assert.AreEqual("host/ready", method);
        Assert.IsNotNull(@params);
    }

    [TestMethod]
    public void MethodNames_AreCorrectStrings()
    {
        Assert.AreEqual("host/ready", MethodNames.HostReady);
        Assert.AreEqual("host/shutdown", MethodNames.HostShutdown);
        Assert.AreEqual("notebook/open", MethodNames.NotebookOpen);
        Assert.AreEqual("notebook/save", MethodNames.NotebookSave);
        Assert.AreEqual("cell/add", MethodNames.CellAdd);
        Assert.AreEqual("cell/list", MethodNames.CellList);
        Assert.AreEqual("execution/run", MethodNames.ExecutionRun);
        Assert.AreEqual("execution/runAll", MethodNames.ExecutionRunAll);
        Assert.AreEqual("kernel/restart", MethodNames.KernelRestart);
        Assert.AreEqual("kernel/getCompletions", MethodNames.KernelGetCompletions);
        Assert.AreEqual("kernel/getDiagnostics", MethodNames.KernelGetDiagnostics);
        Assert.AreEqual("kernel/getHoverInfo", MethodNames.KernelGetHoverInfo);
        Assert.AreEqual("output/clearAll", MethodNames.OutputClearAll);
    }
}
