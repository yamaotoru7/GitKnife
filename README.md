# GitKnife

A Git client for Windows (C# / .NET 10 / WPF).

- Commit graph, branches, tags, remotes, stashes, submodules and worktrees in one window
- Local changes with line / hunk staging, side-by-side and inline diff, image diff
- Interactive rebase, merge conflict resolver, blame, file history, Git Flow
- Custom commands, issue tracker links, workspaces
- Repositories on WSL and over SSH (the git of each location is used)
- All Git operations run the git CLI (a portable git is bundled with the installer)

## Install

Download `GitKnife-<version>-setup.exe` from [Releases](../../releases). GitKnife checks the releases for updates once a day (File > Check for Updates...).

## Build

```sh
dotnet build                          # build everything
dotnet test                           # run the tests
dotnet run --project src/GitKnife     # start the app
pwsh tools/git/fetch-portable-git.ps1           # bundled git → vendor/portable-git (without it, git on PATH is used)
pwsh tools/installer/build-installer.ps1        # installer → .work/installer/ (needs Inno Setup 6)
```

## Layout

- `src/GitKnife.Core`: Git operations (one class per operation), models, diff / conflict parsers, graph layout, settings. No UI dependency.
- `src/GitKnife`: WPF app
- `src/GitKnife.AskPass`, `src/GitKnife.RI`: helpers for GIT_ASKPASS / credential prompts and the interactive rebase editor
- `tests/GitKnife.Core.Tests`: xUnit tests

## Reporting bugs

Press Ctrl+Shift+F12 in the app. The report (screenshots, environment, the end of the log) is saved to `%LocalAppData%\GitKnife\bug-reports`, and a new issue is opened in the browser with the text filled in. Drag the screenshots from the opened folder into the issue.
