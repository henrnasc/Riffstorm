"""
Adds to assets/covers.json the songs whose cover is taken straight from a known studio album
(used when map_covers.py finds no release group matching the reviewed image).
usage: python map_albums.py   (writes previews to ./preview/<game>_<id>.jpg for a visual check)
"""
import json, os
import fetch_covers as fc

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "assets", "covers.json")

ALBUMS = {
    "gh1/cowboysfromhell": ("Pantera", "Cowboys From Hell"),
    "gh1/takemeout": ("Franz Ferdinand", "Franz Ferdinand"),
    "gh1/yougotanotherthingcomin": ("Judas Priest", "Screaming for Vengeance"),
    "gh2/freebird": ("Lynyrd Skynyrd", "(Pronounced 'Lĕh-'nérd 'Skin-'nérd)"),
    "gh2/hangar18": ("Megadeth", "Rust in Peace"),
    "gh2/heartshapedbox": ("Nirvana", "In Utero"),
    "gh2/messageinabottle": ("The Police", "Reggatta de Blanc"),
    "gh2/monkeywrench": ("Foo Fighters", "The Colour and the Shape"),
    "gh2/stop": ("Jane's Addiction", "Ritual de lo habitual"),
    "gh2/thembones": ("Alice in Chains", "Dirt"),
    "gh3/bullsonparade": ("Rage Against the Machine", "Evil Empire"),
    "gh3/lagrange": ("ZZ Top", "Tres Hombres"),
    "gh3/one": ("Metallica", "...And Justice for All"),
    "gh3/paintitblack": ("The Rolling Stones", "Aftermath"),
    "gh3/rainingblood": ("Slayer", "Reign in Blood"),
    "gh3/slowride": ("Foghat", "Fool for the City"),
    "gh3/welcometothejungle": ("Guns N' Roses", "Appetite for Destruction"),
    "gh80s/heatofthemoment": ("Asia", "Asia"),          # check: also matches the Romanian band A.S.I.A. -> a29d9f47-7f6e-3bad-afe7-ad1a55ddf37c
    "gh80s/noonelikeyou": ("Scorpions", "Blackout"),
}


def main():
    res = json.load(open(OUT, encoding="utf-8"))
    os.makedirs(os.path.join(HERE, "preview"), exist_ok=True)
    for key, (artist, album) in ALBUMS.items():
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
