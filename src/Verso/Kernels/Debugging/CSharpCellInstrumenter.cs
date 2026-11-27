using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Verso.Kernels.Debugging;

/// <summary>
/// Makes a C# cell pausable by adding a check ahead of each of its statements. The check asks
/// <see cref="CellProbes"/> whether to stop and, when it should, hands over the values of the
/// variables in scope.
/// </summary>
/// <remarks>
/// <para>
/// Every check is written on the same line as the statement it guards, so line numbers in the
/// rewritten cell are the cell's own. Nothing is added to a statement itself, only before it, and
/// a statement that is the whole body of an <c>if</c> or a loop gets braces so there is somewhere
/// to put the check.
/// </para>
/// <para>
/// Statements are the unit: a lambda or a member written as a single expression has none, so a
/// step passes over it. Capturing a variable means reading it, which is why only variables that
/// are certain to hold a value at that point are captured, and why a value that cannot be boxed
/// (a span, a pointer) is left out.
/// </para>
/// </remarks>
internal static class CSharpCellInstrumenter
{
    private const string Probes = "global::Verso.Kernels.Debugging.CellProbes";
    private const string FramePrefix = "__verso_frame";

    // A cell that touches a great many notebook variables would otherwise repeat every one of
    // them at every statement.
    private const int MaxCapturedPerStatement = 96;

    /// <summary>
    /// Works out where a cell can stop. Returns <c>null</c> when the cell has no statement to stop at.
    /// </summary>
    /// <param name="compilation">The cell compiled as a script submission, earlier cells included.</param>
    /// <param name="code">The cell's text, used to pick its tree out of the compilation.</param>
    /// <param name="captureVariables">Whether stops should report the variables in scope.</param>
    public static Plan? Analyze(
        Compilation compilation, string code, bool captureVariables, CancellationToken cancellationToken)
    {
        // A cell that pulls in other files with #load compiles to several trees. The cell is the
        // one carrying its text.
        SyntaxTree? tree = null;
        foreach (var candidate in compilation.SyntaxTrees)
        {
            if (candidate.GetText(cancellationToken).ToString() != code) continue;
            if (tree is not null) return null;
            tree = candidate;
        }

        if (tree is null || tree.GetRoot(cancellationToken) is not CompilationUnitSyntax root)
            return null;

        var analysis = new Analysis(compilation.GetSemanticModel(tree), root, captureVariables, cancellationToken);
        analysis.Run();
        return analysis.Sites.Count == 0 ? null : new Plan(root, analysis);
    }

    /// <summary>
    /// Where one cell can stop, ready to be written out once the handle its code will use is known.
    /// </summary>
    internal sealed class Plan
    {
        private readonly CompilationUnitSyntax _root;
        private readonly Analysis _analysis;

        internal Plan(CompilationUnitSyntax root, Analysis analysis)
        {
            _root = root;
            _analysis = analysis;
            Probes = analysis.Sites.Values.OrderBy(site => site.Index).Select(site => site.Info).ToArray();
            Methods = analysis.FunctionNames.ToArray();
        }

        public ProbeInfo[] Probes { get; }
        public string[] Methods { get; }

        /// <summary>
        /// The cell's code with the checks added.
        /// </summary>
        public string Emit(int handle)
            => new Emitter(_analysis, handle).Visit(_root)!.ToFullString();
    }

    /// <summary>
    /// One statement the cell can stop at.
    /// </summary>
    /// <param name="Index">The statement's number within the cell, which its check passes back.</param>
    /// <param name="Function">Index of the enclosing function, or -1 outside any function.</param>
    /// <param name="Embedded">Whether the statement is the whole body of a branch or loop, written without braces.</param>
    /// <param name="Info">What a stop at this statement reports.</param>
    /// <param name="Expressions">What to write in the cell to read each captured variable, in step with <see cref="ProbeInfo.Names"/>.</param>
    internal sealed record Site(int Index, int Function, bool Embedded, ProbeInfo Info, string[] Expressions);

