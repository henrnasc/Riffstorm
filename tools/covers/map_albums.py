"""
Adds to assets/covers.json the songs whose cover is taken straight from a known studio album
(used when map_covers.py finds no release group matching the reviewed image).
usage: python map_albums.py   (writes previews to ./preview/<game>_<id>.jpg for a visual check)
"""
import json, os, sys
import fetch_covers as fc

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "assets", "covers.json")

ALBUMS = {
    # GH: Aerosmith / GH: Metallica songs whose reviewed cover had no matching release group
    "gha/catscratchfever": ("Ted Nugent", "Cat Scratch Fever"),
    "gha/completecontrol": ("The Clash", "The Clash"),
    "gha/walkthisway": ("Aerosmith", "Toys in the Attic"),
    "ghm/armedandready": ("Michael Schenker Group", "The Michael Schenker Group"),
    "ghm/fightfirewithfire": ("Metallica", "Ride the Lightning"),
    "ghm/sanitarium": ("Metallica", "Master of Puppets"),
    "ghm/stonecoldcrazy": ("Queen", "Sheer Heart Attack"),
    "ghm/theboysareback": ("Thin Lizzy", "Jailbreak"),}


def main():
    res = json.load(open(OUT, encoding="utf-8-sig"))
    os.makedirs(os.path.join(HERE, "preview"), exist_ok=True)
    for key, (artist, album) in ALBUMS.items():
        if key in res and "--force" not in sys.argv: continue
        ok = False
        for rg in fc.album_candidates(artist, album)[:5]:
            try:
                img = fc.get(f"https://coverartarchive.org/release-group/{rg}/front-250", raw=True)
            except Exception:
                continue
            open(os.path.join(HERE, "preview", key.replace("/", "_") + ".jpg"), "wb").write(img)
            res[key] = rg; ok = True
            break
        print(("ok   " if ok else "sem  ") + key, flush=True)
        json.dump(res, open(OUT, "w", encoding="utf-8"), indent=1, sort_keys=True)


if __name__ == "__main__":
    main()
