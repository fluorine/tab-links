# TabLinks

TabLinks shows how your open tabs connect. Open an interface in one tab and its implementation in another. Each symbol then gets a link above it. Click the link to go to the other tab and to the exact line.

It works for any language that has a VS Code language server with "go to implementation" and "find references".

## What you see

- `↑ implements IUserService.Get (IUserService.cs)` above an implementation.
- `↓ implemented in UserService.Get (UserService.cs)` above an interface member.
- `← used in` and `→ uses` for calls between open tabs. Turn these off with `tablinks.showUsages`.
- A status bar item `$(link) N` when the symbol at the cursor has links. Click it to see the list.
- `Ctrl+Alt+J` opens the list for the symbol at the cursor.

A link needs both ends in open tabs. Close a tab and its links go away.

## Settings

| Setting | Default | Use |
|---|---|---|
| `tablinks.enabled` | true | Turn all links on or off. |
| `tablinks.showUsages` | true | Also link callers and callees. |
| `tablinks.maxOpenDocs` | 10 | Most tabs to analyze. |
| `tablinks.maxSymbolsPerDoc` | 200 | Most symbols to analyze in a tab. |
| `tablinks.maxLensPerSymbol` | 3 | Links above one symbol. |

## Privacy

TabLinks uses only the language servers on your machine. It sends nothing over the network.

## Visual Studio version

The `vs/` folder holds a Visual Studio extension for C#. It uses Roslyn, so it knows real interface, base class and usage links.

- A small chip at the right edge of the line shows the link, such as `↑ implements IUserService.Get (IUserService.cs)`. Click it to go to that tab and line.
- If a symbol has more than one link, the chip shows `+N`. Click it to see the list.
- `Ctrl+Alt+J` opens the list for the symbol at the caret.
- Tools > "TabLinks: Show links" turns the chips on and off.

Build: `MSBuild.exe vs\TabLinks.Vsix\TabLinks.Vsix.csproj /restore /p:Configuration=Release`
