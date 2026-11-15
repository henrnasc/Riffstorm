"""
Fetch album covers for the songs in the local library from MusicBrainz + Cover Art Archive (public APIs).

usage: python fetch_covers.py <library folder> ../../assets/covers
       (library default: %LOCALAPPDATA%\\Riffstorm\\biblioteca)

Reads <library>/<game>/<song>/song.json and writes <covers>/<game>/<song>.jpg for every song found
(convert to 512x512 PNG before shipping). Songs without a cover get a generated one in the game.
Artwork from the games' own soundtrack releases is never used.
"""
import glob, json, os, sys, time, urllib.error, urllib.parse, urllib.request

UA = {"User-Agent": "Riffstorm/1.0 (personal rhythm game; album art lookup)"}
_last = [0.0]

# songs whose best match is the original studio album (searched by album title), per game
ALBUMS = {
    "gh1": {
        "iloverockandroll": ("Joan Jett & the Blackhearts", "I Love Rock 'n' Roll"),
        "crossroads": ("Cream", "Wheels of Fire"),
        "frankenstein": ("The Edgar Winter Group", "They Only Come Out at Night"),
        "spanishcastlemagic": ("The Jimi Hendrix Experience", "Axis: Bold as Love"),
        "morethanafeeling": ("Boston", "Boston"),
        "higherground": ("Red Hot Chili Peppers", "Mother's Milk"),
        "texasflood": ("Stevie Ray Vaughan and Double Trouble", "Texas Flood"),
        "aceofspades": ("Motörhead", "Ace of Spades"),
        "godzilla": ("Blue Öyster Cult", "Spectres"),
    },
    "gh2": {
        "tonightimgonna": ("Spinal Tap", "This Is Spinal Tap"),
        "whowasinmyroom": ("Butthole Surfers", "Independent Worm Saloon"),
        "cantyouhearme": ("The Rolling Stones", "Sticky Fingers"),
        "surrender": ("Cheap Trick", "Heaven Tonight"),
        "sweetchild": ("Guns N' Roses", "Appetite for Destruction"),
        "youreallygotme": ("Van Halen", "Van Halen"),
        "searchanddestroy": ("The Stooges", "Raw Power"),
    },
    "gh80s": {
        "aintnothinbut": ("Poison", "Open Up and Say... Ahh!"),
        "wegotthebeat": ("The Go-Go's", "Beauty and the Beat"),
    },
    "gh3": {
        "holidayincambodia": ("Dead Kennedys", "Fresh Fruit for Rotting Vegetables"),
        "koolthing": ("Sonic Youth", "Goo"),
        "ruby": ("Kaiser Chiefs", "Yours Truly, Angry Mob"),
        "bossdevil": ("The Charlie Daniels Band", "Million Mile Reflections"),
        "blackmagicwoman": ("Santana", "Abraxas"),
        "cherubrock": ("Smashing Pumpkins", "Siamese Dream"),
        "evenflow": ("Pearl Jam", "Ten"),
        "hierkommtalex": ("Die Toten Hosen", "Ein kleines bisschen Horrorschau"),
        "hitmewithyourbestshot": ("Pat Benatar", "Crimes of Passion"),
        "pridenjoy": ("Stevie Ray Vaughan and Double Trouble", "Texas Flood"),
        "rocknrollallnite": ("Kiss", "Dressed to Kill"),
        "schoolsout": ("Alice Cooper", "School's Out"),
        "numberofthebeast": ("Iron Maiden", "The Number of the Beast"),
    },
}
ALBUMS["gha"] = {
    "alltheyoungdudes": ("Mott the Hoople", "All the Young Dudes"), "alwaysontherun": ("Lenny Kravitz", "Mama Said"),
    "completecontrol": ("The Clash", "The Clash"), "drawtheline": ("Aerosmith", "Draw the Line"), "dreamon": ("Aerosmith", "Aerosmith"),
    "dreampolice": ("Cheap Trick", "Dream Police"), "ihatemyselfforlovingyou": ("Joan Jett and the Blackhearts", "Up Your Alley"),
    "kingofrock": ("Run-D.M.C.", "King of Rock"), "livinontheedge": ("Aerosmith", "Get a Grip"), "loveinanelevator": ("Aerosmith", "Pump"),
    "mamakin": ("Aerosmith", "Aerosmith"), "ragdoll": ("Aerosmith", "Permanent Vacation"), "sweetemotion": ("Aerosmith", "Toys in the Attic"),
    "trainkeptarollin": ("Aerosmith", "Get Your Wings"), "walkthiswayrundmc": ("Run-D.M.C.", "Raising Hell"),
}
ALBUMS["ghm"] = {
    "aceofspades": ("Motörhead", "Ace of Spades"), "amievil": ("Diamond Head", "Lightning to the Nations"),
    "battery": ("Metallica", "Master of Puppets"), "creepingdeath": ("Metallica", "Ride the Lightning"),
    "dyerseve": ("Metallica", "...And Justice for All"), "entersandman": ("Metallica", "Metallica"),
    "fadetoblack": ("Metallica", "Ride the Lightning"), "forwhomthebelltolls": ("Metallica", "Ride the Lightning"),
    "frantic": ("Metallica", "St. Anger"), "fuel": ("Metallica", "Reload"), "hellbentforleather": ("Judas Priest", "Hell Bent for Leather"),
    "hitthelights": ("Metallica", "Kill 'Em All"), "masterofpuppets": ("Metallica", "Master of Puppets"),
    "noleafclover": ("Metallica", "S&M"), "nothingelsematters": ("Metallica", "Metallica"), "one": ("Metallica", "...And Justice for All"),
    "orion": ("Metallica", "Master of Puppets"), "seekanddestroy": ("Metallica", "Kill 'Em All"),
    "stackedactors": ("Foo Fighters", "There Is Nothing Left to Lose"), "thememoryremains": ("Metallica", "Reload"),
    "theunforgiven": ("Metallica", "Metallica"), "warensemble": ("Slayer", "Show No Mercy"), "whiplash": ("Metallica", "Kill 'Em All"),
    "disposeableheroes": ("Metallica", "Master of Puppets"), "mommyslittlemonster": ("Social Distortion", "Mommy's Little Monster"),
    "turnthepage": ("Metallica", "Garage Inc."), "sadbuttrue": ("Metallica", "Metallica"), "whereverimayroam": ("Metallica", "Metallica"),
}
TITLE_FIX ={"You Got Another Thing Comin'": "You've Got Another Thing Comin'"}
ARTIST_FIX = {"The Ramones": "Ramones", "Sum41": "Sum 41"}
BLOCK = ("guitar hero",)


