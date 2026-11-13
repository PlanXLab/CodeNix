# CodeNix

CodeNix is a portable Windows development environment installer and updater. The current repository is built around two WinForms executables:

- `CodeNix.exe`: initial installation
- `launcher.exe`: launch, version checks, and upgrades

The final distributable is `CodeNix.exe` only. `launcher.exe` is embedded into the installer and extracted into the target folder during installation.

## Download

- Latest `CodeNix.exe`: https://github.com/PlanXLab/CodeNix/releases/latest/download/CodeNix.exe

## Current Architecture

### Installer

`CodeNix.exe` installs the latest available versions of the following components:

- VS Code
- uv and the latest stable uv-managed Windows x64 CPython
- PowerShell 7
- Oh My Posh
- PowerShell modules: `PSFzf`, `modern-unix-win`
- Fonts: `0xProto Nerd Font`, `DalseoHealing`
- VS Code extensions:
  - `teabyii.ayu`
  - `ms-python.python`
  - `ms-python.vscode-pylance`
  - `ms-python.vscode-python-envs`
  - `ms-python.debugpy`
  - `KevinRose.vsc-python-indent`
  - `usernamehw.errorlens`

Oh My Posh is initialized with the bundled theme at `data\lib\pwsh\ohmyposh\themes\tos.omp.json` using its explicit `--config` option.

`Terminal-Icons` is not installed or updated; the portable profile uses `lsd` for directory listings and icons. Existing `Terminal-Icons` files are left in place and are not imported.

Installer UI behavior:

- Default installation path is `C:\VSCode`
- Leave the Python version blank for the latest stable release, enter `x.y` for the latest patch, or enter `x.y.z` for an exact stable release
- The resolved version replaces the request in the field during installation; unavailable stable releases are rejected before setup begins
- The uv archive is verified against the release's SHA256 digest
- The UI provides overall progress, per-component progress, and a filtered Key Events log

### Launcher

`launcher.exe` is the runtime entry point for an installed environment.

- Launches `Code.exe` in portable mode
- Shows the upgrade prompt when `upgrade_*` flags already exist
- Launches VS Code without waiting for network update checks when no upgrade flags are pending
- Checks package updates and reconciles desktop and Start Menu shortcuts in the background after launch
- Creates `upgrade_*` flags for updates to be offered the next time the launcher starts

Move the entire installation folder to another PC, then run `launcher.exe`. When no upgrade flags are pending, CodeNix applies the portable settings needed for the current run and starts VS Code immediately. It then refreshes shortcut targets, registers bundled fonts, repairs the PowerShell theme, rebases Python project environments, and checks for package updates in the background. Available updates are offered on a later launcher start. VS Code, Python, PowerShell, extensions, modules, and PowerShell command history remain under the portable folder. PowerShell history is stored at `data\user-data\PowerShell\History.txt`. Update-check and setup issues are recorded in `codenix.log` while VS Code continues running.

Upgradeable components:

- uv (Python runtimes and project packages are never automatically upgraded)
- PowerShell 7
- Oh My Posh
- PSFzf
- modern-unix-win
- VS Code

### Version and State Files

The installation root contains the following runtime files:

- `codenix.info`: installation path and installed component versions
- `codenix.log`: installation log
- `upgrade.log`: upgrade log
- `upgrade_*`: launcher-generated upgrade flag files

`PYTHON_VERSION` records the initially installed runtime for diagnostics; it is not a version pin or an update target. Users can add or remove any runtime with uv, including the last runtime or one referenced by a project. CodeNix does not prevent deletion, silently choose a replacement, or reinstall a deleted runtime. VS Code and PowerShell can still launch with no Python runtimes installed.

## Installed Folder Layout

A typical installed layout looks like this:

```text
<install-root>/
├─ Code.exe
├─ launcher.exe
├─ codenix.info
├─ codenix.log
├─ upgrade.log
├─ data/
│  ├─ extensions/
│  ├─ user-data/
│  ├─ venvs/
│  │  └─ global/
│  └─ lib/
│     ├─ fonts/
│     ├─ pwsh/
│     ├─ python/
│     └─ origin/
└─ resources/
```

## Usage

### End Users

1. Download `CodeNix.exe` from GitHub Releases.
2. Run it and choose the installation path. Leave the Python version blank for the latest stable CPython, or enter a version as `x.y` or `x.y.z`.
3. After installation, launch VS Code through `launcher.exe` or the created shortcut.
4. From that point on, `launcher.exe` checks for updates and handles upgrades.

### Maintenance Mode

`launcher.exe --init` runs the internal maintenance routine.

- Synchronizes the VS Code extension set
- Restores configuration and origin files
- Preserves user Python runtimes, caches, and project environments
- Reinstalls fonts and recreates shortcuts
- Cleans user-data and launches VS Code

## Terminal (PowerShell 7)

The integrated terminal runs **PowerShell 7** with a custom profile that provides zsh-style editing, fuzzy history, and abbreviated path completion on Windows. PowerShell syntax and process semantics still apply. Type `??` at the prompt to list the bundled Unix commands.

