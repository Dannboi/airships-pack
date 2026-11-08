# Airships: Conquer the Mines — pack

Auto-updating modpack for Harvy's NeoForge 1.21.1 server. Players launch through
[Prism Launcher](https://prismlauncher.org); every launch syncs the pack from this repo with
[packwiz](https://packwiz.infra.link), downloading only what changed.

## For players
Download **AirshipsInstaller.exe** from the [latest installer release](../../releases/latest) and run it.
It sets up everything (launcher, Java, the pack, desktop shortcut). The first launch asks you to log in
with your Microsoft account, then downloads the mods (~1.5 GB, one time).

Windows may show "Windows protected your PC" because the installer isn't code-signed: click
**More info → Run anyway**.

Already use Prism? Import `AirshipsInstance.zip` from the same release instead
(Add Instance → Import).

## For Harvy (updating the pack)
1. Change the Technic client pack as usual (add/update/patch mods, edit configs).
2. `python tools/build_pack.py`   — rebuilds `pack/` from the Technic pack
3. `python tools/publish.py -m "what changed"` — uploads new files + pushes

Players get the update the next time they press Play. Configure sources/excludes in
`tools/pack_config.json`.

Layout: `pack/` = packwiz index (served via raw.githubusercontent.com) · mod jars not on Modrinth
live on the `files` release · `installer/` = the Windows installer source · `tools/` = build scripts.
