# BiliStreamAudio-TUI

Windows-first terminal client for listening to public Bilibili live audio and reading/sending danmaku. It targets .NET 10 and is licensed under AGPL-3.0-or-later.

## Run

```powershell
dotnet run --project src/BiliStreamAudio.Tui
```

Use `l` to open Bilibili's official login page in a temporary WebView2 window. The app never asks for pasted cookies, passwords, QR data, or browser-cookie access. Select a live room from the “浏览” page; the input field beneath the live room is only for sending danmaku.

The project deliberately does not access paid, DRM, or restricted content and does not attempt to bypass Bilibili access controls.

## Mock mode

Use Mock mode to test the TUI without opening a login window, initializing LibVLC, or making any network request:

```powershell
dotnet run --project src/BiliStreamAudio.Tui -- --mock
```

You can also set `BILISTREAMAUDIO_MOCK=1` (or `true`). Mock mode supplies a logged-in mock user, live room data, audio playback states, and locally echoed danmaku.
