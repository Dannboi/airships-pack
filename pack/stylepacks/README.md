# StylishMod — Style Packs

Drop a **style pack** into this `stylepacks/` folder and StylishMod loads it
automatically — no need to enable anything in the Resource Packs or Data Packs
menus. A pack placed here is applied to **both** the client (models/textures)
and the server/world (the style catalog), so its styles "just work".

> A style pack can be either a **folder** or a **`.zip`**, exactly like a normal
> resource/data pack. To disable a pack, remove it from this folder.

---

## What a style is

A *style* is a named cosmetic skin for one or more items. The item stays the
same item mechanically — only its look changes. Each style has:

- a **style id** = `<namespace>:<name>` (e.g. `mypack:royal`),
- a **display name** (shown in tooltips and at the Style Bench),
- a list of **items it applies to**,
- the **models/textures** it provides for those items.

The style id is the contract: the client-side manifest and the server-side
catalog file just have to use the **same id**.

---

## Pack layout

```
<your pack>/
  pack.mcmeta
  assets/<namespace>/
    stylish_styles/<name>.json          <- CLIENT manifest (what to render)
    models/item/.../<item>.json         <- variant item models (held/inventory)
    textures/...                        <- any textures your models/armor/shields use
    lang/en_us.json                     <- the style's display name
  data/<namespace>/
    stylish_style/<name>.json           <- SERVER catalog (name + which items + weight)
```

`<namespace>` is yours (e.g. `mypack`); `<name>` is the style (e.g. `royal`).
The resulting style id is `<namespace>:<name>`.

### `pack.mcmeta`
Standard pack metadata. For Minecraft 1.21.1 use `pack_format` 34 (resources)
/ 48 (data); listing both via `supported_formats` is fine:

```json
{ "pack": { "description": "My style pack", "pack_format": 34, "supported_formats": [34, 48] } }
```

### 1. Server catalog — `data/<ns>/stylish_style/<name>.json`
Declares the style's name, how likely it is for random assignment, and which
items it can be applied to. `applies_to` accepts item ids and `#item_tags`.

```json
{
  "weight": 1,
  "name_key": "style.mypack.royal",
  "applies_to": [
    "minecraft:diamond_sword",
    "minecraft:diamond_chestplate",
    "#minecraft:swords"
  ]
}
```

### 2. Display name — `assets/<ns>/lang/en_us.json`
This is how the style is **named** in tooltips ("Diamond Sword (Royal)") and at
the Style Bench. The key must match `name_key` above.

```json
{ "style.mypack.royal": "Royal" }
```

### 3. Client manifest — `assets/<ns>/stylish_styles/<name>.json`
Maps each item to what the style provides. Three optional sections:

- **`items`** — the inventory / held / dropped model (any item). Value is a
  *model* id resolving to `assets/<ns>/models/<that path>.json`.
- **`armor`** — the **worn** armor texture base. The mod loads
  `assets/<ns>/textures/models/armor/<base>_layer_1.png` (and `_layer_2.png`
  for leggings). You may point at vanilla bases like `minecraft:gold`. Leather
  also renders an undyed `<base>_layer_1_overlay.png` (and `_layer_2_overlay.png`)
  pass; if your style doesn't ship one, that pass falls back to the vanilla
  overlay instead of showing a missing (purple/black) texture.
- **`shields`** — a 64×64 shield texture base (same layout as
  `entity/shield/base`). Resolves to `assets/<ns>/textures/<base>.png`.

```json
{
  "items": {
    "minecraft:diamond_sword": "mypack:item/royal/diamond_sword",
    "minecraft:diamond_chestplate": "mypack:item/royal/diamond_chestplate"
  },
  "armor": {
    "minecraft:diamond_chestplate": "mypack:royal/diamond"
  },
  "shields": {
    "minecraft:shield": "mypack:entity/shield/royal"
  }
}
```

### 4. A variant item model — `assets/<ns>/models/item/royal/diamond_sword.json`
Just a normal item model. The quickest way to reskin is to point at another
texture (even a vanilla one):

```json
{ "parent": "minecraft:item/handheld", "textures": { "layer0": "minecraft:item/golden_sword" } }
```

---

## Converting an existing resource pack into a style pack

A normal resource pack *replaces* vanilla textures, so it changes every matching
item. To make a pack's look a **selectable style** instead (one variant among
others), its textures must live under a **new namespace** so they don't overwrite
the originals. The recipe:

1. Pick a namespace for the style, e.g. `mypack`. The style id becomes
   `mypack:mypack` (folder name `stylish_styles/mypack.json`).
2. For each item you want to restyle, copy the pack's texture out of
   `assets/minecraft/textures/item/<item>.png` to
   `assets/mypack/textures/item/mypack/<item>.png` (a path under *your* namespace).
3. Make a variant model `assets/mypack/models/item/mypack/<item>.json` whose
   `layer0` points at that relocated texture (`item/handheld` for tools/weapons,
   `item/generated` for everything else; bows also need the `pulling` overrides).
4. For worn armor, copy `textures/models/armor/<material>_layer_1.png` (and `_2`)
   to `assets/mypack/textures/models/armor/mypack/<material>_layer_1.png` and use
   `<material>` as the `armor` base.
