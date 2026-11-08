"""Regenerate the packwiz pack (pack/) from the Technic client instance.

Source of truth = Harvy's Technic client pack folder. Every run rebuilds pack/ from scratch:
  * small files (configs, scripts, ...) are copied into pack/ verbatim
  * big files (>= BIG bytes, all mod jars) become packwiz metafiles (*.pw.toml):
      - found on Modrinth by sha1  -> download straight from Modrinth
      - otherwise                  -> hosted as an asset on the GitHub release "files"
        (staged in ASSET_DIR; tools/publish.py uploads the missing ones)

Run:  python tools/build_pack.py [--source <technic pack dir>]
Then: python tools/publish.py   (uploads assets, commits, pushes)
"""
import argparse, hashlib, json, os, re, shutil, sys, urllib.request
from datetime import date
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
PACK = REPO / "pack"
CFG = json.loads((REPO / "tools" / "pack_config.json").read_text(encoding="utf-8"))
ASSET_DIR = Path(CFG["asset_staging_dir"])
RELEASE_URL = f"https://github.com/{CFG['github_repo']}/releases/download/{CFG['asset_release_tag']}/"
BIG = 1 << 20  # 1 MiB
UA = {"User-Agent": "harvy-airships-packtools/1.0", "Content-Type": "application/json"}

SKIP_RE = re.compile(r"(\.bak(-.*)?|\.bak\d*|\.orig\d*(-.*)?|\.old|_old|\.log|\.tmp|~|\.disabled)$", re.I)


def toml_str(s: str) -> str:
    return json.dumps(s, ensure_ascii=False)


def sha(path: Path, algo: str) -> str:
    h = hashlib.new(algo)
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def asset_name(sha1: str, filename: str) -> str:
    return sha1[:10] + "-" + re.sub(r"[^A-Za-z0-9._+-]", "_", filename)


def collect(src: Path):
    """Yield (relative posix path, absolute path, preserve) for every file to ship."""
    excluded = {e.lower() for e in CFG["exclude_files"]}
    for d in CFG["include_dirs"]:
        root = src / d
        if not root.is_dir():
            continue
        for p in sorted(root.rglob("*")):
            if not p.is_file():
                continue
            rel = p.relative_to(src).as_posix()
            if SKIP_RE.search(p.name) or rel.lower() in excluded or p.name in (".gitignore", ".gitattributes"):
                continue
            if any(rel.startswith(x) for x in CFG["exclude_prefixes"]):
                continue
            if d == "mods" and (p.suffix.lower() != ".jar" or p.parent != root):
                continue
            yield rel, p, False
    for f in CFG["preserve_files"]:
        p = src / f
        if p.is_file():
            yield f, p, True


