import * as vscode from 'vscode';

export type EdgeKind = 'implements' | 'implementedBy' | 'uses' | 'usedBy';

export interface Sym {
  uri: vscode.Uri;
  name: string;
  container: string;
  kind: vscode.SymbolKind;
  range: vscode.Range;
  sel: vscode.Range;
}

export interface Edge {
  from: Sym;
  to: Sym;
  kind: EdgeKind;
}

export interface LinkOptions {
  showUsages: boolean;
  maxDocs: number;
  maxSymbols: number;
}

const WANTED = new Set<vscode.SymbolKind>([
  vscode.SymbolKind.Class, vscode.SymbolKind.Interface, vscode.SymbolKind.Struct, vscode.SymbolKind.Enum,
  vscode.SymbolKind.Method, vscode.SymbolKind.Function, vscode.SymbolKind.Constructor, vscode.SymbolKind.Property,
]);

const REVERSE: Record<EdgeKind, EdgeKind> = {
  implements: 'implementedBy', implementedBy: 'implements', uses: 'usedBy', usedBy: 'uses',
};

export const ORDER: EdgeKind[] = ['implements', 'implementedBy', 'uses', 'usedBy'];

export function openTextTabs(): Map<string, { uri: vscode.Uri; column: vscode.ViewColumn }> {
  const tabs = new Map<string, { uri: vscode.Uri; column: vscode.ViewColumn }>();
  for (const group of vscode.window.tabGroups.all) {
    for (const tab of group.tabs) {
      if (tab.input instanceof vscode.TabInputText) tabs.set(tab.input.uri.toString(), { uri: tab.input.uri, column: group.viewColumn });
    }
  }
  return tabs;
}

export function symbolLabel(s: Sym): string {
  return s.container ? `${s.container}.${s.name}` : s.name;
}

export function fileName(uri: vscode.Uri): string {
  return uri.path.split('/').pop() ?? uri.path;
}

export async function readSymbols(doc: vscode.TextDocument, max: number): Promise<Sym[]> {
  const raw = await vscode.commands.executeCommand<(vscode.DocumentSymbol | vscode.SymbolInformation)[]>(
    'vscode.executeDocumentSymbolProvider', doc.uri);
  const out: Sym[] = [];
  const walk = (items: (vscode.DocumentSymbol | vscode.SymbolInformation)[], container: string) => {
    for (const it of items) {
      if (out.length >= max) return;
      if ('children' in it) {
        if (WANTED.has(it.kind)) out.push({ uri: doc.uri, name: it.name, container, kind: it.kind, range: it.range, sel: it.selectionRange });
        walk(it.children, WANTED.has(it.kind) ? it.name : container);
      } else if (WANTED.has(it.kind)) {
        const line = it.location.range.start.line;
        const col = Math.max(0, doc.lineAt(line).text.indexOf(it.name));
        const sel = new vscode.Range(line, col, line, col + it.name.length);
        out.push({ uri: doc.uri, name: it.name, container: it.containerName, kind: it.kind, range: it.location.range, sel });
      }
    }
  };
  walk(raw ?? [], '');
  return out;
}

function toLocation(x: vscode.Location | vscode.LocationLink): vscode.Location {
  return 'targetUri' in x ? new vscode.Location(x.targetUri, x.targetSelectionRange ?? x.targetRange) : x;
}

function innermost(syms: Sym[], pos: vscode.Position): Sym | undefined {
  let best: Sym | undefined;
  for (const s of syms) {
    if (!s.range.contains(pos)) continue;
    if (!best || best.range.contains(s.range)) best = s;
  }
  return best;
}

async function locations(command: string, s: Sym): Promise<vscode.Location[]> {
  try {
    const r = await vscode.commands.executeCommand<(vscode.Location | vscode.LocationLink)[]>(command, s.uri, s.sel.start);
    return (r ?? []).map(toLocation);
  } catch {
    return [];
  }
}

/** Finds links between symbols of the open tabs. Both ends of a link must be in an open tab. */
export async function buildEdges(opts: LinkOptions, isCurrent: () => boolean): Promise<Edge[]> {
  const tabs = [...openTextTabs().values()].slice(0, opts.maxDocs);
  if (tabs.length < 2) return [];

  const bySym = new Map<string, Sym[]>();
  for (const t of tabs) {
    try {
      const doc = await vscode.workspace.openTextDocument(t.uri);
      bySym.set(t.uri.toString(), await readSymbols(doc, opts.maxSymbols));
    } catch {
      bySym.set(t.uri.toString(), []);
    }
    if (!isCurrent()) return [];
  }

  const edges = new Map<string, Edge>();
  const add = (from: Sym, to: Sym, kind: EdgeKind) => {
    const key = [from.uri, from.sel.start.line, to.uri, to.sel.start.line].map(String).join('|');
    const existing = edges.get(key);
    if (existing && ORDER.indexOf(existing.kind) <= ORDER.indexOf(kind)) return;
    edges.set(key, { from, to, kind });
    const rkey = [to.uri, to.sel.start.line, from.uri, from.sel.start.line].map(String).join('|');
    const rev = edges.get(rkey);
    if (!rev || ORDER.indexOf(rev.kind) > ORDER.indexOf(REVERSE[kind])) edges.set(rkey, { from: to, to: from, kind: REVERSE[kind] });
  };

  const all = [...bySym.values()].flat();
  for (let i = 0; i < all.length; i += 8) {
    await Promise.all(all.slice(i, i + 8).map(async s => {
      const here = s.uri.toString();
      const resolve = (l: vscode.Location) => {
        const key = l.uri.toString();
        return key !== here && bySym.has(key) ? innermost(bySym.get(key)!, l.range.start) : undefined;
      };
      for (const l of await locations('vscode.executeImplementationProvider', s)) {
        const t = resolve(l);
        if (t) add(s, t, 'implementedBy');
      }
      if (!opts.showUsages) return;
      for (const l of await locations('vscode.executeReferenceProvider', s)) {
        const t = resolve(l);
        if (t) add(s, t, 'usedBy');
      }
    }));
    if (!isCurrent()) return [];
  }
  return [...edges.values()];
}
