# CRITICAL

RESPECT THE WORKFLOW BELOW!!!
NEVER: leave uncommitted or unpushed changes - always maintain a consistent and backed-up repository state
ALWAYS: Consider if a web research for current best practices could be useful.
ALWAYS: Consider if a web research for existing framework components that cover the requirements
ALWAYS: Update README.md when changes affect user-facing behaviour, features, setup steps, project structure, or future development ideas

# PROJECT OVERVIEW

**AudioLeash** is a lightweight Windows system tray application written in **C# (.NET 8)** that prevents Windows from automatically switching the user's audio output device. When external events (e.g. plugging in USB headphones) cause Windows to change the default audio device, AudioLeash detects the change and restores the user's chosen device.

The application runs headlessly — no main window — and lives entirely in the Windows notification area (system tray).

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# 12 (.NET 8) |
| UI | Windows Forms (WinForms) — system tray only |
| Audio API | AudioSwitcher.AudioApi.CoreAudio 3.1.0 |
| Reactive events | System.Reactive (Rx.NET) via AudioSwitcher |
| Target OS | Windows 10 / 11 only |
| Build | `dotnet build AudioLeash.sln -c Release` |

## Project Structure

```
AudioLeash/
├── AudioLeash.sln
├── claude.md                    ← this file
├── README.md
└── AudioLeash/
    ├── AudioLeash.csproj
    ├── Program.cs               ← Entry point; STA thread, WinForms bootstrap
    ├── AudioSwitcherContext.cs  ← All application logic (tray, menu, device events)
    └── Resources/
        └── icon.ico             ← (optional) custom tray icon
```

## Key Design Constraints

- The application is **single-file logic** (`AudioSwitcherContext.cs`) — keep this architecture unless there is a strong reason to split.
- All UI interactions must occur on the **UI/STA thread**. Background Rx events must be marshalled via `Control.Invoke`.
- An `isInternalChange` flag prevents feedback loops when the app itself triggers a device change.
- No settings are persisted to disk or registry today — selected device is in-memory only.

---

# WORKFLOW

## Git Branching

- Work on feature branches, NOT directly on main/master
- Create a new branch for each task: `git checkout -b feature/<descriptive-name>`
- Commit changes to the feature branch with conventional commit messages
- Only merge to main when the user approves the changes
- After merge approval: merge to main with `--no-ff`, push, and delete the feature branch
- Keep feature branches focused on a single task/feature

## Conventional Commits

Use the following prefixes for all commit messages:

| Prefix | Use for |
|---|---|
| `feat:` | New feature or capability |
| `fix:` | Bug fix |
| `refactor:` | Code change that is neither a fix nor a feature |
| `test:` | Adding or updating tests |
| `docs:` | Documentation only changes |
| `chore:` | Build process, tooling, or dependency updates |
| `style:` | Formatting, whitespace (no logic change) |

Example: `feat: add settings persistence via JSON file`

## Code Review Process

After completing a task do two subsequent reviews:

1. **First**: review your changes with a subagent that focuses on the big picture, how the new implementation is used and which implications arise
2. **Second**: review your changes with a subagent the default way

Address findings and ask back if anything unclear.

## Testing

### Framework

Use **xUnit** for unit tests and **NSubstitute** for mocking. Tests live in a sibling project:

```
AudioLeash/
├── AudioLeash.sln
├── AudioLeash/          ← main project
└── AudioLeash.Tests/    ← test project (xUnit)
    └── AudioLeash.Tests.csproj
```

If a test project does not yet exist, create it with:

```bash
dotnet new xunit -n AudioLeash.Tests -o AudioLeash.Tests
dotnet sln AudioLeash.sln add AudioLeash.Tests/AudioLeash.Tests.csproj
dotnet add AudioLeash.Tests/AudioLeash.Tests.csproj reference AudioLeash/AudioLeash.csproj
dotnet add AudioLeash.Tests/AudioLeash.Tests.csproj package NSubstitute
```

### Running Tests

```bash
dotnet test AudioLeash.sln
```

### What to Test

