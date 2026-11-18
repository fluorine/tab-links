using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace TabLinks.Vsix.Editor
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("CSharp")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class LinkViewListener : IWpfTextViewCreationListener
    {
        public const string LayerName = "TabLinks";

        [Export(typeof(AdornmentLayerDefinition))]
        [Name(LayerName)]
        [Order(After = PredefinedAdornmentLayers.Text)]
        [TextViewRole(PredefinedTextViewRoles.Document)]
        public AdornmentLayerDefinition? Layer;

        [Import] public VisualStudioWorkspace? Workspace;
        [Import] public ITextDocumentFactoryService? Documents;
        [Import] public IEditorFormatMapService? FormatMaps;

        public void TextViewCreated(IWpfTextView textView)
        {
            if (Workspace == null || Documents == null || FormatMaps == null) return;
            if (!Documents.TryGetTextDocument(textView.TextBuffer, out var doc)) return;
            LinkService.EnsureStarted(Workspace);
            textView.Properties.GetOrCreateSingletonProperty(
                () => new LinkAdornment(textView, doc.FilePath, FormatMaps.GetEditorFormatMap(textView)));
        }
    }

    internal sealed class LinkAdornment
    {
        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly string _path;
        private readonly IEditorFormatMap _formatMap;

        public LinkAdornment(IWpfTextView view, string path, IEditorFormatMap formatMap)
        {
            _view = view;
            _path = path;
            _formatMap = formatMap;
            _layer = view.GetAdornmentLayer(LinkViewListener.LayerName);
            view.LayoutChanged += OnLayout;
            view.Closed += OnClosed;
            LinkService.Changed += OnLinksChanged;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _view.LayoutChanged -= OnLayout;
            _view.Closed -= OnClosed;
            LinkService.Changed -= OnLinksChanged;
        }

        private void OnLayout(object sender, TextViewLayoutChangedEventArgs e) => Redraw();

        private void OnLinksChanged() =>
            _view.VisualElement.Dispatcher.BeginInvoke(new Action(Redraw));

        private Brush Foreground()
        {
            var props = _formatMap.GetProperties("Plain Text");
            return props.Contains(EditorFormatDefinition.ForegroundBrushId)
                ? (Brush)props[EditorFormatDefinition.ForegroundBrushId]
                : Brushes.Gray;
        }

        private void Redraw()
        {
            _layer.RemoveAllAdornments();
            if (!LinkService.Enabled || _view.IsClosed) return;
            var snapshot = _view.TextSnapshot;
            var fg = Foreground();

            foreach (var group in LinkService.EdgesFor(_path).GroupBy(e => e.AnchorLine))
            {
                if (group.Key >= snapshot.LineCount) continue;
                var line = snapshot.GetLineFromLineNumber(group.Key);
                if (line.Length == 0 || !_view.TextViewLines.ContainsBufferPosition(line.Start)) continue;
                var viewLine = _view.TextViewLines.GetTextViewLineContainingBufferPosition(line.Start);
                var edges = group.OrderBy(e => e.Rank).ToList();
                var chip = MakeChip(edges, fg);
                chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var width = chip.DesiredSize.Width;
                var left = viewLine.TextRight + 12;
                if (left + width > _view.ViewportRight) continue;
                Canvas.SetLeft(chip, left);
                Canvas.SetTop(chip, viewLine.TextTop);
                _layer.AddAdornment(AdornmentPositioningBehavior.OwnerControlled,
                    new SnapshotSpan(line.Start, 1), null, chip, null);
            }
        }

        private static UIElement Content(int count, Brush fg)
        {
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0.5,14 H3 V5 Q3,3 5,3 H11 Q13,3 13,5 V14 H15.5"),
                Stroke = fg,
                StrokeThickness = 1.4,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 1, 0, 0),
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(icon);
            if (count > 1)
                panel.Children.Add(new TextBlock { Text = count.ToString(), Foreground = fg, FontSize = 10, Margin = new Thickness(3, 0, 0, 0) });
            return panel;
        }

        private FrameworkElement MakeChip(System.Collections.Generic.List<LinkEdge> edges, Brush fg)
        {
            var color = (fg as SolidColorBrush)?.Color ?? Colors.Gray;
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4, 0, 4, 1),
                Opacity = 0.65,
                Cursor = Cursors.Hand,
                Child = Content(edges.Count, fg),
                ToolTip = string.Join("\n", edges.Select(e => e.Describe())),
            };
            chip.MouseEnter += (s, e) =>
            {
                chip.Opacity = 1;
                TabHighlighter.Show(edges.Select(x => x.To.FileName));
            };
            chip.MouseLeave += (s, e) =>
            {
                chip.Opacity = 0.65;
                TabHighlighter.Clear();
            };
            chip.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Pick(edges, chip, PlacementMode.Bottom, Rect.Empty);
            };
            return chip;
        }

        public void Pick(System.Collections.Generic.IReadOnlyList<LinkEdge> edges, UIElement target,
            PlacementMode placement, Rect rect)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (edges.Count == 0) return;
            if (edges.Count == 1) { LinkService.Jump(edges[0].To); return; }
            var fg = Foreground();
            var color = (fg as SolidColorBrush)?.Color ?? Colors.Gray;
            var hover = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B));
            var panel = new StackPanel();
            var popup = new Popup
            {
                PlacementTarget = target,
                Placement = placement,
                StaysOpen = false,
                Child = new Border
                {
                    Background = _view.Background,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, color.R, color.G, color.B)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(2),
                    Child = panel,
                },
            };
            if (rect != Rect.Empty) popup.PlacementRectangle = rect;
            foreach (var edge in edges)
            {
                var node = edge.To;
                var row = new TextBlock
                {
                    Text = edge.Describe(),
                    Foreground = fg,
                    Background = Brushes.Transparent,
                    Padding = new Thickness(8, 3, 8, 3),
                    Cursor = Cursors.Hand,
                };
                row.MouseEnter += (s, e) => { row.Background = hover; TabHighlighter.Show(new[] { node.FileName }); };
                row.MouseLeave += (s, e) => { row.Background = Brushes.Transparent; TabHighlighter.Clear(); };
                row.MouseLeftButtonUp += (s, e) => { popup.IsOpen = false; TabHighlighter.Clear(); LinkService.Jump(node); };
                panel.Children.Add(row);
            }
            popup.IsOpen = true;
        }
    }
}