The prompt has two distinct levels: a framed information header with a path badge, Git state, and right-aligned clock icon, followed by a separate `╰─ ❯` input rail with a matching `─╯` right prompt. Drive icons and letters are muted blue-gray, intermediate paths are steel blue, and the current folder remains cyan at regular weight without underline. The header remains visible outside Git repositories. Git is green when clean, amber when changed, and red on conflicts; failed commands turn the input marker red. Submitted prompts reduce to a single `❯` line. Narrow terminals prioritize the frame and input rail: the clock appears from 50 columns, Git details from 70, and failure codes or durations of at least one second from 80 columns.

### Editing and Completion

| Key | Action |
|-----|--------|
| `Ctrl+r` | Search unique history entries with fzf, newest first; Enter selects without executing, Esc cancels |
| `Ctrl+t` | Select a file with PSFzf |
| `Tab` / `Shift+Tab` | Complete paths and commands; cycle completion choices |
| `Up` / `Down` | Search history using the current input prefix |
| `Ctrl+a` / `Ctrl+e` | Move to the beginning/end of the line |
| `Ctrl+b` / `Ctrl+f` | Move backward/forward one character; forward accepts inline suggestions |
| `Alt+b` / `Alt+f` | Move backward/forward one word |
| `Ctrl+w` / `Alt+Backspace` | Delete the previous word |
| `Alt+d` / `Ctrl+k` | Delete the next word/rest of the line |
| `Ctrl+u` / `Ctrl+y` | Delete to the beginning of the line / yank deleted text |
| `Ctrl+l` / `Ctrl+_` | Clear the screen / undo |

Path completion expands intermediate directory prefixes, not just the final component. For example, `cd D:/P/O/W/g` followed by Tab can expand to `D:/PlanXLab/Original/Workspace/github/`. Multiple matches remain available in the completion menu. Spaces and literal special characters are quoted safely. This also applies to `pushd` and common PowerShell file commands.

A drive prefix such as `D:` starts completion at that drive's root. Without a colon, completion starts in the current directory and stays relative: `cd Code` can complete to `cd CodeNix\`. Likewise, `D/P/O/W/g` only searches matching local subdirectories; it never falls back to drive D. If no local match exists, the input is left unchanged. Directory completions retain a trailing separator so another component can be entered immediately.

Python filenames entered directly remain unchanged on screen and in history. Python commands use an explicitly activated virtual environment when present, otherwise CodeNix's global environment. The profile does not search for or repair project `.venv` environments. A `#!replx` shebang requires `replx` to be installed in the selected environment.

VS Code discovers a project `.venv` when one exists without configuring it as a required default, so workspaces without `.venv` do not retain a stale interpreter path. Pylance also uses CodeNix's `data\venvs\global\Lib\site-packages` for completion and import analysis. The launcher resolves this path from the current install location on startup, so it remains portable when CodeNix is moved; it does not change runtime imports or the selected interpreter.

### Navigation

| Command | Description |
|---------|-------------|
| `..` | Go up one directory |
| `...` | Go up two directories |
| `....` | Go up three directories |
| `z <dir>` | Jump to a frequently visited directory (zoxide) |
| `zi` | Interactively select a directory to jump to (zoxide + fzf) |
| `cd -` | Return to the previous directory |
| `pushd <dir>` / `popd` | Push/pop the directory stack |

### File and Directory Listing

| Command | Alias for | Description |
|---------|-----------|-------------|
| `ls` | `lsd` | Colorized directory listing with icons |
| `ll` | `lsd -l` | Long format listing |
| `la` | `lsd -la` | Long format including hidden files |
| `cat <file>` | `bat` | Syntax-highlighted file viewer with Git integration |
| `find` | `fd` | Fast, user-friendly alternative to `find` |
| `du` | `dust` | Disk usage with visual tree |
| `df` | `duf` | Disk free space overview |

When listing a Windows drive root, `ls`, `ll`, and `la` exclude reserved protected entries such as `pagefile.sys`, `System Volume Information`, and `WindowsApps` before lsd reads their metadata. Ordinary directories are unchanged, and real path errors remain visible. Invoke `lsd` directly for an unfiltered listing.

The installer upgrades bundled lsd versions older than 1.2.0 using the official Windows release with a pinned SHA256 checksum and preserves the previous executable. This avoids legacy ACL parsing errors that could omit otherwise readable folders. Windows listings use file attributes rather than Unix permission metadata; no filesystem permissions or ACLs are changed. A standalone lsd in the portable PowerShell `bin` directory takes precedence over the module copy.

### Text Processing

| Command | Alias for | Description |
|---------|-----------|-------------|
| `grep <pattern>` | `rg` | Recursive regex search (ripgrep) |
| `sed` | `sd` | Intuitive find-and-replace |
| `jq` | — | Command-line JSON processor |
| `delta` | — | Syntax-highlighted diff viewer |

### System and Process Monitoring

| Command | Alias for | Description |
|---------|-----------|-------------|
| `ps` | `procs` | Modern process list |
| `top` | `btm` | Interactive graphical process/system monitor |
| `ping <host>` | `gping` | Ping with a real-time graph |

