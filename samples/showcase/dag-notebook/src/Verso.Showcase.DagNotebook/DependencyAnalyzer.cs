using System.Text;
using System.Text.RegularExpressions;
using Verso.Abstractions;

namespace Verso.Showcase.DagNotebook;

/// <summary>
/// Builds the variable-level dependency graph the DAG Notebook layout renders and executes.
///
/// For every code cell the analyzer extracts the names the cell defines (assignments, function
/// and type declarations) and the names it uses (identifier references). A variable with exactly
/// one defining cell produces an edge from that cell to every other cell that references the
/// name. Variables written by more than one cell produce no edges; instead every writer is
/// flagged so the layout can surface the conflict, since "who feeds whom" is ambiguous once two
/// cells assign the same name. Cells that end up on a dependency cycle are flagged the same way,
/// and the edges inside the cycle are excluded from execution so a cascade can never loop.
///
/// The extraction is a deliberately lightweight, regex-based pass over comment- and
/// string-stripped source, so the extension carries no parser dependency. It reads interpolation
/// holes out of C# and Python format strings before stripping so a variable referenced only
/// inside one still counts as used. Like any heuristic it can over-link (a local that shadows a
/// shared name) or under-link (dynamic access); the layout treats the result as a strong hint,
/// not ground truth.
///
/// Two things a cell can write are not assignments and would be lost to the stripping pass, so
/// they are read from the raw source before it runs. A cell that shares a widget trait as a
/// notebook variable writes a directive, which in a Python cell begins with the comment
/// character. A cell that reaches the shared store by name puts the name in a string literal.
/// Both are how a value crosses from one language to another, so a dependency graph that missed
/// them would only ever link cells written in the same language.
/// </summary>
internal static class DependencyAnalyzer
{
    /// <summary>A directed dependency: <see cref="To"/> reads <see cref="Variable"/> defined by <see cref="From"/>.</summary>
    public sealed record Edge(Guid From, Guid To, string Variable);

    /// <summary>
    /// Per-cell analysis: 1-based document position, the names defined and used, and the subset
    /// of the defined names that follow a widget control rather than being computed by the cell.
    /// </summary>
    public sealed record Node(
        Guid Id,
        int Number,
        IReadOnlySet<string> Defines,
        IReadOnlySet<string> Uses,
        IReadOnlySet<string> Bindings);

    public sealed class Graph
    {
        private readonly Dictionary<Guid, Node> _nodes = new();
        private readonly List<Edge> _edges = new();
        private readonly Dictionary<Guid, List<Edge>> _inbound = new();
        private readonly Dictionary<Guid, List<Edge>> _outbound = new();
        private readonly List<Guid> _documentOrder = new();

        /// <summary>Variable name to the cells that write it, for names written by more than one cell.</summary>
        public Dictionary<string, List<Guid>> MultiWriterVariables { get; } = new();

        /// <summary>Cells that sit on at least one dependency cycle.</summary>
        public HashSet<Guid> CyclicCells { get; } = new();

        /// <summary>
        /// Variables that follow a widget control, mapped to the cell that bound them. A change to
        /// one of these arrives without any cell having run, which is what lets the layout treat a
        /// control being moved the same way it treats a producer cell finishing. Only names bound
        /// by exactly one cell appear, on the same reasoning that keeps multi-writer names out of
        /// the edge set.
        /// </summary>
        public Dictionary<string, Guid> BoundVariables { get; } = new(StringComparer.Ordinal);

        /// <summary>The bound variable names a given cell is the control behind.</summary>
        public IReadOnlyList<string> BindingsFor(Guid cellId) =>
            BoundVariables.Where(kv => kv.Value == cellId).Select(kv => kv.Key)
                .OrderBy(n => n, StringComparer.Ordinal).ToList();

        public IReadOnlyDictionary<Guid, Node> Nodes => _nodes;
        public IReadOnlyList<Edge> Edges => _edges;

        public IReadOnlyList<Edge> InboundEdges(Guid cellId) =>
            _inbound.TryGetValue(cellId, out var e) ? e : Array.Empty<Edge>();

