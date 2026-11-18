using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using TabLinks.Vsix.Editor;
using Task = System.Threading.Tasks.Task;

namespace TabLinks.Vsix
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("TabLinks", "Links between open C# tabs", "0.1.0")]
    [ProvideOptionPage(typeof(LinkOptionsPage), "TabLinks", "Links", 0, 0, true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuidString)]
    public sealed class TabLinksPackage : AsyncPackage
    {
        public const string PackageGuidString = "a3d1c6f2-4e58-4b7a-9c20-1f6e8d3b5a70";
        private static readonly Guid CmdSet = new Guid("e72b9d14-6a35-4c8f-b0d1-5c93a8e2f467");

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (!(await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)) return;

            commands.AddCommand(new MenuCommand(
                (s, e) => JumpFromCaret(),
                new CommandID(CmdSet, 0x0100)));

            var toggle = new OleMenuCommand(
                (s, e) => { LinkService.Enabled = !LinkService.Enabled; LinkService.Schedule(0); },
                new CommandID(CmdSet, 0x0101));
            toggle.BeforeQueryStatus += (s, e) => ((OleMenuCommand)s).Checked = LinkService.Enabled;
            commands.AddCommand(toggle);
        }

        private void JumpFromCaret()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(GetService(typeof(SVsTextManager)) is IVsTextManager manager)) return;
            manager.GetActiveView(1, null, out IVsTextView adapter);
            if (adapter == null) return;
            var model = (IComponentModel)GetService(typeof(SComponentModel));
            var wpf = model.GetService<IVsEditorAdaptersFactoryService>().GetWpfTextView(adapter);
            var documents = model.GetService<Microsoft.VisualStudio.Text.ITextDocumentFactoryService>();
            if (wpf == null || !documents.TryGetTextDocument(wpf.TextBuffer, out var doc)) return;

            var caret = wpf.Caret.Position.BufferPosition;
            var edges = LinkService.EdgesAt(doc.FilePath, caret.GetContainingLine().LineNumber);
            if (edges.Count == 0) return;
            var bounds = wpf.TextViewLines.GetCharacterBounds(caret);
            var rect = new Rect(bounds.Left - wpf.ViewportLeft, bounds.Bottom - wpf.ViewportTop, 1, 1);
            if (wpf.Properties.TryGetProperty(typeof(LinkAdornment), out LinkAdornment adornment))
                adornment.Pick(edges, rect);
        }
    }
}
