"""
Builds assets/covers.json: song key ("gh1/ironman") -> MusicBrainz release-group id whose Cover Art Archive front image
is the cover the game shows. The game downloads the images itself at runtime, so no album art is stored in the repo.

usage: python map_covers.py [library] [reviewed covers folder]
For every song that has a reviewed local cover (<covers>/<game>/<id>.png), the same lookup as fetch_covers.py is
repeated and the release group whose image matches the reviewed one is kept.
"""
import glob, io, json, os, sys
from PIL import Image
import fetch_covers as fc

HERE = os.path.dirname(os.path.abspath(__file__))


def sig(img):
    return list(img.convert("L").resize((16, 16)).getdata())


def diff(a, b):
    return sum(abs(x - y) for x, y in zip(a, b)) / len(a)


def main():
    lib = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ["LOCALAPPDATA"], "Riffstorm", "biblioteca")
    covers = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "assets", "covers")
    out_path = os.path.join(HERE, "..", "..", "assets", "covers.json")
    result = json.load(open(out_path, encoding="utf-8-sig")) if os.path.exists(out_path) else {}
    unmatched = []
    for sj in sorted(glob.glob(os.path.join(lib, "*", "*", "song.json"))):
        s = json.load(open(sj, encoding="utf-8"))
        game = os.path.basename(os.path.dirname(os.path.dirname(sj)))
        key = game + "/" + s["id"]
        local = os.path.join(covers, game, s["id"] + ".png")
        if key in result or not os.path.exists(local):
            continue
        want = sig(Image.open(local))
        best = None
        try:
            alb = fc.ALBUMS.get(game, {}).get(s["id"])
            for rg in (fc.album_candidates(*alb) if alb else fc.candidates(s["name"], s["artist"]))[:6]:
                try:
                    img = Image.open(io.BytesIO(fc.get(f"https://coverartarchive.org/release-group/{rg}/front-250", raw=True)))
                except Exception:
                    continue
                d = diff(sig(img), want)
                if best is None or d < best[1]: best = (rg, d)
                if d < 12: break
        except Exception as e:
            print("  erro", key, e)
        if best and best[1] < 20:
            result[key] = best[0]
            print(f"ok   {key} {best[0]} d={best[1]:.1f}", flush=True)
            json.dump(result, open(out_path, "w", encoding="utf-8"), indent=1, sort_keys=True)
        else:
            unmatched.append(key)
            print(f"sem  {key} {best}", flush=True)
    print("sem correspondencia:", unmatched)


if __name__ == "__main__":
    main()
