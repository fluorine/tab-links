# TabLinks

<p align="center"><img src="assets/icon.png" width="96" alt="TabLinks icon"></p>

TabLinks shows how your open tabs connect. Open an interface in one tab and its implementation in another. A small arrow button appears in the code. Click it to go to the other tab and to the exact line.

Hover over the button. The target tab lights up in the tab bar, so you see where you will go.

There are two extensions:

| Editor | Folder | Languages |
|---|---|---|
| Visual Studio 2022 and later | [`vs/`](vs/) | C# (uses Roslyn) |
| Visual Studio Code | [`vscode/`](vscode/) | Any language with a language server |

## What links it finds

- **Implementation to interface.** On a class or member, a button goes to the interface, base class or overridden member.
- **Interface to implementations.** On an interface member, a button goes to each implementation.
- **Use of an object or member.** At a line that uses a type or calls a member from another tab, a button goes to its declaration.
- **Back links.** On the declaration, a button goes back to the tabs that use it.

A link needs both ends in open tabs. Close a tab and its links go away. If a button has more than one target, it shows a number and a click opens a list.

## Visual Studio

- Install it from the Visual Studio Marketplace, or download the `.vsix` from the [releases](https://github.com/fluorine/tab-links/releases).
- `Ctrl+Alt+J` opens the list of links for the symbol at the caret.
- Tools > Options > TabLinks > Links turns each kind of link on or off.
- Tools > "TabLinks: Show links" turns all buttons on or off.

## Visual Studio Code

- Links show as CodeLens above symbols, and as a status bar item for the symbol at the cursor.
- `Ctrl+Alt+J` opens the list of links.
- Settings start with `tablinks.`.

## Privacy

TabLinks works on your machine only. It sends nothing over the network.

## Build

```powershell
msbuild vs\TabLinks.Vsix\TabLinks.Vsix.csproj /restore /p:Configuration=Release
cd vscode; npm ci; npm run build; npx vsce package --no-dependencies
```

To publish, push a tag. `v0.1.1` publishes the Visual Studio extension. `vscode-v0.1.1` publishes the VS Code extension. The tag must match the version in the manifest. The secret `VS_MARKETPLACE_PAT` must exist in the repository.

## Related

[CodeGlyph](https://github.com/fluorine/code-glyph) draws a subtle icon behind each class and method. It is a separate project.

## License

MIT
