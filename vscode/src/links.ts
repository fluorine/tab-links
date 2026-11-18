import * as vscode from 'vscode';

export type LinkKind = 'implements' | 'implementedBy' | 'usesType' | 'usesMember' | 'usedByType' | 'usedByMember';

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
  kind: LinkKind;
  rank: number;
  anchorLine: number;
}

export interface LinkOptions {
  allow: Record<LinkKind, boolean>;
  maxDocs: number;
  maxSymbols: number;
}

const WANTED = new Set<vscode.SymbolKind>([
  vscode.SymbolKind.Class, vscode.SymbolKind.Interface, vscode.SymbolKind.Struct, vscode.SymbolKind.Enum,
  vscode.SymbolKind.Method, vscode.SymbolKind.Function, vscode.SymbolKind.Constructor, vscode.SymbolKind.Property,
]);

const TYPES = new Set<vscode.SymbolKind>([
  vscode.SymbolKind.Class, vscode.SymbolKind.Interface, vscode.SymbolKind.Struct, vscode.SymbolKind.Enum,
]);

const RANK: Record<LinkKind, number> = {
  implements: 0, implementedBy: 1, usesType: 2, usesMember: 2, usedByType: 3, usedByMember: 3,
};

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

const symKey = (s: Sym) => `${s.uri.toString()}|${s.sel.start.line}|${s.sel.start.character}`;

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

const edge = (from: Sym, to: Sym, kind: LinkKind, anchorLine: number): Edge => ({ from, to, kind, rank: RANK[kind], anchorLine });

/**
 * A symbol that uses a file many times gets one link for that file, at its first use.
 * The link goes to the first declaration in the file. A pair with an implementation link needs no usage link.
 */
export function collapse(all: Edge[], allow: Record<LinkKind, boolean>): Edge[] {
  const pairs = new Map<string, Edge>();
  const groups = new Map<string, Edge[]>();
  for (const e of all.filter(x => allow[x.kind])) {
    if (e.rank === 2 || e.rank === 3) {
      const key = `${symKey(e.from)}|${e.rank}|${e.to.uri.toString()}`;
      groups.set(key, [...(groups.get(key) ?? []), e]);
    } else {
      const key = `${symKey(e.from)}|${symKey(e.to)}`;
      const old = pairs.get(key);
      if (!old || old.rank > e.rank) pairs.set(key, e);
    }
  }
  const result = [...pairs.values()];
  for (const list of groups.values()) {
    const best = [...list].sort((a, b) => a.to.range.start.line - b.to.range.start.line)[0];
    if (pairs.has(`${symKey(best.from)}|${symKey(best.to)}`)) continue;
    const anchor = best.rank === 2 ? Math.min(...list.map(x => x.anchorLine)) : best.anchorLine;
    result.push({ ...best, anchorLine: anchor });
  }
  return result;
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

  const a = opts.allow;
  const wantBase = a.implements || a.implementedBy;
  const wantUsage = a.usesType || a.usesMember || a.usedByType || a.usedByMember;
  const raw: Edge[] = [];
  const all = [...bySym.values()].flat();

  for (let i = 0; i < all.length; i += 8) {
    await Promise.all(all.slice(i, i + 8).map(async s => {
      const here = s.uri.toString();
      const resolve = (l: vscode.Location) => {
        const key = l.uri.toString();
        return key !== here && bySym.has(key) ? innermost(bySym.get(key)!, l.range.start) : undefined;
      };
      if (wantBase) {
        for (const l of await locations('vscode.executeImplementationProvider', s)) {
          const t = resolve(l);
          if (!t) continue;
          raw.push(edge(s, t, 'implementedBy', s.sel.start.line));
          raw.push(edge(t, s, 'implements', t.sel.start.line));
        }
      }
      if (wantUsage) {
        const isType = TYPES.has(s.kind);
        for (const l of await locations('vscode.executeReferenceProvider', s)) {
          const t = resolve(l);
          if (!t) continue;
          raw.push(edge(t, s, isType ? 'usesType' : 'usesMember', l.range.start.line));
          raw.push(edge(s, t, isType ? 'usedByType' : 'usedByMember', s.sel.start.line));
        }
      }
    }));
    if (!isCurrent()) return [];
  }
  return collapse(raw, a);
}
