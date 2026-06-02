using Verso.Ado.Helpers;

namespace Verso.Ado.Tests.Helpers;

[TestClass]
public sealed class SqlParameterScannerTests
{
    [TestMethod]
    public void Scan_BasicParam_Found()
    {
        var refs = SqlParameterScanner.Scan("SELECT @x", SqlDialect.SqlServer);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("x", refs[0].Name);
        Assert.AreEqual(7, refs[0].Offset);
        Assert.AreEqual(2, refs[0].Length);
    }

    [TestMethod]
    public void Scan_GlobalVariable_Excluded()
    {
        var refs = SqlParameterScanner.Scan("SELECT @@SPID", SqlDialect.SqlServer);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_MixedGlobalAndParam_OnlyParam()
    {
        var refs = SqlParameterScanner.Scan(
            "WHERE id = @u AND @@SPID > 0", SqlDialect.SqlServer);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("u", refs[0].Name);
    }

    [TestMethod]
    public void Scan_ParamInSingleLineComment_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "-- @ignored\nSELECT @keep", SqlDialect.SqlServer);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("keep", refs[0].Name);
    }

    [TestMethod]
    public void Scan_ParamInBlockComment_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "/* @ignored */ SELECT @keep", SqlDialect.SqlServer);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("keep", refs[0].Name);
    }

    [TestMethod]
    public void Scan_ParamInBlockCommentSpanningLines_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "/* line one\n@ignored\nline three */ SELECT @keep", SqlDialect.SqlServer);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("keep", refs[0].Name);
    }

    [TestMethod]
    public void Scan_ParamInSingleQuote_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "INSERT INTO Emails VALUES ('alice@example.com')", SqlDialect.SqlServer);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_ParamInNUnicode_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "SELECT N'hello @world' AS msg", SqlDialect.SqlServer);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_ParamInDoubleQuotedIdentifier_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "SELECT \"@col\" FROM T", SqlDialect.SqlServer);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_ParamInBracketedIdentifier_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "SELECT [@col] FROM T", SqlDialect.SqlServer);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_DoubledSingleQuoteInString_Excluded()
    {
        // String contents: it's @local — the @local is inside the string.
        var refs = SqlParameterScanner.Scan(
            "SELECT 'it''s @local' AS phrase", SqlDialect.SqlServer);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_Oracle_AtParam_NotRecognized()
    {
        // Oracle binds with ':', not '@'. An '@' token must not be treated as a
        // parameter under the Oracle dialect.
        var refs = SqlParameterScanner.Scan(
            "SELECT * FROM T WHERE Id = @x", SqlDialect.Oracle);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_Oracle_ColonParam_Found()
    {
        var refs = SqlParameterScanner.Scan(
            "SELECT * FROM T WHERE Id = :userId", SqlDialect.Oracle);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("userId", refs[0].Name);
        Assert.AreEqual(27, refs[0].Offset);
        Assert.AreEqual(7, refs[0].Length); // ":userId"
    }

    [TestMethod]
    public void Scan_Oracle_MultipleColonParams_AllFound()
    {
        var refs = SqlParameterScanner.Scan(
            "WHERE a = :a AND b = :b AND c = :c", SqlDialect.Oracle);

        Assert.AreEqual(3, refs.Count);
        CollectionAssert.AreEqual(
            new[] { "a", "b", "c" },
            refs.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void Scan_Oracle_RepeatedColonParam_EmittedEachOccurrence()
    {
        // The scanner reports every occurrence; the binder deduplicates by name.
        var refs = SqlParameterScanner.Scan(
            "WHERE a = :x OR b = :x", SqlDialect.Oracle);

        Assert.AreEqual(2, refs.Count);
        Assert.AreEqual("x", refs[0].Name);
        Assert.AreEqual("x", refs[1].Name);
    }

    [TestMethod]
    public void Scan_Oracle_AssignmentOperator_NotEmitted()
    {
        // ':=' is the PL/SQL assignment operator, not a bind.
        var refs = SqlParameterScanner.Scan(
            "BEGIN total := 0; END;", SqlDialect.Oracle);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_Oracle_PositionalBind_NotEmitted()
    {
        // ':1' is a positional bind, not a named one — the name must start with
        // a letter or underscore.
        var refs = SqlParameterScanner.Scan(
            "SELECT * FROM T WHERE Id = :1", SqlDialect.Oracle);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_Oracle_TriggerPseudorecords_NotEmitted()
    {
        // ':new' and ':old' are trigger correlation names, not binds.
        var refs = SqlParameterScanner.Scan(
            "IF :new.salary > :old.salary THEN NULL; END IF;", SqlDialect.Oracle);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_Oracle_ColonInStringLiteral_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "SELECT 'time is :value' FROM dual", SqlDialect.Oracle);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_Oracle_ColonInComment_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "-- bind :ignored here\nSELECT :keep FROM dual", SqlDialect.Oracle);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("keep", refs[0].Name);
    }

    [TestMethod]
    public void Scan_Oracle_ColonInQuotedIdentifier_Excluded()
    {
        var refs = SqlParameterScanner.Scan(
            "SELECT \":col\" FROM T WHERE Id = :keep", SqlDialect.Oracle);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("keep", refs[0].Name);
    }

    [TestMethod]
    public void Scan_Postgres_DoubleColonCast_NotEmitted()
    {
        // Regression guard: ':' is only a bind prefix for Oracle. A PostgreSQL
        // '::' cast must not be scanned as a parameter, and '@' still works.
        var refs = SqlParameterScanner.Scan(
            "SELECT value::int FROM T WHERE Id = @id", SqlDialect.Postgres);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("id", refs[0].Name);
    }

    [TestMethod]
    public void Scan_SqlServer_ColonNotTreatedAsParam()
    {
        var refs = SqlParameterScanner.Scan(
            "SELECT * FROM T WHERE Id = :notAParam", SqlDialect.SqlServer);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_MultipleParams_AllFound()
    {
        var refs = SqlParameterScanner.Scan(
            "WHERE a = @a AND b = @b AND c = @c", SqlDialect.SqlServer);

        Assert.AreEqual(3, refs.Count);
        CollectionAssert.AreEqual(
            new[] { "a", "b", "c" },
            refs.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void Scan_EmptyString_Empty()
    {
        Assert.AreEqual(0, SqlParameterScanner.Scan("", SqlDialect.SqlServer).Count);
        Assert.AreEqual(0, SqlParameterScanner.Scan(null!, SqlDialect.SqlServer).Count);
    }

    [TestMethod]
    public void Scan_ParamFollowedByOperator_TerminatesName()
    {
        var refs = SqlParameterScanner.Scan("SELECT @x+1", SqlDialect.SqlServer);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("x", refs[0].Name);
        Assert.AreEqual(2, refs[0].Length);
    }

    [TestMethod]
    public void Scan_ParamAtEndOfInput_Found()
    {
        var refs = SqlParameterScanner.Scan("SELECT @final", SqlDialect.SqlServer);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("final", refs[0].Name);
    }

    [TestMethod]
    public void Scan_AtWithNoName_NotEmitted()
    {
        var refs = SqlParameterScanner.Scan("SELECT @ FROM T", SqlDialect.SqlServer);

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void Scan_WordPrefixBeforeApostrophe_NotTreatedAsNUnicode()
    {
        // FOO'bar' — the apostrophe is a regular string, the FOO is an identifier.
        // Crucially the `O` directly before the quote must NOT be treated as the
        // `N` prefix for a unicode literal.
        var refs = SqlParameterScanner.Scan(
            "SELECT FOO'@inside' AS @keep FROM T", SqlDialect.SqlServer);

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("keep", refs[0].Name);
    }
}