def modrinth_lookup(sha1s):
    out = {}
    sha1s = list(sha1s)
    for i in range(0, len(sha1s), 500):
        req = urllib.request.Request("https://api.modrinth.com/v2/version_files", headers=UA,
                                     data=json.dumps({"hashes": sha1s[i:i + 500], "algorithm": "sha1"}).encode())
        out.update(json.load(urllib.request.urlopen(req, timeout=60)))
    ids = sorted({v["project_id"] for v in out.values()})
    titles = {}
    for i in range(0, len(ids), 200):
        q = urllib.parse.quote(json.dumps(ids[i:i + 200]))
        req = urllib.request.Request(f"https://api.modrinth.com/v2/projects?ids={q}", headers=UA)
        for p in json.load(urllib.request.urlopen(req, timeout=60)):
            titles[p["id"]] = p["title"]
    return out, titles


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", default=os.path.expandvars(CFG["technic_pack_dir"]))
    ap.add_argument("--version", help="pack version (default: YYYY.MM.DD-N, auto-incremented)")
    a = ap.parse_args()
    src = Path(a.source)
    if not (src / "mods").is_dir():
        sys.exit(f"not a pack dir: {src}")

    files = list(collect(src))
    big = [(r, p) for r, p, _ in files if p.stat().st_size >= BIG or r.startswith("mods/")]
    print(f"{len(files)} files, {len(big)} as metafiles; hashing...")
    hashes = {r: (sha(p, "sha1"), sha(p, "sha512")) for r, p in big}
    mr, titles = modrinth_lookup({h[0] for h in hashes.values()})

    # fresh pack dir (keep nothing stale)
    if PACK.exists():
        shutil.rmtree(PACK)
    PACK.mkdir(parents=True)
    ASSET_DIR.mkdir(parents=True, exist_ok=True)

    index, report = [], {"modrinth": 0, "release": 0, "inline": 0, "release_assets": []}
    for rel, p, preserve in files:
        if rel in hashes:
            sha1, sha512 = hashes[rel]
            meta_rel = rel[: -len(p.suffix)] + ".pw.toml" if p.suffix else rel + ".pw.toml"
            v = mr.get(sha1)
            lines = []
            if v:
                fobj = next(f for f in v["files"] if f["hashes"]["sha1"] == sha1)
                name = titles.get(v["project_id"], p.stem)
                lines += [f"name = {toml_str(name)}", f"filename = {toml_str(p.name)}", 'side = "both"', "",
                          "[download]", f"url = {toml_str(fobj['url'])}", 'hash-format = "sha512"',
                          f"hash = {toml_str(sha512)}", "", "[update]", "[update.modrinth]",
                          f"mod-id = {toml_str(v['project_id'])}", f"version = {toml_str(v['id'])}"]
                report["modrinth"] += 1
            else:
                an = asset_name(sha1, p.name)
                staged = ASSET_DIR / an
                if not staged.exists():
                    shutil.copy2(p, staged)
                lines += [f"name = {toml_str(p.stem)}", f"filename = {toml_str(p.name)}", 'side = "both"', "",
                          "[download]", f"url = {toml_str(RELEASE_URL + an)}", 'hash-format = "sha512"',
                          f"hash = {toml_str(sha512)}"]
                report["release"] += 1
                report["release_assets"].append(an)
            out = PACK / meta_rel
            out.parent.mkdir(parents=True, exist_ok=True)
            out.write_bytes(("\n".join(lines) + "\n").encode("utf-8"))
            index.append((meta_rel, sha(out, "sha256"), True, preserve))
        else:
            out = PACK / rel
            out.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(p, out)
            index.append((rel, sha(out, "sha256"), False, preserve))
            report["inline"] += 1

    idx = ['hash-format = "sha256"', ""]
    for rel, h, meta, preserve in sorted(index):
        idx += ["[[files]]", f"file = {toml_str(rel)}", f"hash = {toml_str(h)}"]
        if meta:
            idx.append("metafile = true")
        if preserve:
            idx.append("preserve = true")
        idx.append("")
    (PACK / "index.toml").write_bytes("\n".join(idx).encode("utf-8"))

    # version: YYYY.MM.DD-N, bumped if pack content changed vs the committed one
    state_f = REPO / "tools" / "last_build.json"
    state = json.loads(state_f.read_text()) if state_f.exists() else {}
    idx_hash = sha(PACK / "index.toml", "sha256")
    if a.version:
        ver = a.version
    elif state.get("index_hash") == idx_hash:
        ver = state["version"]
    else:
        today = date.today().strftime("%Y.%m.%d")
        n = int(state["version"].split("-")[1]) + 1 if state.get("version", "").startswith(today) else 1
        ver = f"{today}-{n}"

    nf = json.loads((src / "bin" / "version.json").read_text(encoding="utf-8"))["id"].split("-")[-1]
    pack_toml = "\n".join([
        f"name = {toml_str(CFG['pack_name'])}", f"author = {toml_str(CFG['author'])}",
        f"version = {toml_str(ver)}", 'pack-format = "packwiz:1.1.0"', "",
        "[index]", 'file = "index.toml"', 'hash-format = "sha256"', f"hash = {toml_str(idx_hash)}", "",
        "[versions]", f'minecraft = "{CFG["minecraft"]}"', f"neoforge = {toml_str(nf)}", ""])
    (PACK / "pack.toml").write_bytes(pack_toml.encode("utf-8"))
    state_f.write_text(json.dumps({"version": ver, "index_hash": idx_hash, "neoforge": nf,
                                   "release_assets": sorted(report["release_assets"])}, indent=1))
    print(f"pack {ver} (NeoForge {nf}): {report['modrinth']} from Modrinth, {report['release']} from release, "
          f"{report['inline']} inline files")


if __name__ == "__main__":
    import urllib.parse
    main()
