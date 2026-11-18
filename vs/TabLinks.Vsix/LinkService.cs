using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace TabLinks.Vsix
{
    internal sealed class LinkNode
    {
        public string FilePath = "";
        public string Label = "";
        public int NameLine, NameColumn, NameLength, StartLine, EndLine;
        public string FileName => Path.GetFileName(FilePath);
    }

    internal sealed class LinkEdge
    {
        public LinkNode From = null!;
        public LinkNode To = null!;
        public int Rank;
        public string Verb = "";
        public int AnchorLine = -1;
        public LinkKind Kind;

        public string Describe(bool specific = true)
        {
            if (specific) return To.Label + "  ·  " + To.FileName;
            var parts = To.Label.Split('.');
            if (!string.Equals(parts[0], Path.GetFileNameWithoutExtension(To.FilePath), StringComparison.OrdinalIgnoreCase))
                return To.Label + "  ·  " + To.FileName;
            return parts.Length > 1 ? "Go to tab  ·  " + parts[1] : "Go to tab";
        }
    }

    /// <summary>Finds links between symbols that are declared in open documents.</summary>
    internal static class LinkService
    {
        private const int MaxDocs = 10;
        private const int MaxSymbolsPerDoc = 200;

        private static readonly object Gate = new object();
        private static Workspace? _workspace;
        private static CancellationTokenSource _cts = new CancellationTokenSource();
        private static ILookup<string, LinkEdge> _byFile = Array.Empty<LinkEdge>().ToLookup(e => e.From.FilePath);

        public static bool Enabled { get; set; } = true;
        public static event Action? Changed;

        public static void EnsureStarted(Workspace workspace)
        {
            lock (Gate)
            {
                if (_workspace != null) return;
                _workspace = workspace;
            }
            workspace.DocumentOpened += (s, e) => Schedule(600);
            workspace.DocumentClosed += (s, e) => Schedule(300);
            workspace.WorkspaceChanged += (s, e) =>
            {
                if (e.Kind == WorkspaceChangeKind.DocumentChanged) Schedule(2500);
            };
            Schedule(1500);
        }

        public static void Schedule(int delayMs)
        {
            CancellationToken token;
            lock (Gate)
            {
                _cts.Cancel();
                _cts = new CancellationTokenSource();
                token = _cts.Token;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                    var edges = Enabled ? await BuildAsync(token).ConfigureAwait(false) : new List<LinkEdge>();
                    if (token.IsCancellationRequested) return;
                    _byFile = edges.ToLookup(e => e.From.FilePath, StringComparer.OrdinalIgnoreCase);
                    Changed?.Invoke();
                }
                catch (OperationCanceledException) { }
                catch (Exception) { }
            });
        }

        public static IReadOnlyList<LinkEdge> EdgesFor(string path) => _byFile[path].ToList();

        public static IReadOnlyList<LinkEdge> EdgesAt(string path, int line)
        {
            var onLine = _byFile[path].Where(e => e.AnchorLine == line).OrderBy(e => e.Rank).ToList();
            if (onLine.Count > 0) return onLine;
            var here = _byFile[path].Where(e => e.From.StartLine <= line && line <= e.From.EndLine).ToList();
            if (here.Count == 0) return here;
            var innermost = here.Max(e => e.From.StartLine);
            return here.Where(e => e.From.StartLine == innermost).OrderBy(e => e.Rank).ToList();
        }

        public static void Jump(LinkNode node)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.OpenDocument(ServiceProvider.GlobalProvider, node.FilePath, Guid.Empty,
                out _, out _, out IVsWindowFrame frame);
            frame.Show();
            IVsTextView? view = VsShellUtilities.GetTextView(frame);
            if (view == null) return;
            view.SetSelection(node.NameLine, node.NameColumn, node.NameLine, node.NameColumn + node.NameLength);
            view.CenterLines(node.NameLine, 1);
        }

        private sealed class Decl
        {
            public ISymbol Symbol = null!;
            public LinkNode Node = null!;
            public string Path = "";
            public int Start, End;
        }

        private static async Task<List<LinkEdge>> BuildAsync(CancellationToken ct)
        {
            var ws = _workspace;
            if (ws == null) return new List<LinkEdge>();
            var solution = ws.CurrentSolution;
            var docs = ws.GetOpenDocumentIds()
                .Select(solution.GetDocument)
                .Where(d => d != null && d.FilePath != null && d.SourceCodeKind == SourceCodeKind.Regular && d.Project.Language == LanguageNames.CSharp)
                .GroupBy(d => d!.FilePath!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()!)
                .Take(MaxDocs)
                .ToList();
            if (docs.Count < 2) return new List<LinkEdge>();

            var decls = new List<Decl>();
            var bySymbol = new Dictionary<ISymbol, Decl>(SymbolEqualityComparer.Default);
            foreach (var doc in docs)
            {
                ct.ThrowIfCancellationRequested();
                var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
                var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                var text = await doc.GetTextAsync(ct).ConfigureAwait(false);
                if (model == null || root == null) continue;
                var count = 0;
                foreach (var node in root.DescendantNodes())
                {
                    SyntaxToken id;
                    switch (node)
                    {
                        case BaseTypeDeclarationSyntax t: id = t.Identifier; break;
                        case MethodDeclarationSyntax m: id = m.Identifier; break;
                        case PropertyDeclarationSyntax p: id = p.Identifier; break;
                        default: continue;
                    }
                    var symbol = model.GetDeclaredSymbol(node, ct);
                    if (symbol == null) continue;
                    var body = text.Lines.GetLinePositionSpan(node.Span);
                    var name = text.Lines.GetLinePosition(id.SpanStart);
                    var decl = new Decl
                    {
                        Symbol = symbol,
                        Path = doc.FilePath!,
                        Start = node.Span.Start,
                        End = node.Span.End,
                        Node = new LinkNode
                        {
                            FilePath = doc.FilePath!,
                            Label = symbol.ContainingType != null && !(symbol is INamedTypeSymbol) ? symbol.ContainingType.Name + "." + symbol.Name : symbol.Name,
                            NameLine = name.Line, NameColumn = name.Character, NameLength = id.Span.Length,
                            StartLine = body.Start.Line, EndLine = body.End.Line,
                        },
                    };
                    decls.Add(decl);
                    bySymbol[symbol.OriginalDefinition] = decl;
                    if (++count >= MaxSymbolsPerDoc) break;
                }
            }

            var edges = new Dictionary<(LinkNode, LinkNode), LinkEdge>();
            void Add(LinkNode from, LinkNode to, int rank, string verb, LinkKind kind)
            {
                if (from == to) return;
                if (edges.TryGetValue((from, to), out var old) && old.Rank <= rank) return;
                edges[(from, to)] = new LinkEdge { From = from, To = to, Rank = rank, Verb = verb, AnchorLine = from.NameLine, Kind = kind };
            }
            var settings = LinkSettings.Current;
            var uses = new List<LinkEdge>();

            foreach (var d in decls)
            {
                foreach (var (target, verb, reverse) in Bases(d.Symbol))
                {
                    if (!bySymbol.TryGetValue(target.OriginalDefinition, out var t) || t.Path == d.Path) continue;
                    Add(d.Node, t.Node, 0, "↑ " + verb, LinkKind.Implements);
                    Add(t.Node, d.Node, 1, "↓ " + reverse, LinkKind.ImplementedBy);
                }
            }

            var docSet = ImmutableHashSet.CreateRange(docs);
            foreach (var d in decls)
            {
                ct.ThrowIfCancellationRequested();
                if (!settings.AnyUsage) break;
                IEnumerable<Microsoft.CodeAnalysis.FindSymbols.ReferencedSymbol> refs;
                try
                {
                    refs = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
                        .FindReferencesAsync(d.Symbol, solution, docSet, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { continue; }

                foreach (var loc in refs.SelectMany(r => r.Locations))
                {
                    var path = loc.Document.FilePath;
                    if (path == null || string.Equals(path, d.Path, StringComparison.OrdinalIgnoreCase)) continue;
                    var pos = loc.Location.SourceSpan.Start;
                    var user = decls
                        .Where(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase) && x.Start <= pos && pos <= x.End)
                        .OrderBy(x => x.End - x.Start)
                        .FirstOrDefault();
                    if (user == null) continue;
                    var line = loc.Location.GetLineSpan().StartLinePosition.Line;
                    var isType = d.Symbol is INamedTypeSymbol;
                    uses.Add(new LinkEdge
                    {
                        From = user.Node, To = d.Node, Rank = 2, AnchorLine = line,
                        Kind = isType ? LinkKind.UsesType : LinkKind.UsesMember,
                    });
                    Add(d.Node, user.Node, 3, "", isType ? LinkKind.UsedByType : LinkKind.UsedByMember);
                }
            }
            return Collapse(edges.Values.Concat(uses).Where(e => settings.Allows(e.Kind)));
        }

        /// <summary>
        /// A method that uses a file many times gets one link for that file, at its first use.
        /// The link goes to the first declaration in the file, normally the type.
        /// </summary>
        private static List<LinkEdge> Collapse(IEnumerable<LinkEdge> all)
        {
            var result = new List<LinkEdge>();
            foreach (var g in all.GroupBy(e => (e.From, e.Rank >= 2 ? e.Rank : -1, e.Rank >= 2 ? e.To.FilePath : e.To.Label + e.To.StartLine)))
            {
                var best = g.OrderBy(e => e.To.StartLine).First();
                if (best.Rank == 2)
                    best = new LinkEdge { From = best.From, To = best.To, Rank = 2, AnchorLine = g.Min(e => e.AnchorLine), Kind = best.Kind };
                result.Add(best);
            }
            return result;
        }

        private static IEnumerable<(ISymbol Target, string Verb, string Reverse)> Bases(ISymbol s)
        {
            switch (s)
            {
                case INamedTypeSymbol t:
                    if (t.BaseType != null && t.BaseType.SpecialType == SpecialType.None)
                        yield return (t.BaseType, "extends", "extended in");
                    foreach (var i in t.Interfaces)
                        yield return (i, t.TypeKind == TypeKind.Interface ? "extends" : "implements", t.TypeKind == TypeKind.Interface ? "extended in" : "implemented in");
                    yield break;
                case IMethodSymbol m:
                    if (m.OverriddenMethod != null) yield return (m.OverriddenMethod, "overrides", "overridden in");
                    foreach (var e in m.ExplicitInterfaceImplementations) yield return (e, "implements", "implemented in");
                    break;
                case IPropertySymbol p:
                    if (p.OverriddenProperty != null) yield return (p.OverriddenProperty, "overrides", "overridden in");
                    foreach (var e in p.ExplicitInterfaceImplementations) yield return (e, "implements", "implemented in");
                    break;
                default:
                    yield break;
            }
            var type = s.ContainingType;
            if (type == null || type.TypeKind == TypeKind.Interface) yield break;
            foreach (var i in type.AllInterfaces)
            {
                foreach (var member in i.GetMembers(s.Name))
                {
                    if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), s))
                        yield return (member, "implements", "implemented in");
                }
            }
        }
    }
}
