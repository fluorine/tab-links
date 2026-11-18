import * as vscode from 'vscode';
import { buildEdges, fileName, openTextTabs, ORDER, symbolLabel, type Edge, type EdgeKind } from './links';

const VERB: Record<EdgeKind, string> = {
  implements: '$(arrow-up) implements',
  implementedBy: '$(arrow-down) implemented in',
  uses: '$(arrow-right) uses',
  usedBy: '$(arrow-left) used in',
};

export function activate(context: vscode.ExtensionContext): void {
  const cfg = () => vscode.workspace.getConfiguration('tablinks');
  const lensChanged = new vscode.EventEmitter<void>();
  let edges: Edge[] = [];
  let generation = 0;
  let timer: NodeJS.Timeout | undefined;
  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 90);
  status.command = 'tablinks.jumpFromCursor';

  const sorted = (list: Edge[]) => [...list].sort((a, b) => ORDER.indexOf(a.kind) - ORDER.indexOf(b.kind));

  const jump = async (e: Edge) => {
    const tab = openTextTabs().get(e.to.uri.toString());
    const editor = await vscode.window.showTextDocument(e.to.uri, {
      viewColumn: tab?.column, selection: new vscode.Range(e.to.sel.start, e.to.sel.start), preview: false,
    });
    editor.revealRange(e.to.range, vscode.TextEditorRevealType.InCenterIfOutsideViewport);
    const flash = vscode.window.createTextEditorDecorationType({
      backgroundColor: new vscode.ThemeColor('editor.findMatchHighlightBackground'), isWholeLine: true,
    });
    editor.setDecorations(flash, [e.to.sel]);
    setTimeout(() => flash.dispose(), 1200);
  };

  const pick = async (list: Edge[]) => {
    if (!list.length) { void vscode.window.showInformationMessage('TabLinks: no linked symbol in the open tabs.'); return; }
    if (list.length === 1) { await jump(list[0]); return; }
    const chosen = await vscode.window.showQuickPick(
      sorted(list).map(e => ({
        label: `${VERB[e.kind]} ${symbolLabel(e.to)}`, description: fileName(e.to.uri), detail: `Line ${e.to.sel.start.line + 1}`, edge: e,
      })),
      { placeHolder: 'Jump to a linked symbol in an open tab' });
    if (chosen) await jump(chosen.edge);
  };

  const edgesAt = (uri: vscode.Uri, line: number) =>
    edges.filter(e => e.from.uri.toString() === uri.toString() && e.from.range.start.line <= line && line <= e.from.range.end.line);

  const updateStatus = () => {
    const ed = vscode.window.activeTextEditor;
    if (!ed || !cfg().get('enabled', true)) { status.hide(); return; }
    const line = ed.selection.active.line;
    const here = edgesAt(ed.document.uri, line);
    const innermost = here.length ? Math.max(...here.map(e => e.from.range.start.line)) : -1;
    const list = here.filter(e => e.from.range.start.line === innermost);
    if (!list.length) { status.hide(); return; }
    status.text = `$(link) ${list.length}`;
    status.tooltip = sorted(list).map(e => `${VERB[e.kind].replace(/\$\([^)]+\)\s*/, '')} ${symbolLabel(e.to)} (${fileName(e.to.uri)})`).join('\n');
    status.show();
  };

  const refresh = async () => {
    if (!cfg().get('enabled', true)) { edges = []; lensChanged.fire(); updateStatus(); return; }
    const mine = ++generation;
    try {
      const result = await buildEdges({
        showUsages: cfg().get('showUsages', true),
        maxDocs: cfg().get('maxOpenDocs', 10),
        maxSymbols: cfg().get('maxSymbolsPerDoc', 200),
      }, () => mine === generation);
      if (mine !== generation) return;
      edges = result;
      lensChanged.fire();
      updateStatus();
    } catch {
      if (mine === generation) edges = [];
    }
  };

  const schedule = (ms: number) => {
    clearTimeout(timer);
    timer = setTimeout(() => void refresh(), ms);
  };

  const lenses: vscode.CodeLensProvider = {
    onDidChangeCodeLenses: lensChanged.event,
    provideCodeLenses(doc) {
      if (!cfg().get('enabled', true)) return [];
      const max = cfg().get('maxLensPerSymbol', 3);
      const byLine = new Map<number, Edge[]>();
      for (const e of edges) {
        if (e.from.uri.toString() !== doc.uri.toString()) continue;
        const k = e.from.sel.start.line;
        byLine.set(k, [...(byLine.get(k) ?? []), e]);
      }
      const out: vscode.CodeLens[] = [];
      for (const [line, list] of byLine) {
        const range = new vscode.Range(line, 0, line, 0);
        const ordered = sorted(list);
        for (const e of ordered.slice(0, max)) {
          out.push(new vscode.CodeLens(range, {
            title: `${VERB[e.kind]} ${symbolLabel(e.to)} (${fileName(e.to.uri)})`,
            command: 'tablinks.jump', arguments: [e],
          }));
        }
        if (ordered.length > max) {
          out.push(new vscode.CodeLens(range, { title: `+${ordered.length - max} more`, command: 'tablinks.pick', arguments: [ordered] }));
        }
      }
      return out;
    },
  };

  context.subscriptions.push(
    status, lensChanged,
    vscode.languages.registerCodeLensProvider({ scheme: 'file' }, lenses),
    vscode.window.tabGroups.onDidChangeTabs(() => schedule(800)),
    vscode.workspace.onDidChangeTextDocument(ev => { if (ev.contentChanges.length) schedule(2500); }),
    vscode.workspace.onDidSaveTextDocument(() => schedule(500)),
    vscode.window.onDidChangeTextEditorSelection(updateStatus),
    vscode.window.onDidChangeActiveTextEditor(updateStatus),
    vscode.workspace.onDidChangeConfiguration(ev => { if (ev.affectsConfiguration('tablinks')) schedule(100); }),
    vscode.commands.registerCommand('tablinks.jump', (e: Edge) => jump(e)),
    vscode.commands.registerCommand('tablinks.pick', (list: Edge[]) => pick(list)),
    vscode.commands.registerCommand('tablinks.refresh', () => refresh()),
    vscode.commands.registerCommand('tablinks.toggle', () => cfg().update('enabled', !cfg().get('enabled', true), vscode.ConfigurationTarget.Global)),
    vscode.commands.registerCommand('tablinks.jumpFromCursor', () => {
      const ed = vscode.window.activeTextEditor;
      if (!ed) return;
      const here = edgesAt(ed.document.uri, ed.selection.active.line);
      const innermost = here.length ? Math.max(...here.map(e => e.from.range.start.line)) : -1;
      return pick(here.filter(e => e.from.range.start.line === innermost));
    }),
  );

  schedule(1500);
}

export function deactivate(): void {}