- **Unit test** pure logic: device selection state, loop-prevention flag transitions, graceful-unavailable-device handling.
- **Do not** write tests that require a real Windows audio stack — mock `IAudioController` / `IDevice` interfaces provided by AudioSwitcher.
- **Do not** test WinForms UI mechanics directly; extract logic into testable methods/classes first.

### Test Quality Bar

- Every new feature must include corresponding tests before the feature branch is merged.
- All tests must pass (`dotnet test`) before requesting a merge review.
- Aim for meaningful coverage of business logic, not arbitrary percentage targets.

---

# DOCUMENTATION & RESEARCH

## MCP / context7

This project uses the **context7 MCP integration** for resolving library documentation. Before implementing anything that involves an external library or API, use context7 to retrieve up-to-date documentation:

```
use context7 to look up: <library or topic>
```

Prefer context7 over guessing API shapes — library APIs (especially AudioSwitcher and Rx.NET) change between versions, and incorrect assumptions cause subtle bugs.

## Web Research

Before starting any non-trivial implementation, consider whether a web search would be valuable:

- **Coding patterns**: Search for established C#/.NET patterns for the problem (e.g. "C# system tray application best practices .NET 8").
- **UI/UX design**: Search for Windows tray application UX guidelines before making UI decisions.
- **NuGet packages**: Search for existing packages that solve the requirement before writing custom code.
- **Security**: If the feature touches registry, file system, or inter-process communication, search for Windows security guidance.
- **Audio API quirks**: The Windows Core Audio API has many undocumented edge cases — search before assuming standard behaviour.

---

# CODE QUALITY STANDARDS

## C# Style

- Follow **Microsoft C# Coding Conventions** (https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).
- Use **nullable reference types** (`#nullable enable`) — all new code must be null-safe.
- Prefer `async/await` over raw `Task.Run` + callbacks where it improves readability.
- Use `using` declarations (C# 8+) rather than `using` blocks unless scope control is needed.
- Dispose all `IDisposable` objects — especially `CoreAudioController`, `NotifyIcon`, and Rx subscriptions.

## General Principles

- **YAGNI** — do not add features or abstractions not required by the current task.
- **Single Responsibility** — if a method grows beyond ~40 lines, consider splitting it.
- **Fail loudly in development, gracefully in production** — use `Debug.Assert` for invariants, structured error handling for user-facing paths.
- Avoid `catch (Exception ex)` swallowing — always log or surface errors meaningfully.
- Prefer `readonly` fields and immutable data where possible.

## Windows-Specific

- The app targets Windows only (`net8.0-windows`) — do not add cross-platform abstractions.
- When interacting with Win32 / COM APIs, always check return values and `Marshal.GetLastWin32Error()`.
- Test device-change handling with both USB and Bluetooth devices where possible, as their event behaviour differs.

---

# BUILD & DEVELOPMENT COMMANDS

```bash
# Restore dependencies
dotnet restore AudioLeash.sln

# Build (debug)
dotnet build AudioLeash.sln

# Build (release)
dotnet build AudioLeash.sln -c Release

# Run tests
dotnet test AudioLeash.sln

# Run tests with verbose output
dotnet test AudioLeash.sln --logger "console;verbosity=detailed"

# Publish self-contained executable
dotnet publish AudioLeash/AudioLeash.csproj -c Release -r win-x64 --self-contained true
```

> The application can only be **run** on Windows. Build and test steps may be executed in a Linux CI environment (excluding integration tests that require the Windows audio stack).

---

# FUTURE DEVELOPMENT AREAS

The following are tracked ideas from the README. Reference this list when scoping new tasks — do not implement features from this list unless explicitly requested:

- Windows startup registration (registry `Run` key)
- Global hotkey to cycle audio devices
- "Default communications device" support alongside playback device
- Recording/microphone device support
- Named profiles (switch playback + recording together)
- Per-application audio routing (Windows 10+)
- Settings persistence (JSON file or registry)
- Tray icon tooltip showing selected device name
- Dark/light theme icon variants
- Volume indicator / master volume control from tray
- Migrate off `AudioSwitcher.AudioApi.CoreAudio` → NAudio + custom `PolicyConfigClient.cs` COM interop (see README for rationale)
