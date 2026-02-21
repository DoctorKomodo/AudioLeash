# Single-Instance Enforcement Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Prevent duplicate tray icons by making a second AudioLeash instance exit immediately and silently if one is already running.

**Architecture:** Acquire a named system `Mutex` (`Global\AudioLeash`) at the top of `Main`, before any WinForms or audio initialisation. If the mutex is already owned, return immediately. The mutex is held via a `using` declaration for the full process lifetime and released automatically on exit or crash.

**Tech Stack:** C# 12 / .NET 8, `System.Threading.Mutex` (BCL — no new dependencies).

---

### Task 1: Create feature branch

**Files:**
- No file changes yet.

**Step 1: Create and switch to the feature branch**

```bash
git checkout -b feature/single-instance-enforcement
```

Expected: `Switched to a new branch 'feature/single-instance-enforcement'`

---

### Task 2: Add the Mutex guard to Program.cs

> **Note on TDD:** The single-instance behaviour is process-lifecycle enforcement — the only meaningful test is launching two real processes and observing that the second exits. That is an integration/manual test, not a unit test. There is no pure logic to extract (the code is a direct BCL API call with a branch). Per the project testing guidelines ("Do not test WinForms UI mechanics directly"), no unit test is written for this task. All existing tests must still pass after the change.

**Files:**
- Modify: `AudioLeash/Program.cs`

**Step 1: Read the current file**

Open `AudioLeash/Program.cs` and confirm it looks like:

```csharp
using System.Windows.Forms;

namespace AudioLeash
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AudioLeashContext());
        }
    }
}
```

**Step 2: Add the Mutex guard**

Replace the contents of `AudioLeash/Program.cs` with:

```csharp
using System.Threading;
using System.Windows.Forms;

namespace AudioLeash
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Prevent duplicate instances. The Global\ prefix makes the mutex
            // session-global so it works across UAC elevation boundaries.
            using var mutex = new Mutex(initiallyOwned: false, "Global\\AudioLeash", out bool created);
            if (!created || !mutex.WaitOne(0))
                return; // Another instance is already running — exit silently.

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AudioLeashContext());
        }
    }
}
```

**Step 3: Build the solution**

```bash
dotnet build AudioLeash.sln
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

**Step 4: Run existing tests**

```bash
dotnet test AudioLeash.sln
```

Expected: `Passed! - Failed: 0, Passed: 19` (or whatever the current count is)

**Step 5: Commit**

```bash
git add AudioLeash/Program.cs
git commit -m "feat: enforce single instance via named Mutex"
```

---

### Task 3: Update README

**Files:**
- Modify: `README.md`

**Step 1: Mark the feature as implemented**

In `README.md`, find the single-instance enforcement line in the future development section:

```markdown
- **Single-instance enforcement** — If the app is launched a second time while already running, the new process should detect the existing instance and exit immediately rather than creating a duplicate tray icon. Typical implementation uses a named `Mutex` acquired at startup in `Program.cs`.
```

Replace it with:

```markdown
- ~~**Single-instance enforcement**~~ — ✔ Implemented (named `Mutex` in `Program.cs`; second instance exits silently).
```

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: mark single-instance enforcement as implemented"
```

---

### Task 4: Merge to main

**Step 1: Run full test suite one final time**

```bash
dotnet test AudioLeash.sln
```

Expected: all tests pass.

**Step 2: Merge, push, and delete feature branch**

```bash
git checkout main
git merge --no-ff feature/single-instance-enforcement -m "feat: single-instance enforcement via named Mutex"
git push
git branch -d feature/single-instance-enforcement
```
