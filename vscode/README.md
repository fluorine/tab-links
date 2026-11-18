# TabLinks for VS Code

TabLinks shows how your open tabs connect. Open an interface in one tab and its implementation in another. A small arrow button appears in the code. Hover over it to see the targets. Click a target to go to the other tab and to the exact line.

It works for any language that has a VS Code language server with "go to implementation" and "find references".

## What you get

- **Implementation to interface.** A button on a class or member goes to the interface, base class or overridden member.
- **Interface to implementations.** A button on an interface member goes to each implementation.
- **Use of an object or member.** A button at a line that uses a type from another tab goes to that type.
- **Back links.** A button on the declaration goes back to the tabs that use it.
- A button with a number has more than one target.

A link needs both ends in open tabs. Close a tab and its links go away.

## Use it

- Click the arrow button. One target: you go there. Several targets: a list opens. You can also hover and click a target in the popup.
- A status bar item `$(link-external) N` shows when the symbol at the cursor has links. Click it for the list.
- `Ctrl+Alt+J` opens the list for the symbol at the cursor.

## Settings

| Setting | Default | Use |
|---|---|---|
| `tablinks.enabled` | true | Turn all buttons on or off. |
| `tablinks.clickToJump` | true | A click at the end of a line with a button jumps. Turn off if it gets in your way. |
| `tablinks.links.implements` | true | Implementation to interface. |
| `tablinks.links.implementedBy` | true | Interface to implementations. |
| `tablinks.links.usesTypes` | true | Use of an object (type). |
| `tablinks.links.usesMembers` | true | Use of a member (call, property). |
| `tablinks.links.usedByTypes` | true | Type to the places that use it. |
| `tablinks.links.usedByMembers` | true | Member to the places that use it. |
| `tablinks.maxOpenDocs` | 10 | Most tabs to analyze. |
| `tablinks.maxSymbolsPerDoc` | 200 | Most symbols to analyze in a tab. |

## Privacy

TabLinks uses only the language servers on your machine. It sends nothing over the network.

[Source code and issues](https://github.com/fluorine/tab-links) · A Visual Studio version for C# is in the same repository.
