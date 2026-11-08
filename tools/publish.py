"""Publish the built pack: upload missing release assets, then commit + push pack/.

Order matters: assets first, index second - players must never see an index that points at a
file that isn't uploaded yet.

Run:  python tools/publish.py [-m "changelog message"] [--dry-run]
"""
import argparse, json, subprocess, sys, tomllib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CFG = json.loads((REPO / "tools" / "pack_config.json").read_text(encoding="utf-8"))
ASSET_DIR = Path(CFG["asset_staging_dir"])
GH_REPO, TAG = CFG["github_repo"], CFG["asset_release_tag"]


def run(*cmd, check=True, capture=False):
    print("+", " ".join(cmd))
    r = subprocess.run(cmd, cwd=REPO, text=True, capture_output=capture)
    if check and r.returncode:
        sys.exit(f"failed ({r.returncode}): {r.stderr if capture else ''}")
    return r


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-m", "--message", default=None)
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    state = json.loads((REPO / "tools" / "last_build.json").read_text())
    needed = set(state["release_assets"])

    if run("gh", "release", "view", TAG, "-R", GH_REPO, check=False, capture=True).returncode:
        if a.dry_run:
            print(f"[dry-run] would create release '{TAG}'")
        else:
            run("gh", "release", "create", TAG, "-R", GH_REPO, "--title", "Pack files (do not delete)",
                "--notes", "Hosted mod/resource files for the packwiz index. Managed by tools/publish.py.")
    have = set()
    r = run("gh", "release", "view", TAG, "-R", GH_REPO, "--json", "assets", check=False, capture=True)
    if r.returncode == 0:
        have = {x["name"] for x in json.loads(r.stdout)["assets"]}
    missing = sorted(needed - have)
    print(f"release assets: {len(needed)} needed, {len(have)} uploaded, {len(missing)} missing")
    for i in range(0, len(missing), 10):
        batch = [str(ASSET_DIR / n) for n in missing[i:i + 10]]
        if a.dry_run:
            print("[dry-run] would upload", missing[i:i + 10])
        else:
            run("gh", "release", "upload", TAG, "-R", GH_REPO, *batch)
    unused = sorted(have - needed)
    if unused:
        print(f"note: {len(unused)} old assets no longer referenced (kept so older pack versions still "
              f"install; prune with tools/prune_assets.py once nobody is on them)")

    msg = a.message or f"Pack {state['version']}"
    if a.dry_run:
        run("git", "status", "--short", check=False)
        print(f"[dry-run] would commit '{msg}' and push")
        return
    run("git", "add", "-A")
    # every non-metafile in the index must actually be in git, or players get a 404
    tracked = set(subprocess.run(["git", "-c", "core.quotepath=off", "ls-files", "-z", "pack"], cwd=REPO,
                                 capture_output=True).stdout.decode("utf-8").split("\0"))
    with open(REPO / "pack" / "index.toml", "rb") as f:
        files = [e["file"] for e in tomllib.load(f)["files"]]
    lost = [f for f in files if "pack/" + f not in tracked]
    if lost:
        sys.exit(f"refusing to publish: {len(lost)} indexed files are not tracked by git (gitignored?): {lost[:5]}")
    if run("git", "diff", "--cached", "--quiet", check=False).returncode == 0:
        print("nothing changed in the repo")
        return
    run("git", "commit", "-q", "-m", msg)
    run("git", "push", "-q", "origin", "HEAD")
    print(f"published {state['version']} - players get it on their next launch")


if __name__ == "__main__":
    main()