5. Write the manifest (`stylish_styles/mypack.json`), the catalog
   (`data/mypack/stylish_style/mypack.json`), and the name (`lang/en_us.json`),
   as described above.
6. Drop the folder in `stylepacks/`. The pack now adds a `mypack` style; vanilla
   and other packs are untouched.

**Converters (in the mod's source repo, under `tools/`):**

- **GUI — `StyleStudio.java`** (recommended). No build step:
  ```
  java tools/StyleStudio.java
  ```
  Pick a source pack, name the style, and tick which items to convert. It scans
  **every namespace** in the pack — vanilla *and* modded (e.g. `twilightforest:`) —
  shows a live thumbnail of each item's texture (with an enlarged preview) and a
  Filter box for large packs, then outputs the ready-to-use `<namespace>_pack`.
  Modded items work with no code changes; only the converter needed to learn to
  look beyond the `minecraft` namespace.

- **CLI** — the same file runs headless when given arguments, or use the Bash script:
  ```
  java tools/StyleStudio.java <source.zip|dir> <namespace> "<Display Name>" [output_dir]
  tools/resourcepack-to-stylepack.sh <source.zip|dir> <namespace> "<Display Name>" [output_dir] [item ...]
  ```

Both relocate each item's texture under the new namespace, infer the model type,
handle bow pull-states and armor layers (incl. the `golden`→`gold` layer name),
and fall back to the vanilla texture for anything the source pack doesn't include.

**Importing OptiFine / CIT packs:** packs that ship `optifine/cit` (or `citresewn/cit`)
item variants can be imported directly — StyleStudio's **"Import CIT pack…"** button
(or `java tools/StyleStudio.java --cit <pack> <namespace> [out]`) reads the
`.properties` files and turns each `nbt.display.Name` variant into a style, merging
entries that share a name across items (so all the "*royal*" pieces become one
`royal` style). It imports custom `model=` files too (copying the Blockbench model
and rewriting its `./` texture refs), so unique 3D weapon shapes survive; CIT
shields and tridents route to the shield/trident renderers, and `type=elytra` entries
become styled **worn elytra wings** (with the elytra item icon coming through the normal
item path). Only entries with a custom texture/model are imported; NBT-functional CITs
that reuse vanilla art, plus enchantment entries, are skipped. (CIT `damage=` durability
variants aren't imported yet.)

**Import everything:** **"Import everything…"** (or `--all <pack> <namespace> "<name>" [out]`)
does the CIT import *and* adds one `base` style from the pack's ordinary
`textures/item` sprites — handy for a pack that has both CIT variants and a regular
retexture layer (e.g. an elytra-CIT pack that also reskins items). For the broadest
result, use this.

**What the converter handles** (out of the box):
- Flat item sprites (tools, weapons, generic items) — any namespace, vanilla or modded.
- **Custom 3D models** (Blockbench `elements`) — copied with their textures, and any
  **referenced models are followed recursively** (e.g. a crossbow → its musket/flintlock
  loading/primed/firework state models), so unique shapes and multi-state items survive.
- **Global custom item models** that have no item-sprite (found by scanning `models/item/`).
- **Bows & crossbows** with pull/charge states; **armor** (worn layers + icons); **leather**
  with dye-preserving overlay; **custom-geometry shields** (rendered as their real shape) and
  vanilla-shape shields + **tridents** (via the shield/trident renderers).
- **Elytra** — both the inventory icon and the **worn wings** (OptiFine `type=elytra`), so a
  styled elytra no longer falls back to the default wings in flight.
- Skips `builtin/entity`-only and NBT-functional CITs that just reuse vanilla art.

## Using styles on a multiplayer server

- **The mod** must be installed on the **server and every client** (NeoForge mods
  aren't auto-downloaded on join).
- **The style catalog** (which styles exist, names, which items) **syncs
  automatically** to clients on join, and each item's chosen style travels in its
  synced data component — so everyone agrees on what style an item *is*.
- **The textures/models** are not pushed by the mod. A client renders a style only
  if it has that style pack's assets locally. Two ways to get them there:
  1. **Pre-load** the style pack(s) on clients (drop in their `stylepacks/`, which
     auto-enables them). Clients then render every style the server uses. Best for
     curated servers / modpacks.
  2. **Server resource pack** — bundle the styles' assets into one zip and host it,
     then in `server.properties`:
     ```
     resource-pack=<URL to the zip>
     resource-pack-sha1=<sha1 printed by the exporter>
     require-resource-pack=true
     ```
     StyleStudio builds this zip for you (and prints the SHA-1 + these lines):
     ```
     java tools/StyleStudio.java --server-pack <your stylepacks folder> stylish-server-pack.zip
     ```
     or use the **"Export server pack…"** button in the GUI. Note: the zip must be
     hosted at a URL — the Minecraft server hands clients a URL to download, it does
     not serve the file itself.

## Tips

- An item can appear in `items` **and** `armor`/`shields` at once — that styles
  the inventory icon *and* the in-world look.
- If a player doesn't have your pack, styled items just fall back to their
  normal vanilla look — nothing breaks.
- Multiple packs can add styles for the same item; each adds its own option.
- Bundled example styles (`stylish:royal`, `stylish:elven`) live inside the mod
  and show every section in action — a good reference.
- Reload in-game with `F3+T` (client assets) and `/reload` (server catalog).
