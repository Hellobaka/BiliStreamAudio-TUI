# BiliStreamAudio-TUI

Windows-first terminal client for listening to public Bilibili live audio and reading/sending danmaku. It targets .NET 10 and is licensed under AGPL-3.0-or-later.

## Run

```powershell
dotnet run --project src/BiliStreamAudio.Tui
```

Use `l` to open Bilibili's official login page in a temporary WebView2 window. The app never asks for pasted cookies, passwords, QR data, or browser-cookie access. Enter a room ID in the input field and press Enter.

The project deliberately does not access paid, DRM, or restricted content and does not attempt to bypass Bilibili access controls.