def get(url, raw=False):
    for attempt in range(5):
        wait = 1.6 - (time.time() - _last[0])
        if wait > 0: time.sleep(wait)
        _last[0] = time.time()
        try:
            req = urllib.request.Request(url, headers=UA)
            with urllib.request.urlopen(req, timeout=30) as r:
                data = r.read()
            return data if raw else json.loads(data)
        except urllib.error.HTTPError as e:
            if e.code in (502, 503) and attempt < 4:
                time.sleep(3 * (attempt + 1)); continue
            raise


def album_candidates(artist, album):
    q = f'releasegroup:"{album}" AND artist:"{artist}" AND primarytype:album'
    js = get("https://musicbrainz.org/ws/2/release-group/?fmt=json&limit=10&query=" + urllib.parse.quote(q))
    return [rg["id"] for rg in js.get("release-groups", [])
            if int(rg.get("score", 0)) >= 80 and not rg.get("secondary-types") and not any(b in rg.get("title", "").lower() for b in BLOCK)]


def candidates(name, artist):
    name = TITLE_FIX.get(name, name)
    artist = ARTIST_FIX.get(artist, artist)
    q = f'recording:"{name}" AND artist:"{artist}"'
    js = get("https://musicbrainz.org/ws/2/recording/?fmt=json&limit=25&query=" + urllib.parse.quote(q))
    groups = {}
    for rec in js.get("recordings", []):
        if int(rec.get("score", 0)) < 80: continue
        for rel in rec.get("releases", []):
            rg = rel.get("release-group", {})
            if not rg.get("id") or any(b in (rg.get("title") or "").lower() for b in BLOCK): continue
            ptype = rg.get("primary-type") or ""
            sec = rg.get("secondary-types") or []
            rank = (0 if ptype == "Album" and not sec else 1 if ptype == "Single" else 2 if ptype == "Album" else 3, rel.get("date") or "9999")
            if rg["id"] not in groups or rank < groups[rg["id"]]:
                groups[rg["id"]] = rank
    return [g for g, _ in sorted(groups.items(), key=lambda kv: kv[1])]


def main():
    lib = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.environ["LOCALAPPDATA"], "Riffstorm", "biblioteca")
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "..", "..", "assets", "covers")
    missing = []
    for sj in sorted(glob.glob(os.path.join(lib, "*", "*", "song.json"))):
        s = json.load(open(sj, encoding="utf-8"))
        game = os.path.basename(os.path.dirname(os.path.dirname(sj)))
        gdir = os.path.join(out, game); os.makedirs(gdir, exist_ok=True)
        dst = os.path.join(gdir, s["id"] + ".jpg")
        if os.path.exists(dst) or os.path.exists(os.path.join(gdir, s["id"] + ".png")):
            continue
        ok = False
        try:
            alb = ALBUMS.get(game, {}).get(s["id"])
            for rg in (album_candidates(*alb) if alb else candidates(s["name"], s["artist"]))[:6]:
                try:
                    img = get(f"https://coverartarchive.org/release-group/{rg}/front-500", raw=True); open(dst, "wb").write(img); ok = True; break
                except Exception:
                    continue
        except Exception as e:
            print("  erro", s["id"], e)
        print(("ok   " if ok else "sem  ") + game + "/" + s["id"], "-", s["name"], "/", s["artist"])
        if not ok: missing.append(game + "/" + s["id"])
    print("sem capa:", missing)


if __name__ == "__main__":
    main()
