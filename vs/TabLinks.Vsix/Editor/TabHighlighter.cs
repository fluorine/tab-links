using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TabLinks.Vsix.Editor
{
    /// <summary>
    /// Glows the document tab headers of the given files. It looks for the tab by its caption in the
    /// window tree. If the tab is not found, nothing happens.
    /// </summary>
    internal static class TabHighlighter
    {
        private static readonly Dictionary<UIElement, Effect?> Marked = new Dictionary<UIElement, Effect?>();

        public static void Show(IEnumerable<string> fileNames)
        {
            Clear();
            try
            {
                var root = Application.Current?.MainWindow;
                if (root == null) return;
                var wanted = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
                var glow = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x4D, 0xA3, 0xFF), BlurRadius = 14, ShadowDepth = 0, Opacity = 1,
                };
                Walk(root, wanted, glow, 0);
            }
            catch (Exception) { }
        }

        public static void Clear()
        {
            foreach (var pair in Marked) pair.Key.Effect = pair.Value;
            Marked.Clear();
        }

        private static void Walk(DependencyObject node, HashSet<string> wanted, Effect glow, int depth)
        {
            if (depth > 60) return;
            if (node is TextBlock tb && tb.IsVisible && wanted.Contains(Normalize(tb.Text)))
            {
                var tab = TabAncestor(tb);
                if (tab != null && !Marked.ContainsKey(tab))
                {
                    Marked[tab] = tab.Effect;
                    tab.Effect = glow;
                }
                return;
            }
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i), wanted, glow, depth + 1);
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
    }
}
