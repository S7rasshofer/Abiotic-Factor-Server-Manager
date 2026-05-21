# Facility Overseer

**A friendly Windows app for running your own Abiotic Factor dedicated server.**

Facility Overseer is a thin, friendly wrapper around the *official* Abiotic
Factor dedicated server. It downloads the server for you with SteamCMD, turns
the server's text-file settings into normal buttons and sliders, and
starts / stops / monitors it for you. You never have to open a config file or
type a command. You can run several worlds at once, each on its own tab.

> Motto: *Do not hardcode the facility. Discover it.* If the game adds new
> settings in an update, Facility Overseer shows them anyway instead of
> dropping them.

---

## Status

Young software, but it works. It has been used to host real sessions with
friends - a couple of short play-tests that connected and ran successfully.

Treat it as a capable early release: keep your own copy of any save you would
be sad to lose, and you will be fine. The **Backups** tab makes that easy, and
the app also backs up on its own before anything risky.

---

## What it does

- **Installs the server for you.** It fetches everything needed - including
  SteamCMD - automatically. No Steam login required.
- **Runs multiple worlds.** Each world is a tab. Create, clone, or delete them.
- **Configures the server without editing files:** name, password, admin
  password, max players, game / query ports, LAN-only mode, platform access.
- **Tunes gameplay with simple controls.** World, Player, and Enemy settings
  appear as toggles, sliders, and dropdowns with plain-language explanations.
- **Starts, stops, and restarts** the server, with a live log, player activity
  history, and status lights.
- **Catches mistakes early.** It warns about blank names, bad ports, and two
  worlds fighting over the same port before you start.
- **Backs up and restores.** Make a backup any time; the app also backs up
  automatically before risky actions, so an "oops" is recoverable.
- **Helps with networking.** It can add the Windows Firewall rules for your
  ports and give you a copy-paste checklist for forwarding ports on your router.

---

## How to use it (for average users)

You do not need to know anything technical. Follow these steps in order.

1. **Get the app.** Download `FacilityOverseer.exe` (or build it - see
   *For developers* below). It is a single file. You do **not** need to install
   .NET, Steam, or anything else - just double-click it.

2. **Open it.** A first world called **"My World"** is created for you. You can
   rename it later on the *Server* tab.

3. **Prepare the server.** Click **Prepare / Update Server** in the top bar.
   Facility Overseer downloads SteamCMD and then the Abiotic Factor dedicated
   server into its own managed folder. This can take a while the first time -
   the progress bar tells you what it is doing. Wait for it to finish.

4. **Set up the server.** On the **Server** tab, give your server a name. A
   password is optional (leave it blank for an open server). Set an admin
   password if you want in-game admin powers, and check the max players and the
   game / query ports.

5. **Tune the game (optional).** Use the **World**, **Player**, and **Enemy**
   tabs to adjust difficulty, loot, enemies, and more. Every control has a
   short explanation. If you change nothing, sensible defaults are used.

6. **Open it to the internet (optional but recommended for online play).** On
   the **Network** tab, click **Create Firewall Rules**, then follow the
   **Router Checklist** to forward the game and query ports (by default
   **7777** and **27015**) to this PC. If you only want LAN play, turn on
   *LAN Only* and skip this step.

7. **Start it.** Press **Start** in the world's toolbar. The app switches to
   **Logs & Status** so you can watch the log, the player list, and the status
   lights. Press **Stop** when you are done.

8. **Run more worlds (optional).** Use the tabs along the top to add and switch
   between worlds. Each world keeps its own settings, save, and backups.

**If something goes wrong**, check the **Logs & Status** tab first - most
problems (blank name, bad port, port conflict) are listed there with a
suggested fix. See *Troubleshooting* below.

---

## How the app works (and why it rarely needs updates)

Facility Overseer is deliberately simple under the hood. It is a **wrapper**:
it does, with a friendly interface, exactly what you would do by hand if you
followed a dedicated-server guide. There are three jobs:

1. **Get the server.** It runs **SteamCMD** (the same tool Valve provides) to
   download and update the Abiotic Factor dedicated server - Steam app id
   **2857200** - with an anonymous login.
2. **Launch the server.** It builds the server's command line from your
   settings (name, ports, passwords, max players, and so on) and runs the
   server executable, capturing its log.
3. **Edit the settings.** The server's gameplay options live in a text file,
   `SandboxSettings.ini`. The app reads that file and presents each option as a
   control, then writes your changes back.

The app holds **no copy of the game itself** and adds nothing on top of it. It
only arranges files and command-line arguments that the official server
already understands.

### Built for longevity

The design goal is that a normal Abiotic Factor game update should **not**
require a new version of Facility Overseer:

- **Settings are discovered, not hardcoded.** The app reads whatever is in
  `SandboxSettings.ini`. If a game update adds a new setting, it simply appears
  as a new control. If the app does not recognise a setting, it still shows it
  (on the **Advanced** tab) instead of throwing it away - nothing is lost.
- **The server executable is found, not assumed.** The app searches for the
  server's program file rather than relying on a fixed name or path, so a moved
  or renamed file is still found.
- **Extra launch options pass straight through.** Anything you add to a world's
  *Additional Launch Arguments* is handed to the server verbatim, so you can
  use new server flags before the app ever has a control for them.
- **The labels can be refreshed without a rebuild.** The friendly names and
  help text for settings come from a data file that can be updated in place,
  separately from the program.
- **SteamCMD repairs itself.** If SteamCMD's own download breaks (a known,
  common failure), the app detects it and reinstalls SteamCMD cleanly.

The only thing genuinely pinned to Abiotic Factor is the Steam app id
(**2857200**), which does not change. Everything else adapts. The result is a
tool that should keep working across game updates with little or no
maintenance.

---

## Where your files live

Facility Overseer keeps everything it manages inside **one folder**, so there
is only ever one place to look, to back up, or to clear out for a fresh start.

| Situation | The data folder is... |
|---|---|
| Normal install | `FacilityOverseerData`, right beside `FacilityOverseer.exe` |
| The exe sits in OneDrive / Dropbox / Google Drive, or a read-only location | `%LOCALAPPDATA%\FacilityOverseer` |

The second case is automatic and you do not have to think about it. Cloud-sync
tools lock and "dehydrate" files while SteamCMD is writing them, which corrupts
the download. So when the app notices it is running from a synced folder, it
puts its data in a safe local folder instead.

Inside the data folder:

| Subfolder | Contents |
|---|---|
| `config`  | Your world profiles and settings |
| `servers` | The Abiotic Factor dedicated server |
| `tools`   | SteamCMD |
| `backups` | Automatic and manual backups |
| `logs`    | App logs |
| `players` | Per-world player history |

The **Backups** tab has an *Open Backup Folder* button if you ever want to grab
a backup by hand.

---

## Troubleshooting

- **Friends cannot join over the internet.** Online hosting needs your *router*
  to forward the game and query ports to this PC - the app cannot do that part
  for you. Use the **Router Checklist** on the *Network* tab. Also make sure
  *LAN Only* is turned off.
- **Windows Firewall prompt.** Creating firewall rules asks for administrator
  permission. That is expected.
- **The server will not start.** Check the **Logs & Status** tab. Configuration
  errors (blank name, bad port, port conflict) are listed there with suggested
  fixes.
- **The server download fails ("Failed to load steam.dll" or similar).** This
  is a known SteamCMD problem, usually caused by antivirus or a cloud-synced
  folder. The app tries to repair it automatically and writes a report into the
  `logs` folder if it cannot.
- **Something broke after a change.** Open the **Backups** tab and restore an
  earlier backup. The app keeps automatic backups from before risky actions.

---

## Credits and references

The dedicated-server knowledge built into Facility Overseer - the Steam app id,
launch parameters, default ports, and the config / save file layout - follows
the community documentation by **DFJacob**:

- [DFJacob/AbioticFactorDedicatedServer](https://github.com/DFJacob/AbioticFactorDedicatedServer)
- [Wiki: setup, launch parameters, sandbox options, save migration](https://github.com/DFJacob/AbioticFactorDedicatedServer/wiki)

If you want to understand or run the server by hand, that documentation is the
recommended starting point. Facility Overseer aims to do the same things it
describes, just with a UI.

Abiotic Factor is a game by Deep Field Games / Playstack. This project is an
unofficial, fan-made tool and is not affiliated with them.

---

## For developers

Built with C# / WPF / .NET 10.

```pwsh
# Requires the .NET 10 SDK (pinned in global.json)
dotnet build AbioticServerManager.slnx
dotnet test  AbioticServerManager.slnx

# Run the app from source
dotnet run --project src/AbioticServerManager.App/AbioticServerManager.App.csproj

# Single self-contained exe (no .NET needed to run it, win-x64)
dotnet publish src/AbioticServerManager.App/AbioticServerManager.App.csproj -c Release -o publish
```

| Project | Responsibility |
|---|---|
| `AbioticServerManager.Core` | Models, INI parser, launch args, validation, schema, networking math - pure logic, no IO |
| `AbioticServerManager.Infrastructure` | SteamCMD, process runner, persistence, backups, diagnostics, firewall |
| `AbioticServerManager.App` | WPF MVVM shell (world tabs and feature tabs) |
| `AbioticServerManager.Tests` | xUnit tests |

A deeper architecture write-up lives in
[`docs/CURRENT_BUILD.md`](docs/CURRENT_BUILD.md).