        public IReadOnlyList<Edge> OutboundEdges(Guid cellId) =>
            _outbound.TryGetValue(cellId, out var e) ? e : Array.Empty<Edge>();

        /// <summary>The multi-writer variable names a given cell participates in writing.</summary>
        public IReadOnlyList<string> ConflictsFor(Guid cellId) =>
            MultiWriterVariables.Where(kv => kv.Value.Contains(cellId)).Select(kv => kv.Key).ToList();

        internal void AddNode(Node node)
        {
            _nodes[node.Id] = node;
            _documentOrder.Add(node.Id);
        }

        internal void AddEdge(Edge edge)
        {
            _edges.Add(edge);
            (_outbound.TryGetValue(edge.From, out var o) ? o : _outbound[edge.From] = new()).Add(edge);
            (_inbound.TryGetValue(edge.To, out var i) ? i : _inbound[edge.To] = new()).Add(edge);
        }

        internal void RemoveEdgesWhere(Func<Edge, bool> predicate)
        {
            _edges.RemoveAll(e => predicate(e));
            foreach (var list in _outbound.Values) list.RemoveAll(e => predicate(e));
            foreach (var list in _inbound.Values) list.RemoveAll(e => predicate(e));
        }

        /// <summary>
        /// The transitive dependents of a cell, in execution order (topological, document order
        /// breaking ties), excluding the cell itself. Only follows cycle-free edges.
        /// </summary>
        public IReadOnlyList<Guid> Downstream(Guid cellId)
        {
            var reachable = new HashSet<Guid>();
            var stack = new Stack<Guid>();
            stack.Push(cellId);
            while (stack.Count > 0)
            {
                foreach (var edge in OutboundEdges(stack.Pop()))
                {
                    if (reachable.Add(edge.To))
                        stack.Push(edge.To);
                }
            }
            return TopologicalOrder().Where(reachable.Contains).ToList();
        }

        /// <summary>
        /// Every analyzed cell in dependency order: a producer always precedes its consumers, and
        /// otherwise cells keep their document order. Cycle members keep document order among
        /// themselves because their internal edges were removed.
        /// </summary>
        public IReadOnlyList<Guid> TopologicalOrder()
        {
            var remainingInbound = _documentOrder.ToDictionary(
                id => id,
                id => InboundEdges(id).Select(e => e.From).Distinct().Count());
            var emitted = new HashSet<Guid>();
            var result = new List<Guid>(_documentOrder.Count);

            while (result.Count < _documentOrder.Count)
            {
                var progressed = false;
                foreach (var id in _documentOrder)
                {
                    if (emitted.Contains(id) || remainingInbound[id] > 0) continue;
                    emitted.Add(id);
                    result.Add(id);
                    progressed = true;
                    foreach (var target in OutboundEdges(id).Select(e => e.To).Distinct())
                        remainingInbound[target]--;
                }
                if (!progressed)
                {
                    // Defensive: should be unreachable because intra-cycle edges are removed
                    // before ordering, but never loop forever if a cycle slips through.
                    foreach (var id in _documentOrder)
                        if (emitted.Add(id)) result.Add(id);
                    break;
                }
            }
            return result;
        }
    }

    public static Graph Analyze(IReadOnlyList<CellModel> cells)
    {
        var graph = new Graph();
        var writers = new Dictionary<string, List<Guid>>();

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (!string.Equals(cell.Type, "code", StringComparison.OrdinalIgnoreCase)) continue;

            var language = NormalizeLanguage(cell.Language);
            var (defines, uses, bindings) = Extract(cell.Source ?? "", language);
            graph.AddNode(new Node(cell.Id, i + 1, defines, uses, bindings));

            foreach (var name in defines)
            {
                if (!writers.TryGetValue(name, out var list)) writers[name] = list = new List<Guid>();
                if (!list.Contains(cell.Id)) list.Add(cell.Id);
            }
        }

        foreach (var (name, writerCells) in writers)
        {
            if (writerCells.Count > 1)
            {
                graph.MultiWriterVariables[name] = writerCells;
                continue;
            }

            if (graph.Nodes[writerCells[0]].Bindings.Contains(name))
                graph.BoundVariables[name] = writerCells[0];

            var producer = writerCells[0];
            foreach (var node in graph.Nodes.Values)
            {
                if (node.Id != producer && node.Uses.Contains(name))
                    graph.AddEdge(new Edge(producer, node.Id, name));
            }
        }