    // ------------------------------------------------------------------ analysis

    internal sealed class Analysis
    {
        private readonly SemanticModel _model;
        private readonly CompilationUnitSyntax _root;
        private readonly bool _capture;
        private readonly CancellationToken _ct;

        // Notebook variables this cell declares, with where each declaration starts.
        private readonly List<(IFieldSymbol Field, int Position)> _ownFields = new();

        // Notebook variables each top-level member mentions, the cell's own and earlier cells' alike.
        private readonly Dictionary<SyntaxNode, List<IFieldSymbol>> _mentionedBy = new();

        // Earlier cells' variables mentioned anywhere in this cell.
        private readonly List<IFieldSymbol> _mentionedEarlier = new();

        public Analysis(SemanticModel model, CompilationUnitSyntax root, bool capture, CancellationToken ct)
        {
            _model = model;
            _root = root;
            _capture = capture;
            _ct = ct;
        }

        public Dictionary<SyntaxNode, Site> Sites { get; } = new();
        public Dictionary<SyntaxNode, int> Functions { get; } = new();
        public List<string> FunctionNames { get; } = new();

        public void Run()
        {
            if (_capture)
                CollectNotebookVariables();

            foreach (var node in _root.DescendantNodes())
            {
                _ct.ThrowIfCancellationRequested();

                var embedded = false;
                if (node is FieldDeclarationSyntax field)
                {
                    // A top-level `var x = ...;` in a script is a field, not a statement.
                    if (field.Parent != _root || field.Modifiers.Any(SyntaxKind.ConstKeyword)
                        || !field.Declaration.Variables.Any(v => v.Initializer is not null))
                        continue;
                }
                else if (node is StatementSyntax statement)
                {
                    if (!IsStoppable(statement, out embedded))
                        continue;
                }
                else
                {
                    continue;
                }

                var function = EnclosingFunction(node);
                var functionIndex = -1;
                if (function is not null && !Functions.TryGetValue(function, out functionIndex))
                {
                    functionIndex = FunctionNames.Count;
                    Functions[function] = functionIndex;
                    FunctionNames.Add(DescribeFunction(function));
                }

                var (names, types, expressions) = _capture
                    ? Capture(node, function)
                    : (Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
                var lines = node.SyntaxTree.GetLineSpan(node.Span, _ct);
                var line = lines.StartLinePosition.Line + 1;
                var endLine = IsCompound(node) ? line : lines.EndLinePosition.Line + 1;

                Sites[node] = new Site(
                    Sites.Count,
                    functionIndex,
                    embedded,
                    new ProbeInfo(line, endLine, functionIndex, names, types),
                    expressions);
            }
        }

        private static bool IsStoppable(StatementSyntax statement, out bool embedded)
        {
            embedded = false;

            switch (statement)
            {
                case BlockSyntax or LocalFunctionStatementSyntax or EmptyStatementSyntax:
                    return false;
                case LocalDeclarationStatementSyntax declaration
                    when declaration.IsConst || !declaration.Declaration.Variables.Any(v => v.Initializer is not null):
                    return false;
            }

            switch (statement.Parent)
            {
                case BlockSyntax or GlobalStatementSyntax or SwitchSectionSyntax:
                    return true;

                // The whole body of a branch or a loop, written without braces.
                case IfStatementSyntax parent when parent.Statement == statement:
                case ElseClauseSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case ForStatementSyntax:
                case CommonForEachStatementSyntax:
                case UsingStatementSyntax:
                case LockStatementSyntax:
                case FixedStatementSyntax:
                    embedded = true;
                    return true;

                // Under a label there is nowhere to add a check without changing what the label
                // names or the scope of what follows it.
                default:
                    return false;
            }
        }

        private static bool IsCompound(SyntaxNode node) => node is
            IfStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax
            or DoStatementSyntax or TryStatementSyntax or SwitchStatementSyntax or UsingStatementSyntax
            or LockStatementSyntax or FixedStatementSyntax or CheckedStatementSyntax or UnsafeStatementSyntax
            or LabeledStatementSyntax;

        /// <summary>The nearest function written with a body of statements, or null at the top level.</summary>
        private static SyntaxNode? EnclosingFunction(SyntaxNode node)
        {
            for (var current = node.Parent; current is not null; current = current.Parent)
            {
                switch (current)
                {
                    case BaseMethodDeclarationSyntax { Body: not null }:
                    case AccessorDeclarationSyntax { Body: not null }:
                    case LocalFunctionStatementSyntax { Body: not null }:
                    case AnonymousFunctionExpressionSyntax { Block: not null }:
                        return current;
                }
            }
            return null;
        }

        private static string DescribeFunction(SyntaxNode function)
        {
            var name = function switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText,
                ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
                DestructorDeclarationSyntax destructor => "~" + destructor.Identifier.ValueText,
                OperatorDeclarationSyntax op => "operator " + op.OperatorToken.Text,
                ConversionOperatorDeclarationSyntax conversion => "operator " + conversion.Type,
                LocalFunctionStatementSyntax local => local.Identifier.ValueText,
                AccessorDeclarationSyntax accessor => accessor.Parent?.Parent switch
                {
                    PropertyDeclarationSyntax property => property.Identifier.ValueText,
                    EventDeclarationSyntax @event => @event.Identifier.ValueText,
                    IndexerDeclarationSyntax => "this[]",
                    _ => "",
                } + "." + accessor.Keyword.Text,
                _ => "lambda",
            };

            if (function is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                return name;

            var types = function.Ancestors().OfType<BaseTypeDeclarationSyntax>()
                .Select(type => type.Identifier.ValueText).Reverse().ToList();
            return types.Count == 0 ? name : string.Join(".", types) + "." + name;
        }

        // -------------------------------------------------------------- variables

        private void CollectNotebookVariables()
        {
            foreach (var node in _root.DescendantNodes())
            {
                _ct.ThrowIfCancellationRequested();

                switch (node)
                {
                    case VariableDeclaratorSyntax or SingleVariableDesignationSyntax
                        when _model.GetDeclaredSymbol(node, _ct) is IFieldSymbol declared && IsNotebookVariable(declared):
                        _ownFields.Add((declared, node.SpanStart));
                        break;

                    case IdentifierNameSyntax identifier
                        when _model.GetSymbolInfo(identifier, _ct).Symbol is IFieldSymbol mentioned
                             && IsNotebookVariable(mentioned):
                    {
                        var member = TopLevelMember(identifier);
                        if (member is null) break;

                        if (!_mentionedBy.TryGetValue(member, out var list))
                            _mentionedBy[member] = list = new List<IFieldSymbol>();
                        if (!list.Contains(mentioned, SymbolEqualityComparer.Default))
                            list.Add(mentioned);

                        if (mentioned.DeclaringSyntaxReferences.All(r => r.SyntaxTree != _root.SyntaxTree)
                            && !_mentionedEarlier.Contains(mentioned, SymbolEqualityComparer.Default))
                            _mentionedEarlier.Add(mentioned);
                        break;
                    }
                }
            }
        }

        private static bool IsNotebookVariable(IFieldSymbol field)
            => field.ContainingType is { IsScriptClass: true } && !field.IsImplicitlyDeclared && !field.IsConst;

        private SyntaxNode? TopLevelMember(SyntaxNode node)
        {
            for (var current = node; current is not null; current = current.Parent)
            {
                if (current.Parent == _root) return current;
            }
            return null;
        }

        private (string[] Names, string[] Types, string[] Expressions) Capture(SyntaxNode site, SyntaxNode? function)
        {
            var names = new List<string>();
            var types = new List<string>();
            var expressions = new List<string>();

            // Written with a leading @ so a variable named after a keyword still reads as a name.
            void Add(string name, ITypeSymbol type, string? expression = null)
            {
                expression ??= "@" + name;
                if (names.Count >= MaxCapturedPerStatement || expressions.Contains(expression)) return;
                names.Add(name);
                types.Add(type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                expressions.Add(expression);
            }

            // Locals and parameters that are certain to hold a value when the statement starts.
            if (site is StatementSyntax statement)
            {
                DataFlowAnalysis? flow = null;
                try
                {
                    flow = _model.AnalyzeDataFlow(statement);
                }
                catch (ArgumentException)
                {
                    // Not a region flow analysis accepts. The statement still stops, without locals.
                }

                if (flow is { Succeeded: true })
                {
                    var assigned = flow.DefinitelyAssignedOnEntry
                        .Where(symbol => symbol is ILocalSymbol or IParameterSymbol { IsThis: false })
                        .OrderBy(symbol => symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0);

                    foreach (var symbol in assigned)
                    {
                        var type = symbol is ILocalSymbol local ? local.Type : ((IParameterSymbol)symbol).Type;
                        if (symbol.Name.Length == 0 || symbol.Name.StartsWith(FramePrefix, StringComparison.Ordinal)
                            || !CanBox(type) || !IsVisibleAt(site, symbol) || CrossesStaticBoundary(site, symbol.ContainingSymbol))
                            continue;

                        Add(symbol.Name, type);
                    }
                }
            }

            var enclosingType = site.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (enclosingType is not null)
            {
                // Inside a type the cell declares, notebook variables are out of reach and `this` is
                // what there is to show.
                if (CanCaptureThis(site, enclosingType) && _model.GetDeclaredSymbol(enclosingType, _ct) is { } self)
                    Add("this", self, expression: "this");
            }
            else if (!CrossesStaticBoundary(site, owner: null) && !IsStaticTopLevelMember(site))
            {
                var position = site.SpanStart;
                if (function is null)
                {
                    foreach (var (field, declaredAt) in _ownFields)
                    {
                        if (declaredAt < position && IsVisibleAt(site, field))
                            Add(field.Name, field.Type);
                    }

                    foreach (var field in _mentionedEarlier)
                    {
                        if (IsVisibleAt(site, field))
                            Add(field.Name, field.Type);
                    }
                }
                else if (TopLevelMember(site) is { } member && _mentionedBy.TryGetValue(member, out var mentioned))
                {
                    foreach (var field in mentioned)
                    {
                        if (IsVisibleAt(site, field))
                            Add(field.Name, field.Type);
                    }
                }
            }

            return (names.ToArray(), types.ToArray(), expressions.ToArray());
        }

        private static bool CanBox(ITypeSymbol type)
        {
            if (type.IsRefLikeType || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Error)
                return false;
            if (type is ITypeParameterSymbol { AllowsRefLikeType: true })
                return false;
            return type.SpecialType is not (SpecialType.System_TypedReference or SpecialType.System_ArgIterator
                or SpecialType.System_RuntimeArgumentHandle or SpecialType.System_Void);
        }

        /// <summary>Whether a name written at the site would mean this symbol, rather than nothing or something that hides it.</summary>
        private bool IsVisibleAt(SyntaxNode site, ISymbol symbol)
        {
            foreach (var found in _model.LookupSymbols(site.SpanStart, name: symbol.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(found, symbol))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a static lambda or static local function sits between the site and whatever owns
        /// the thing being captured. With no owner, any static function on the way out counts.
        /// </summary>
        private bool CrossesStaticBoundary(SyntaxNode site, ISymbol? owner)
        {
            for (var current = site.Parent; current is not null; current = current.Parent)
            {
                ISymbol? symbol;
                bool isStatic;
                switch (current)
                {
                    case LocalFunctionStatementSyntax local:
                        symbol = _model.GetDeclaredSymbol(local, _ct);
                        isStatic = local.Modifiers.Any(SyntaxKind.StaticKeyword);
                        break;
                    case AnonymousFunctionExpressionSyntax lambda:
                        symbol = _model.GetSymbolInfo(lambda, _ct).Symbol;
                        isStatic = lambda.Modifiers.Any(SyntaxKind.StaticKeyword);
                        break;
                    case MemberDeclarationSyntax:
                        return false;
                    default:
                        continue;
                }

                if (owner is not null && SymbolEqualityComparer.Default.Equals(symbol, owner))
                    return false;
                if (isStatic)
                    return true;
            }
            return false;
        }

        private bool IsStaticTopLevelMember(SyntaxNode site)
            => TopLevelMember(site) is BaseMethodDeclarationSyntax method && method.Modifiers.Any(SyntaxKind.StaticKeyword);

        private bool CanCaptureThis(SyntaxNode site, TypeDeclarationSyntax type)
        {
            if (type.Modifiers.Any(SyntaxKind.StaticKeyword) || type.Modifiers.Any(SyntaxKind.RefKeyword))
                return false;

            var isStruct = type is StructDeclarationSyntax
                || type is RecordDeclarationSyntax { ClassOrStructKeyword.RawKind: (int)SyntaxKind.StructKeyword };

            for (var current = site.Parent; current is not null && current != type; current = current.Parent)
            {
                switch (current)
                {
                    case LocalFunctionStatementSyntax local:
                        if (isStruct || local.Modifiers.Any(SyntaxKind.StaticKeyword)) return false;
                        break;
                    case AnonymousFunctionExpressionSyntax lambda:
                        if (isStruct || lambda.Modifiers.Any(SyntaxKind.StaticKeyword)) return false;
                        break;
                    case AccessorDeclarationSyntax accessor:
                        return accessor.Parent?.Parent is BasePropertyDeclarationSyntax owner
                            && !owner.Modifiers.Any(SyntaxKind.StaticKeyword);
                    case MethodDeclarationSyntax or ConstructorDeclarationSyntax or DestructorDeclarationSyntax:
                        return !((MemberDeclarationSyntax)current).Modifiers.Any(SyntaxKind.StaticKeyword);
                    case MemberDeclarationSyntax:
                        // A field or property initializer, or an operator: no `this` to read.
                        return false;
                }
            }
            return false;
        }
    }

    // ------------------------------------------------------------------ emit

    private sealed class Emitter : CSharpSyntaxRewriter
    {
        private readonly Analysis _analysis;
        private readonly int _handle;

        public Emitter(Analysis analysis, int handle)
        {
            _analysis = analysis;
            _handle = handle;
        }

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            var visited = base.Visit(node);

            // The whole body of a branch or loop, written without braces: give it braces so the
            // check has somewhere to go.
            if (node is StatementSyntax original && visited is StatementSyntax rewritten
                && _analysis.Sites.TryGetValue(original, out var site) && site.Embedded)
            {
                var sequence = Sequence(original, rewritten);
                var leading = sequence[0].GetLeadingTrivia();
                var trailing = sequence[^1].GetTrailingTrivia();
                sequence[0] = sequence[0].WithLeadingTrivia(SyntaxFactory.Space);
                sequence[^1] = sequence[^1].WithTrailingTrivia(SyntaxFactory.Space);

                return SyntaxFactory.Block(
                    SyntaxFactory.Token(leading, SyntaxKind.OpenBraceToken, SyntaxFactory.TriviaList()),
                    SyntaxFactory.List(sequence),
                    SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.CloseBraceToken, trailing));
            }

            return visited;
        }

        public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
        {
            var members = new List<MemberDeclarationSyntax>(node.Members.Count * 2);
            foreach (var member in node.Members)
            {
                var rewritten = (MemberDeclarationSyntax)Visit(member)!;
                SyntaxNode target = member is GlobalStatementSyntax global ? global.Statement : member;

                if (_analysis.Sites.TryGetValue(target, out var site))
                {
                    members.Add(SyntaxFactory.GlobalStatement(
                        Check(site).WithLeadingTrivia(rewritten.GetLeadingTrivia())));
                    rewritten = rewritten.WithLeadingTrivia(SyntaxFactory.Space);
                }

                members.Add(rewritten);
            }

            return node.WithMembers(SyntaxFactory.List(members));
        }

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            var statements = new List<StatementSyntax>(node.Statements.Count * 2 + 1);
            foreach (var statement in node.Statements)
                statements.AddRange(Sequence(statement, (StatementSyntax)Visit(statement)!));

            // The body of a function: say so on the way in, so calls made from here know their caller.
            if (statements.Count > 0 && node.Parent is not null
                && _analysis.Functions.TryGetValue(node.Parent, out var function))
            {
                var enter = SyntaxFactory.ParseStatement(
                    $"using var {FramePrefix}{function} = {Probes}.Enter({_handle}, {function});");
                var first = statements[0];
                statements[0] = first.WithLeadingTrivia(SyntaxFactory.Space);
                statements.Insert(0, enter.WithLeadingTrivia(first.GetLeadingTrivia()).WithTrailingTrivia());
            }

            return node.WithStatements(SyntaxFactory.List(statements));
        }

        public override SyntaxNode? VisitSwitchSection(SwitchSectionSyntax node)
        {
            var statements = new List<StatementSyntax>(node.Statements.Count * 2);
            foreach (var statement in node.Statements)
                statements.AddRange(Sequence(statement, (StatementSyntax)Visit(statement)!));

            var labels = SyntaxFactory.List(node.Labels.Select(label => (SwitchLabelSyntax)Visit(label)!));
            return node.WithLabels(labels).WithStatements(SyntaxFactory.List(statements));
        }

        /// <summary>
        /// What stands in for one statement in a list: the statement, with its check ahead of it when
        /// it has one, all on the statement's own line.
        /// </summary>
        private List<StatementSyntax> Sequence(StatementSyntax original, StatementSyntax rewritten)
        {
            var sequence = new List<StatementSyntax>(4);
            if (!_analysis.Sites.TryGetValue(original, out var site))
            {
                sequence.Add(rewritten);
                return sequence;
            }

            sequence.Add(Check(site));

            // An iterator hands control to its consumer at a yield. What the consumer calls next is
            // the consumer's callee, not the iterator's.
            var yields = site.Function >= 0 && original.IsKind(SyntaxKind.YieldReturnStatement);
            if (yields)
                sequence.Add(SyntaxFactory.ParseStatement($"{FramePrefix}{site.Function}.Suspend();").WithTrailingTrivia());

            sequence.Add(rewritten);

            if (yields)
                sequence.Add(SyntaxFactory.ParseStatement($"{FramePrefix}{site.Function}.Resume();").WithTrailingTrivia());

            var leading = rewritten.GetLeadingTrivia();
            var trailing = rewritten.GetTrailingTrivia();
            for (var i = 0; i < sequence.Count; i++)
            {
                sequence[i] = sequence[i]
                    .WithLeadingTrivia(i == 0 ? leading : SyntaxFactory.TriviaList(SyntaxFactory.Space))
                    .WithTrailingTrivia(i == sequence.Count - 1 ? trailing : SyntaxFactory.TriviaList());
            }

            return sequence;
        }

        private StatementSyntax Check(Site site)
        {
            var values = site.Expressions.Length == 0
                ? "new object[0]"
                : "new object[] { " + string.Join(", ", site.Expressions) + " }";

            var text = site.Function < 0
                ? $"if ({Probes}.Hit({_handle}, {site.Index})) {Probes}.Pause({_handle}, {site.Index}, {values});"
                : $"if ({FramePrefix}{site.Function}.Hit({site.Index})) {FramePrefix}{site.Function}.Pause({site.Index}, {values});";

            return SyntaxFactory.ParseStatement(text).WithTrailingTrivia();
        }
    }
}
