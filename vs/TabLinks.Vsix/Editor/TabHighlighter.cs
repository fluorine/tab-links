using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TabLinks.Vsix.Editor
{
    /// <summary>
    /// Glows the document tab headers of the given files. It looks for the tab by its caption in every
    /// WPF window tree. If the tab is not found, nothing happens. Each try is logged to a file.
    /// </summary>
    internal static class TabHighlighter
    {
        private static readonly Dictionary<UIElement, Effect?> Marked = new Dictionary<UIElement, Effect?>();
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabLinks", "log.txt");

        public static void Show(IEnumerable<string> fileNames)
        {
            Clear();
            try
            {
                var wanted = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
                var glow = new DropShadowEffect
                {
                    Color = Color.FromRgb(0xFF, 0xA5, 0x2E), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9,
                };
                var stats = new Stats();
                foreach (PresentationSource source in PresentationSource.CurrentSources)
                {
                    if (source.RootVisual is DependencyObject root) Walk(root, wanted, glow, 0, stats);
                }
                Log("show " + string.Join(",", wanted) + " blocks=" + stats.Blocks + " matches=" + Marked.Count
                    + " sample=" + string.Join(" | ", stats.Sample));
            }
            catch (Exception ex) { Log("error " + ex.Message); }
        }

        public static void Clear()
        {
            foreach (var pair in Marked) pair.Key.Effect = pair.Value;
            Marked.Clear();
            foreach (var (layer, outline) in Outlines) layer.Remove(outline);
            Outlines.Clear();
        }

        private static readonly List<(System.Windows.Documents.AdornerLayer Layer, System.Windows.Documents.Adorner Outline)> Outlines
            = new List<(System.Windows.Documents.AdornerLayer, System.Windows.Documents.Adorner)>();

        private sealed class OutlineAdorner : System.Windows.Documents.Adorner
        {
            public OutlineAdorner(UIElement element) : base(element) { IsHitTestVisible = false; }

            protected override void OnRender(DrawingContext dc)
            {
                var accent = Color.FromRgb(0xFF, 0xA5, 0x2E);
                var rect = new Rect(new Point(1, 1), new Size(Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)),
                    new Pen(new SolidColorBrush(accent), 1.5), rect, 4, 4);
            }
        }

        private sealed class Stats
        {
            public int Blocks;
            public readonly List<string> Sample = new List<string>();
        }

        private static void Walk(DependencyObject node, HashSet<string> wanted, Effect glow, int depth, Stats stats)
        {
            if (depth > 90) return;
            if (node is TextBlock tb)
            {
                stats.Blocks++;
                var text = Normalize(tb.Text);
                if (text.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && stats.Sample.Count < 12)
                    stats.Sample.Add(text + (tb.IsVisible ? "" : "(hidden)"));
                if (tb.IsVisible && Matches(wanted, text))
                {
                    var tab = TabAncestor(tb);
                    if (tab != null && !Marked.ContainsKey(tab))
                    {
                        Marked[tab] = tab.Effect;
                        tab.Effect = glow;
                        var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(tab);
                        if (layer != null)
                        {
                            var outline = new OutlineAdorner(tab);
                            layer.Add(outline);
                            Outlines.Add((layer, outline));
                        }
                    }
                    return;
                }
            }
            var count = node is Visual || node is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(node) : 0;
            for (var i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i), wanted, glow, depth + 1, stats);
        }

        private static bool Matches(HashSet<string> wanted, string text)
        {
            if (wanted.Contains(text)) return true;
            var cut = text.IndexOf((char)0x2026);
            if (cut < 0) return false;
            var head = text.Substring(0, cut);
            var tail = text.Substring(cut + 1);
            return wanted.Any(w => w.StartsWith(head, StringComparison.OrdinalIgnoreCase) && w.EndsWith(tail, StringComparison.OrdinalIgnoreCase));
        }

        private static string Normalize(string? text) => (text ?? "").Trim().TrimEnd('*', ' ');

        private static UIElement? TabAncestor(DependencyObject start)
        {
            // The tab is the largest ancestor that is still about the size of one tab header.
            DependencyObject? current = start;
            FrameworkElement? best = null;
            var chain = new List<string>();
            for (var i = 0; i < 14 && current != null; i++)
            {
                current = VisualTreeHelper.GetParent(current);
                if (!(current is FrameworkElement fe)) continue;
                chain.Add(fe.GetType().Name + " " + (int)fe.ActualWidth + "x" + (int)fe.ActualHeight);
                if (fe.ActualWidth > 420 || fe.ActualHeight > 48) break;
                best = fe;
            }
            Log("tab chain: " + string.Join(" > ", chain) + " => " + (best?.GetType().Name ?? "none"));
            return best;
        }

        private static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 200_000) File.Delete(LogPath);
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + line + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