### Network

| Command | Alias for | Description |
|---------|-----------|-------------|
| `http` | `xh` | Send HTTP requests (HTTPie-style) |
| `dig <host>` | `dog` | DNS lookup |
| `curlie` | — | curl with httpie-style output |

### Miscellaneous Tools

| Command | Description |
|---------|-------------|
| `fzf` | Interactive fuzzy finder |
| `broot` | Interactive tree explorer and launcher |
| `cheat <cmd>` | View community cheatsheets for a command |
| `hyperfine <cmd>` | Command-line benchmarking |
| `which <cmd>` | Show the full path of a command |
| `clh` | Clear all terminal history (file, buffer, and session) |

### Keyboard Shortcuts (PSReadLine / Emacs mode)

| Key | Action |
|-----|--------|
| `Ctrl+A` | Move to beginning of line |
| `Ctrl+E` | Move to end of line |
| `Alt+B` | Move backward one word |
| `Alt+F` | Move forward one word |
| `Ctrl+U` | Delete to beginning of line |
| `Ctrl+K` | Delete to end of line |
| `Tab` | Menu-style autocomplete |
| `Ctrl+R` | Fuzzy search command history (fzf) |
| `Ctrl+T` | Fuzzy file picker (fzf) |
| `↑` / `↓` | Navigate history predictions (inline list view) |

### Running Python Scripts

For project-specific dependencies, use a project virtual environment:

```powershell
uv init --python 3.12
uv add replx
uv run main.py
```

Request another Python version in `uv init --python <version>` when needed. uv stores runtimes under `data\lib\python`, and the default selection is not fixed by CodeNix. In VS Code, use **Python: Select Interpreter** to select the project's `.venv\Scripts\python.exe`. The **Run Python File in Terminal** button uses that interpreter directly. Newly opened integrated terminals automatically activate the selected environment, so terminal `python` and `pip` use the same environment. Existing terminals retain their current environment until reopened.

Outside VS Code, `uv venv` creates but does not activate `.venv`; use `uv run` as above or activate it explicitly:

```powershell
.\.venv\Scripts\Activate.ps1
python main.py
```

In an existing installation, set `"python-envs.terminal.autoActivationType": "command"` and `"python.terminal.activateEnvironment": true` in VS Code's user settings, and remove any workspace overrides that disable activation. Close existing terminals and open a new one for the settings to take effect. CodeNix does not change execution policy automatically; if local policy blocks activation, use `uv run` instead. Project environments are not retargeted when runtimes change; the global pip environment remains tied to the runtime selected when it was created.

For pip-style global installs in a CodeNix PowerShell terminal, use the bundled `pip.ps1` wrapper:

```powershell
pip install requests
pip install -U requests
pip list
pip uninstall requests
python -c "import requests; print(requests.__version__)"
```

The wrapper targets one automatically managed environment at `data\venvs\global`; its `Scripts` directory is on the launcher's process PATH, so `python` and package command-line scripts work from any working directory. Packages installed there are separate from each project's `.venv`. The environment uses the Python runtime selected when it was created; removing that runtime can make the global environment unusable until it is restored or recreated.

The shared uv cache is at `data\cache\uv`. Compatible package files are reused through hardlinks when the cache and environment are on a supporting filesystem on the same volume. Package installation state and uninstall operations remain independent per environment. Cross-volume or unsupported filesystems may require copies. Do not edit cached or hardlinked package files in place.

Keep portable projects under `<install-root>\projects`. On launch, CodeNix rebases moved `.venv` environments there only when the exact original runtime exists in the current portable store. Rebasing uses `--allow-existing --no-python-downloads` to preserve packages and never downloads a replacement for a deleted runtime. The PowerShell profile does not repair environments when running scripts. Standard virtual environments with non-relocatable console scripts may need recreation after a move.

Python scripts can be run directly from the terminal without typing `python`:

```powershell
foo.py
./foo.py
./ch1/foo.py
```

If the first line of the script is exactly `#!replx`, it is executed with `replx` instead:

```python
#!replx
print("Hello from replx")
```

```powershell
foo.py
```

Without an explicitly activated environment, direct `.py` execution uses CodeNix's global environment; the profile does not discover or repair nearby project `.venv` environments. Activate a project environment to use it, or use `uv run main.py` for uv's project-specific behavior. Use `uv run replx main.py` to invoke replx explicitly (uv does not interpret the custom `#!replx` marker). `uv run` outside a project is not itself a guarantee of virtual-environment execution.

## Build and Verification

Windows and the .NET Framework C# compiler are required. The build embeds `launcher.exe` into `dist\CodeNix.exe`.

```powershell
.\.test\build.ps1
.\.test\runtime.Tests.ps1
.\.test\runtime.Tests.ps1 -Network
```

The network suite installs uv and CPython in a disposable directory and verifies package reuse, isolated removal, uv-only replacement, last-runtime deletion, and moved-environment rebasing. This is a new installation format; upgrading installations from the previous Embedded Python/pip layout is not supported.
