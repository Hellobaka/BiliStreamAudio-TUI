# Repository Guidelines

## Project Structure & Module Organization

The solution is `BiliStreamAudio-TUI.sln`. Application code is in
`src/BiliStreamAudio.Tui/`: `Core/` contains models, interfaces, session state,
and options; `Infrastructure/` implements Bilibili, playback, authentication,
and protocol integrations; `Views/` contains Terminal.Gui windows. `Program.cs`
is the composition root. Tests live in `tests/BiliStreamAudio.Tests/` and target
the public behavior of core and infrastructure code. Keep TUI state out of
transport and protocol helpers so those helpers remain directly testable.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet build BiliStreamAudio-TUI.sln     # restore and compile; warnings fail builds
dotnet test BiliStreamAudio-TUI.sln      # run the xUnit suite
dotnet run --project src/BiliStreamAudio.Tui
dotnet run --project src/BiliStreamAudio.Tui -- --mock
```

Use mock mode for UI checks that must not initialize LibVLC, open WebView2, or
make network calls. The app targets `net10.0-windows`; develop and validate on
Windows with a compatible .NET 10 SDK.

## Coding Style & Naming Conventions

Use C# with four-space indentation, file-scoped namespaces, nullable reference
types, and implicit usings, matching the existing source. Prefer `sealed` for
non-inheritable classes and `record` types for immutable data. Use PascalCase
for types, members, and enum values; camelCase for locals and parameters.
Name implementation files after their primary type (for example,
`DanmakuConnection.cs`). There is no repository formatter or linter configured;
keep edits formatted consistently with nearby code. Treat all compiler warnings
as errors.

## Testing Guidelines

Tests use xUnit. Place new tests in `tests/BiliStreamAudio.Tests`, using a
`<Feature>Tests.cs` file and descriptive methods such as
`Stream_url_joins_host_path_and_extra_once`. Use `[Fact]` for self-contained
cases, assert observable behavior, and cover failure or boundary cases for
protocol parsing, authentication, and network-facing logic. Run `dotnet test
BiliStreamAudio-TUI.sln` before opening a pull request. No coverage threshold
is configured.

## Commit & Pull Request Guidelines

Recent history uses concise, imperative summaries, commonly in Chinese (for
example, `Mock模式`); use the language appropriate to the change and keep the
subject focused on one outcome. Keep commits scoped and buildable. Pull requests
should explain the user-visible change, list tests run, link relevant issues,
and include a screenshot or terminal capture for TUI changes. Do not commit
cookies, tokens, logs containing credentials, or generated `bin/` and `obj/`
files.
