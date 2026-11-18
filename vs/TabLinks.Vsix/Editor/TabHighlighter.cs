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
                    Color = Color.FromRgb(0x4D, 0xA3, 0xFF), BlurRadius = 16, ShadowDepth = 0, Opacity = 1,
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
                if (tb.IsVisible && wanted.Contains(text))
                {
                    var tab = TabAncestor(tb);
                    if (tab != null && !Marked.ContainsKey(tab))
                    {
                        Marked[tab] = tab.Effect;
                        tab.Effect = glow;
                    }
                    return;
                }
            }
            var count = node is Visual || node is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(node) : 0;
            for (var i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i), wanted, glow, depth + 1, stats);
        }

        private static string Normalize(string? text) => (text ?? "").Trim().TrimEnd('*', ' ');

        private static UIElement? TabAncestor(DependencyObject start)
        {
            DependencyObject? current = start;
            UIElement? firstElement = null;
            for (var i = 0; i < 8 && current != null; i++)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is UIElement element)
                {
                    firstElement ??= element;
                    if (current.GetType().Name.IndexOf("Tab", StringComparison.OrdinalIgnoreCase) >= 0) return element;
                }
            }
            return firstElement;
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
