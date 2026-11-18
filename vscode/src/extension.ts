import * as vscode from 'vscode';
import { buildEdges, fileName, openTextTabs, symbolLabel, type Edge, type LinkKind } from './links';

const KIND_SETTINGS: Record<LinkKind, string> = {
  implements: 'implements',
  implementedBy: 'implementedBy',
  usesType: 'usesTypes',
  usesMember: 'usesMembers',
  usedByType: 'usedByTypes',
  usedByMember: 'usedByMembers',
};

const NBSP = ' ';

export function activate(context: vscode.ExtensionContext): void {
  const cfg = () => vscode.workspace.getConfiguration('tablinks');
  let edges: Edge[] = [];
  let generation = 0;
  let timer: NodeJS.Timeout | undefined;

  const status = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 90);
  status.command = 'tablinks.jumpFromCursor';

  const arrow = vscode.window.createTextEditorDecorationType({
    after: {
      color: new vscode.ThemeColor('textLink.foreground'),
      border: '1px solid rgba(128,128,128,0.45)',
      textDecoration: 'none; cursor: pointer',
      margin: '0 0 0 12px',
    },
  });

  const sorted = (list: Edge[]) => [...list].sort((a, b) => a.rank - b.rank);
  const unique = (list: Edge[]) => {
    const seen = new Set<string>();
    return sorted(list).filter(e => {
      const k = e.to.uri.toString() + '|' + symbolLabel(e.to);
      return seen.has(k) ? false : (seen.add(k), true);
    });
  };

  const jumpTo = async (uri: vscode.Uri, line: number, col: number, length: number, endLine: number) => {
    const tab = openTextTabs().get(uri.toString());
    const sel = new vscode.Range(line, col, line, col + length);
    const editor = await vscode.window.showTextDocument(uri, { viewColumn: tab?.column, selection: sel, preview: false });
    editor.revealRange(new vscode.Range(line, 0, Math.max(line, endLine), 0), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
    const flash = vscode.window.createTextEditorDecorationType({
      backgroundColor: new vscode.ThemeColor('editor.findMatchHighlightBackground'), isWholeLine: true,
    });
    editor.setDecorations(flash, [sel]);
    setTimeout(() => flash.dispose(), 1200);
  };

  const jump = (e: Edge) =>
    jumpTo(e.to.uri, e.to.sel.start.line, e.to.sel.start.character, e.to.sel.end.character - e.to.sel.start.character, e.to.range.end.line);

  const pick = async (list: Edge[]) => {
    const shown = unique(list);
    if (!shown.length) { void vscode.window.showInformationMessage('TabLinks: no linked symbol in the open tabs.'); return; }
    if (shown.length === 1) { await jump(shown[0]); return; }
    const chosen = await vscode.window.showQuickPick(
      shown.map(e => ({ label: `$(link-external) ${symbolLabel(e.to)}`, description: fileName(e.to.uri), detail: `Line ${e.to.sel.start.line + 1}`, edge: e })),
      { placeHolder: 'Jump to a linked symbol in an open tab' });
    if (chosen) await jump(chosen.edge);
  };

  const edgesAt = (uri: vscode.Uri, line: number) => {
    const mine = edges.filter(e => e.from.uri.toString() === uri.toString());
    const onLine = mine.filter(e => e.anchorLine === line);
    if (onLine.length) return sorted(onLine);
    const here = mine.filter(e => e.from.range.start.line <= line && line <= e.from.range.end.line);
    if (!here.length) return [];
    const top = Math.max(...here.map(e => e.from.range.start.line));
    return sorted(here.filter(e => e.from.range.start.line === top));
  };

  const hover = (list: Edge[]): vscode.MarkdownString => {
    const md = new vscode.MarkdownString(undefined, true);
    md.isTrusted = true;
    const items = unique(list).map(e => {
      const args = encodeURIComponent(JSON.stringify([
        e.to.uri.toString(), e.to.sel.start.line, e.to.sel.start.character,
        e.to.sel.end.character - e.to.sel.start.character, e.to.range.end.line,
      ]));
      const label = `${symbolLabel(e.to)} · ${fileName(e.to.uri)}`.replace(/[\\`*_{}[\]()#+!|<>]/g, m => '\\' + m);
      return `[$(link-external) ${label}](command:tablinks.jumpTo?${args})`;
    });
    md.appendMarkdown(items.join('\n\n'));
    return md;
  };

  const render = (editor: vscode.TextEditor) => {
    if (!cfg().get('enabled', true)) { editor.setDecorations(arrow, []); return; }
    const key = editor.document.uri.toString();
    const byLine = new Map<number, Edge[]>();
    for (const e of edges) {
      if (e.from.uri.toString() !== key || e.anchorLine >= editor.document.lineCount) continue;
      byLine.set(e.anchorLine, [...(byLine.get(e.anchorLine) ?? []), e]);
    }
    const options: vscode.DecorationOptions[] = [];
    for (const [line, list] of byLine) {
      const len = editor.document.lineAt(line).text.length;
      if (len === 0) continue;
      const count = unique(list).length;
      options.push({
        range: new vscode.Range(line, len - 1, line, len),
        hoverMessage: hover(list),
        renderOptions: { after: { contentText: `${NBSP}↗${count > 1 ? NBSP + count : ''}${NBSP}` } },
      });
    }
    editor.setDecorations(arrow, options);
  };

  const renderAll = () => { for (const e of vscode.window.visibleTextEditors) render(e); };

  const updateStatus = () => {
    const ed = vscode.window.activeTextEditor;
    if (!ed || !cfg().get('enabled', true)) { status.hide(); return; }
    const list = unique(edgesAt(ed.document.uri, ed.selection.active.line));
    if (!list.length) { status.hide(); return; }
    status.text = `$(link-external) ${list.length}`;
    status.tooltip = list.map(e => `${symbolLabel(e.to)} (${fileName(e.to.uri)})`).join('\n');
    status.show();
  };

  const refresh = async () => {
    if (!cfg().get('enabled', true)) { edges = []; renderAll(); updateStatus(); return; }
    const mine = ++generation;
    try {
      const allow = {} as Record<LinkKind, boolean>;
      for (const k of Object.keys(KIND_SETTINGS) as LinkKind[]) allow[k] = cfg().get(`links.${KIND_SETTINGS[k]}`, true);
      const result = await buildEdges({
        allow, maxDocs: cfg().get('maxOpenDocs', 10), maxSymbols: cfg().get('maxSymbolsPerDoc', 200),
      }, () => mine === generation);
      if (mine !== generation) return;
      edges = result;
      renderAll();
      updateStatus();
    } catch {
      if (mine === generation) edges = [];
    }
  };

  const schedule = (ms: number) => {
    clearTimeout(timer);
    timer = setTimeout(() => void refresh(), ms);
  };

  context.subscriptions.push(
    status, arrow,
    vscode.window.tabGroups.onDidChangeTabs(() => schedule(800)),
    vscode.workspace.onDidChangeTextDocument(ev => { if (ev.contentChanges.length) schedule(2500); }),
    vscode.workspace.onDidSaveTextDocument(() => schedule(500)),
    // A click on the arrow puts the caret at the end of the line. Treat that as a click on the button.
    vscode.window.onDidChangeTextEditorSelection(ev => {
      updateStatus();
      if (!cfg().get('clickToJump', true) || ev.kind !== vscode.TextEditorSelectionChangeKind.Mouse) return;
      const sel = ev.selections[0];
      if (ev.selections.length !== 1 || !sel.isEmpty) return;
      const doc = ev.textEditor.document;
      const line = sel.active.line;
      if (sel.active.character !== doc.lineAt(line).text.length) return;
      const here = edges.filter(e => e.from.uri.toString() === doc.uri.toString() && e.anchorLine === line);
      if (here.length) void pick(here);
    }),
    vscode.window.onDidChangeActiveTextEditor(() => { renderAll(); updateStatus(); }),
    vscode.window.onDidChangeVisibleTextEditors(renderAll),
    vscode.workspace.onDidChangeConfiguration(ev => { if (ev.affectsConfiguration('tablinks')) schedule(100); }),
    vscode.commands.registerCommand('tablinks.jumpTo', (uri: string, line: number, col: number, length: number, endLine: number) =>
      jumpTo(vscode.Uri.parse(uri), line, col, length, endLine)),
    vscode.commands.registerCommand('tablinks.refresh', () => refresh()),
    vscode.commands.registerCommand('tablinks.toggle', () => cfg().update('enabled', !cfg().get('enabled', true), vscode.ConfigurationTarget.Global)),
    vscode.commands.registerCommand('tablinks.jumpFromCursor', () => {
      const ed = vscode.window.activeTextEditor;
      if (ed) return pick(edgesAt(ed.document.uri, ed.selection.active.line));
    }),
  );

  schedule(1500);
}

export function deactivate(): void {}
