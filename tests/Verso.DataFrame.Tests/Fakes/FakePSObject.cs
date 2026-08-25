namespace System.Management.Automation;

internal sealed class PSObject
{
    public PSObject(object baseObject) => BaseObject = baseObject;

    public object BaseObject { get; }
}
