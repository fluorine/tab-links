# TabLinks for Visual Studio

TabLinks shows how your open C# tabs connect. Open an interface in one tab and its implementation in another. A small arrow button appears in the code. Click it to go to the other tab and to the exact line.

Hover over the button. The target tab lights up in the tab bar, so you see where you will go.

## What you get

- **Implementation to interface.** A button on a class or member goes to the interface, base class or overridden member.
- **Interface to implementations.** A button on an interface member goes to each implementation.
- **Use of an object or member.** A button at a line that uses a type from another tab goes to that type.
- **Back links.** A button on the declaration goes back to the tabs that use it.
- A button with a number has more than one target. Click it to see the list.

A link needs both ends in open tabs. Close a tab and its links go away.

## Use it

- `Ctrl+Alt+J` opens the list of links for the symbol at the caret.
- Tools > Options > TabLinks > Links turns each kind of link on or off.
- Tools > "TabLinks: Show links" turns all buttons on or off.

## Privacy

TabLinks works on your machine only. It uses Roslyn in Visual Studio. It sends nothing over the network.

[Source code and issues](https://github.com/fluorine/tab-links)
