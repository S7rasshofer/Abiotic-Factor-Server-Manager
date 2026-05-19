# Facility Overseer

**A friendly way to run your own Abiotic Factor dedicated server on Windows.**

Facility Overseer downloads the server for you, lets you set it up with normal
buttons and sliders instead of editing text files, and shows you whether your
server is actually running and reachable. You can manage several worlds, each on
its own tab.

> Motto: *Do not hardcode the facility. Discover it.* â€” if the game adds new
> settings in an update, Facility Overseer keeps them instead of throwing them away.

---

## âš ï¸ Please read first: this is an early build

Facility Overseer **is not finished and has not been tested against a real
running game server yet.** It builds and its internal tests pass, but the
install / launch / backup features have not been verified endâ€‘toâ€‘end in the real
world.

**Treat it as a preview, not a finished tool.** Don't rely on it for a server
you care about, and always keep your own copy of any important save files.

---

## What it can do

- **Install or update the server for you.** It fetches everything needed
  (including SteamCMD) automatically â€” no Steam login required.
- **Manage multiple worlds.** Each world is a tab. Create, copy, or delete them.
- **Set up your server without editing files:** server name, password, admin
  password, max players, game/query ports, LAN-only mode, platform access,
  and more.
- **Tune gameplay with simple controls.** World, Player, and Enemy settings show
  up as toggles, sliders, and dropdowns with plainâ€‘language explanations.
  Anything the app doesn't recognise is kept safely on an *Advanced* tab.
- **Start, stop, and restart** the server from the world toolbar, with a live
  log, player activity history, and status lights for "running", "config OK",
  "responding locally", and "visible to others".
- **Catch mistakes early.** It warns about blank names, bad ports, and two
  worlds fighting over the same port before you start.
- **Backups & restore.** Make a backup any time, and the app automatically backs
  up *before* risky actions (deleting a world, saving settings, updating the
  server). Restoring also snapshots the current state first, so an "oops" is
  recoverable.
- **Network help.** It can add the Windows Firewall rules for your ports and
  give you a copyâ€‘paste checklist for forwarding ports on your router.

### Not available yet

- Importing an existing singleâ€‘player/local world into the server
- Automatic confirmation that your server shows in the inâ€‘game browser
  (it currently says "Unknown" and gives guidance instead)
- Scheduled/automatic recurring backups, backup cleanup, zipped backups

---

## Getting started

1. Download / build `FacilityOverseer.exe` (see below). You do **not** need to
   install .NET or anything else â€” just run the file.
2. Open the app. A first world ("Cascade") is created for you.
3. Click **Prepare / Update Server**. Facility Overseer downloads SteamCMD if
   needed and prepares the Abiotic Factor dedicated server inside its managed
   data folder, then waits for it to finish.
4. On the **Server** tab, give your server a name and (optionally) passwords,
   and check the ports and max players.
5. Adjust gameplay on the **World / Player / Enemy** tabs if you want.
6. (Recommended) On the **Network** info, create the firewall rules and follow
   the router checklist so friends on the internet can join.
7. Press **Start** in the world toolbar. The app switches to *Logs & Status*
   so you can watch the log, player activity, and status lights.

To run more than one world, use the world tabs along the top. Each world has its
own settings, save, and backups.

### Where your files live

| What | Where |
|---|---|
| Managed data root | `FacilityOverseerData` beside `FacilityOverseer.exe` when writable; otherwise `%LOCALAPPDATA%\FacilityOverseer` |
| Server files | `<DataRoot>\servers\abiotic-factor-dedicated` |
| SteamCMD | `<DataRoot>\tools\steamcmd` |
| Backups | `<DataRoot>\backups` |
| Logs | `<DataRoot>\logs` |
| Your world profiles & settings | `<DataRoot>\config` |

The **Backups** tab has an "Open Backup Folder" button if you ever want to grab
a backup by hand.

---

## Troubleshooting

- **Friends can't join over the internet.** Game hosting needs your *router* to
  forward the game and query ports to this PC â€” the app can't do that part for
  you. Use the router checklist on the Network section. Also make sure
  *LAN Only* is turned off.
- **Windows Firewall prompt.** Creating firewall rules asks for administrator
  permission; that's expected.
- **Server won't start.** Check the *Logs & Status* tab â€” configuration errors
  (blank name, bad port, port conflict) are listed there with suggested fixes.
- **Something broke after a change.** Go to the **Backups** tab and restore an
  earlier backup. The app keeps automatic backups from before risky actions.

---

## For developers

Built with C# / WPF / .NET 10. Full plan and progress are in
[`PLAN.md`](PLAN.md) and [`STATUS.md`](STATUS.md).

```pwsh
# Requires the .NET 10 SDK (pinned in global.json)
dotnet build AbioticServerManager.slnx
dotnet test  src/AbioticServerManager.Tests/AbioticServerManager.Tests.csproj
dotnet run   --project src/AbioticServerManager.App/AbioticServerManager.App.csproj

# Single self-contained exe (no .NET needed to run it, ~70 MB, win-x64)
dotnet publish src/AbioticServerManager.App/AbioticServerManager.App.csproj -c Release -o publish
```

| Project | Responsibility |
|---|---|
| `AbioticServerManager.Core` | Models, INI parser, launch args, validation, backup contracts (no IO) |
| `AbioticServerManager.Infrastructure` | SteamCMD, process runner, persistence, backups, diagnostics |
| `AbioticServerManager.App` | WPF MVVM shell (two-axis tabs) |
| `AbioticServerManager.Tests` | xUnit tests |