        MarkCyclesAndDropTheirEdges(graph);
        return graph;
    }

    private static string NormalizeLanguage(string? language) => language?.ToLowerInvariant() switch
    {
        "python" or "py" => "python",
        "fsharp" or "fs" => "fsharp",
        _ => "csharp",
    };

    // --- Cycle handling -------------------------------------------------------------------

    /// <summary>
    /// Tarjan strongly-connected components over the edge set. Any component with more than one
    /// cell is a cycle: its members are flagged and the edges between them removed, leaving an
    /// acyclic graph for ordering and cascades.
    /// </summary>
    private static void MarkCyclesAndDropTheirEdges(Graph graph)
    {
        var index = 0;
        var indices = new Dictionary<Guid, int>();
        var lowLinks = new Dictionary<Guid, int>();
        var onStack = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        var componentOf = new Dictionary<Guid, int>();
        var componentCount = 0;

        void StrongConnect(Guid v)
        {
            indices[v] = lowLinks[v] = index++;
            stack.Push(v);
            onStack.Add(v);

            foreach (var edge in graph.OutboundEdges(v))
            {
                var w = edge.To;
                if (!indices.ContainsKey(w))
                {
                    StrongConnect(w);
                    lowLinks[v] = Math.Min(lowLinks[v], lowLinks[w]);
                }
                else if (onStack.Contains(w))
                {
                    lowLinks[v] = Math.Min(lowLinks[v], indices[w]);
                }
            }

            if (lowLinks[v] == indices[v])
            {
                var component = componentCount++;
                Guid member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    componentOf[member] = component;
                } while (member != v);
            }
        }

        foreach (var id in graph.Nodes.Keys)
        {
            if (!indices.ContainsKey(id))
                StrongConnect(id);
        }

        var componentSizes = componentOf.Values
            .GroupBy(c => c)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (id, component) in componentOf)
        {
            if (componentSizes[component] > 1)
                graph.CyclicCells.Add(id);
        }

        if (graph.CyclicCells.Count > 0)
            graph.RemoveEdgesWhere(e => componentOf[e.From] == componentOf[e.To]
                                        && componentSizes[componentOf[e.From]] > 1);
    }

    // --- Per-language extraction ----------------------------------------------------------

    private static (IReadOnlySet<string> Defines, IReadOnlySet<string> Uses,
        IReadOnlySet<string> Bindings) Extract(string source, string language)
    {
        var bindings = new HashSet<string>(StringComparer.Ordinal);
        var namedWrites = new HashSet<string>(StringComparer.Ordinal);
        var namedReads = new HashSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(source))
            return (new HashSet<string>(), new HashSet<string>(), bindings);

        // Read from the raw source, before comments and string literals go: a bind directive is a
        // comment as far as Python is concerned, and a store access carries its name in a string.
        HarvestBindDirectives(source, bindings, namedReads);
        HarvestStoreAccess(source, namedWrites, namedReads);

        var interpolated = new List<string>();
        var stripped = language == "python"
            ? StripPython(source, interpolated)
            : StripCLike(source, interpolated);

        var defines = language switch
        {
            "python" => ExtractPythonDefines(stripped),
            "fsharp" => ExtractFSharpDefines(stripped),
            _ => ExtractCSharpDefines(stripped),
        };

        var keywords = language == "python" ? PythonKeywords : CSharpKeywords;
        defines.ExceptWith(keywords);
        defines.Remove("_");

        var uses = new HashSet<string>();
        CollectIdentifiers(stripped, uses);
        foreach (var fragment in interpolated)
            CollectIdentifiers(fragment, uses);
        uses.ExceptWith(keywords);
        uses.Remove("_");

        // Added after the keyword filter on purpose. A name written in full, either in a directive
        // or in a string, is the name the author meant, even where it collides with a keyword: a
        // trait bound without a new name is shared as the trait's own, and `value` is a trait name
        // as often as it is a keyword.
        defines.UnionWith(bindings);
        defines.UnionWith(namedWrites);
        uses.UnionWith(namedReads);

        return (defines, uses, bindings);
    }

    // --- Names a cell writes without assigning ------------------------------------------------

    // #!bind <expression>.<trait> [as <name>]: the directive that shares a widget trait as a
    // notebook variable. Without a name of its own the variable takes the trait's. The listing and
    // removal forms start with a dash and define nothing.
    private static readonly Regex BindDirectiveRx = new(
        @"^\s*#!bind\s+(?!-)(?<expr>\S+?)\.(?<trait>[A-Za-z_]\w*)\s*(?:as\s+(?<name>[A-Za-z_]\w*))?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex LeadingIdentifierRx = new(@"^[A-Za-z_]\w*", RegexOptions.Compiled);

    /// <summary>
    /// Collects the variables a cell's bind directives share, and the objects those directives
    /// name. The object is a use: it links the cell that built the widget to the cell that bound
    /// one of its traits.
    /// </summary>
    private static void HarvestBindDirectives(string source, HashSet<string> bindings, HashSet<string> uses)
    {
        if (source.IndexOf("#!bind", StringComparison.OrdinalIgnoreCase) < 0) return;

        foreach (Match m in BindDirectiveRx.Matches(source))
        {
            var name = m.Groups["name"].Success ? m.Groups["name"].Value : m.Groups["trait"].Value;
            bindings.Add(name);

            var root = LeadingIdentifierRx.Match(m.Groups["expr"].Value);
            if (root.Success) uses.Add(root.Value);
        }
    }

    // Variables.Get<T>("name") and Variables.TryGet<T>("name", out ...) read the shared store;
    // Variables.Set("name", value) writes it. This is the explicit form of variable sharing, and
    // the only one available when the name is not a legal identifier in the reading language.
    private static readonly Regex StoreReadRx = new(
        @"\bVariables\s*\.\s*(?:TryGet|Get)\s*(?:<[^<>()]*>)?\s*\(\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex StoreWriteRx = new(
        @"\bVariables\s*\.\s*Set\s*\(\s*""([^""]+)""", RegexOptions.Compiled);

    private static void HarvestStoreAccess(string source, HashSet<string> writes, HashSet<string> reads)
    {
        if (source.IndexOf("Variables", StringComparison.Ordinal) < 0) return;

        foreach (Match m in StoreReadRx.Matches(source)) AddStoreName(reads, m.Groups[1].Value);
        foreach (Match m in StoreWriteRx.Matches(source)) AddStoreName(writes, m.Groups[1].Value);
    }

    /// <summary>
    /// Keeps identifier-shaped names only, and leaves out the double-underscore prefix that marks
    /// a value belonging to a framework rather than to the notebook's author.
    /// </summary>
    private static void AddStoreName(HashSet<string> sink, string name)
    {
        if (name.StartsWith("__", StringComparison.Ordinal)) return;
        if (LeadingIdentifierRx.Match(name).Length == name.Length && name.Length > 0)
            sink.Add(name);
    }

    // Identifiers not immediately preceded by '.' (member access) or another word character.
    private static readonly Regex IdentifierRx = new(
        @"(?<![.\w])[A-Za-z_]\w*", RegexOptions.Compiled);

    private static void CollectIdentifiers(string text, HashSet<string> sink)
    {
        foreach (Match m in IdentifierRx.Matches(text))
            sink.Add(m.Value);
    }

    // --- C# / F# --------------------------------------------------------------------------

    // var x = ...
    private static readonly Regex CSharpVarRx = new(
        @"^\s*var\s+([A-Za-z_]\w*)\s*=(?![=>])", RegexOptions.Compiled | RegexOptions.Multiline);

    // Type name = ... (two tokens before '='; excludes control-flow keywords at line start)
    private static readonly Regex CSharpTypedRx = new(
        @"^\s*(?!var\b|return\b|await\b|if\b|while\b|for\b|foreach\b|switch\b|using\b|throw\b|yield\b|else\b|new\b|case\b)[A-Za-z_][\w<>,\.\[\]\?\s]*?\s+([A-Za-z_]\w*)\s*=(?![=>])",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Bare assignment or compound assignment at line start: x = ..., x += ...
    private static readonly Regex CSharpBareAssignRx = new(
        @"^\s*([A-Za-z_]\w*)\s*(?:[+\-*/%|&^]|<<|>>|\?\?)?=(?![=>])",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Type declarations: class/record/struct/interface/enum Name
    private static readonly Regex CSharpTypeDeclRx = new(
        @"\b(?:class|record|struct|interface|enum)\s+([A-Za-z_]\w*)",
        RegexOptions.Compiled);

    // Method-ish declarations at line start: ReturnType Name(...) { or => (covers top-level
    // script functions with or without modifiers)
    private static readonly Regex CSharpMethodRx = new(
        @"^\s*(?:(?:public|internal|private|protected|static|async|virtual|override|sealed)\s+)*[A-Za-z_][\w<>,\.\[\]\?]*\s+([A-Za-z_]\w*)\s*\([^)]*\)\s*(?:\{|=>)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static HashSet<string> ExtractCSharpDefines(string source)
    {
        var defines = new HashSet<string>();
        foreach (Match m in CSharpVarRx.Matches(source)) defines.Add(m.Groups[1].Value);
        foreach (Match m in CSharpTypedRx.Matches(source)) defines.Add(m.Groups[1].Value);
        foreach (Match m in CSharpBareAssignRx.Matches(source)) defines.Add(m.Groups[1].Value);
        foreach (Match m in CSharpTypeDeclRx.Matches(source)) defines.Add(m.Groups[1].Value);
        foreach (Match m in CSharpMethodRx.Matches(source)) defines.Add(m.Groups[1].Value);
        return defines;
    }

    private static readonly Regex FSharpLetRx = new(
        @"^\s*let\s+(?:rec\s+|mutable\s+|inline\s+)*([A-Za-z_]\w*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex FSharpTypeRx = new(
        @"^\s*(?:type|module)\s+([A-Za-z_]\w*)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static HashSet<string> ExtractFSharpDefines(string source)
    {
        var defines = new HashSet<string>();
        foreach (Match m in FSharpLetRx.Matches(source)) defines.Add(m.Groups[1].Value);
        foreach (Match m in FSharpTypeRx.Matches(source)) defines.Add(m.Groups[1].Value);
        return defines;
    }

    // --- Python -----------------------------------------------------------------------------

    // x = ... or x: int = ... (annotated), excluding == comparisons
    private static readonly Regex PythonAssignRx = new(
        @"^\s*([A-Za-z_]\w*)\s*(?::[^=\n]+)?=(?!=)", RegexOptions.Compiled | RegexOptions.Multiline);

    // a, b = ... tuple unpacking
    private static readonly Regex PythonTupleAssignRx = new(
        @"^\s*([A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)+)\s*=(?!=)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // x += ... augmented assignment (a write and a read)
    private static readonly Regex PythonAugmentedRx = new(
        @"^\s*([A-Za-z_]\w*)\s*(?:[+\-*/%@&|^]|//|\*\*|<<|>>)=(?!=)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex PythonDefClassRx = new(
        @"^\s*(?:def|class)\s+([A-Za-z_]\w*)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static HashSet<string> ExtractPythonDefines(string source)
    {
        var defines = new HashSet<string>();
        foreach (Match m in PythonAssignRx.Matches(source)) defines.Add(m.Groups[1].Value);
        foreach (Match m in PythonAugmentedRx.Matches(source)) defines.Add(m.Groups[1].Value);
        foreach (Match m in PythonDefClassRx.Matches(source)) defines.Add(m.Groups[1].Value);
        foreach (Match m in PythonTupleAssignRx.Matches(source))
        {
            foreach (var part in m.Groups[1].Value.Split(','))
            {
                var name = part.Trim();
                if (name.Length > 0) defines.Add(name);
            }
        }
        return defines;
    }

    // --- Comment / string stripping ---------------------------------------------------------

    private static readonly Regex CLikeBlockCommentRx = new(@"/\*[\s\S]*?\*/", RegexOptions.Compiled);
    private static readonly Regex CLikeLineCommentRx = new(@"//[^\n]*", RegexOptions.Compiled);
    private static readonly Regex CLikeStringRx = new(
        @"@?\$?""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'", RegexOptions.Compiled);

    private static readonly Regex PythonTripleStringRx = new(
        "\"\"\"[\\s\\S]*?\"\"\"|'''[\\s\\S]*?'''", RegexOptions.Compiled);
    private static readonly Regex PythonStringRx = new(
        @"[rbfu]{0,2}""(?:\\.|[^""\\\n])*""|[rbfu]{0,2}'(?:\\.|[^'\\\n])*'",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PythonCommentRx = new(@"#[^\n]*", RegexOptions.Compiled);

    // Interpolation holes inside format strings: the expression part of {expr} / {expr:format}.
    private static readonly Regex InterpolationHoleRx = new(
        @"\{([^{}:]+)(?::[^{}]*)?\}", RegexOptions.Compiled);

    private static string StripCLike(string source, List<string> interpolatedFragments)
    {
        var text = CLikeBlockCommentRx.Replace(source, Blank);
        text = CLikeLineCommentRx.Replace(text, Blank);
        text = CLikeStringRx.Replace(text, m =>
        {
            HarvestInterpolations(m.Value, interpolatedFragments);
            return Blank(m);
        });
        return text;
    }

    private static string StripPython(string source, List<string> interpolatedFragments)
    {
        var text = PythonTripleStringRx.Replace(source, m =>
        {
            HarvestInterpolations(m.Value, interpolatedFragments);
            return Blank(m);
        });
        text = PythonStringRx.Replace(text, m =>
        {
            HarvestInterpolations(m.Value, interpolatedFragments);
            return Blank(m);
        });
        text = PythonCommentRx.Replace(text, Blank);
        return text;
    }

    /// <summary>
    /// Collects the expression parts of interpolation holes so a variable referenced only inside
    /// a format string still registers as used after the string is blanked.
    /// </summary>
    private static void HarvestInterpolations(string literal, List<string> sink)
    {
        if (!literal.Contains('{')) return;
        foreach (Match hole in InterpolationHoleRx.Matches(literal))
            sink.Add(hole.Groups[1].Value);
    }

    /// <summary>Replaces a match with same-length whitespace, preserving newlines so the
    /// line-anchored define patterns keep their positions.</summary>
    private static string Blank(Match m)
    {
        var sb = new StringBuilder(m.Value.Length);
        foreach (var ch in m.Value)
            sb.Append(ch == '\n' ? '\n' : ' ');
        return sb.ToString();
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "dynamic", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "from", "get", "goto", "if", "implicit", "in", "init",
        "int", "interface", "internal", "is", "let", "lock", "long", "nameof", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "partial", "private",
        "protected", "public", "readonly", "record", "ref", "required", "return", "sbyte",
        "sealed", "select", "set", "short", "sizeof", "stackalloc", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "value", "var", "virtual", "void", "volatile", "when",
        "where", "while", "yield",
        // Ubiquitous BCL names that would otherwise create meaningless links.
        "Console", "Math", "Task", "List", "Dictionary", "HashSet", "String", "Convert",
        "DateTime", "TimeSpan", "Guid", "Enumerable", "Random", "Exception", "Array", "Tuple",
        // F# keywords that share this table (the C-like branch handles both languages).
        "member", "module", "mutable", "open", "rec", "then", "type", "elif", "done", "begin",
        "end", "fun", "function", "match", "with", "printfn",
    };

    private static readonly HashSet<string> PythonKeywords = new(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "case", "class", "continue", "def",
        "del", "elif", "else", "except", "finally", "for", "from", "global", "if", "import",
        "in", "is", "lambda", "match", "nonlocal", "not", "or", "pass", "raise", "return",
        "try", "while", "with", "yield", "True", "False", "None", "self", "cls",
        // Ubiquitous builtins that would otherwise create meaningless links.
        "print", "len", "range", "enumerate", "zip", "map", "filter", "sorted", "sum", "min",
        "max", "abs", "round", "int", "float", "str", "bool", "list", "dict", "set", "tuple",
        "type", "isinstance", "open", "input", "repr", "format", "any", "all", "iter", "next",
    };
}
