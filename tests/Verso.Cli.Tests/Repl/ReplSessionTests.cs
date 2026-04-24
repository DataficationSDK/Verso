using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verso.Abstractions;
using Verso.Cli.Repl;
using Verso.Execution;
using Verso.Extensions;

namespace Verso.Cli.Tests.Repl;

[TestClass]
public class ReplSessionTests
{
    [TestMethod]
    public void ConfirmDiscardUnsavedChanges_CleanSession_ReturnsTrueImmediately()
    {
        var session = BuildSession();

        Assert.IsTrue(session.ConfirmDiscardUnsavedChanges());
    }

    [TestMethod]
    public void ConfirmDiscardUnsavedChanges_FirstCallWhileDirty_WarnsAndDoesNotClearDirty()
    {
        var session = BuildSession();
        session.MarkDirty();

        Assert.IsFalse(session.ConfirmDiscardUnsavedChanges(),
            "First invocation against a dirty session must not authorize the destructive op.");
        Assert.IsTrue(session.IsDirty,
            "IsDirty must survive the warning so .exit and other guards keep firing.");
    }

    [TestMethod]
    public void ConfirmDiscardUnsavedChanges_SecondCallWhileDirty_ReturnsTrue()
    {
        var session = BuildSession();
        session.MarkDirty();
        session.ConfirmDiscardUnsavedChanges();

        Assert.IsTrue(session.ConfirmDiscardUnsavedChanges(),
            "Second invocation honors the warn-then-retry contract.");
    }

    [TestMethod]
    public void ConfirmDiscardUnsavedChanges_PendingConsentIsOneShot()
    {
        var session = BuildSession();
        session.MarkDirty();
        session.ConfirmDiscardUnsavedChanges();   // warn
        session.ConfirmDiscardUnsavedChanges();   // consent honored

        session.MarkDirty();
        Assert.IsFalse(session.ConfirmDiscardUnsavedChanges(),
            "Consent is one-shot — a new dirty cycle needs a fresh warning.");
    }

    [TestMethod]
    public void ConfirmDiscardUnsavedChanges_MarkCleanInvalidatesPriorConsent()
    {
        var session = BuildSession();
        session.MarkDirty();
        session.ConfirmDiscardUnsavedChanges();   // warn + set pending

        session.MarkClean();                      // e.g. .save
        session.MarkDirty();                      // new user work

        Assert.IsFalse(session.ConfirmDiscardUnsavedChanges(),
            "MarkClean must reset pending consent so a save-then-redirty cycle re-warns.");
    }

    [TestMethod]
    public void ConfirmDiscardUnsavedChanges_NewSubmissionInvalidatesPriorConsent()
    {
        var session = BuildSession();
        session.MarkDirty();
        session.ConfirmDiscardUnsavedChanges();   // warn + set pending

        session.MarkDirty();                      // new cell typed after the warning

        Assert.IsFalse(session.ConfirmDiscardUnsavedChanges(),
            "Typing new cells after the warning should require a fresh confirmation.");
    }

    private static ReplSession BuildSession()
    {
        var notebook = new NotebookModel();
        var host = new ExtensionHost();
        var scaffold = new Scaffold(notebook, host, null);
        return new ReplSession(notebook, scaffold, host, null);
    }
}
