"""Builds sanitised message fixtures (tests/fixtures/messages) from local Fourvale.Capture files.

Run locally:  python tools/scripts/make_message_fixtures.py
Only the output is committed; captures stay in %LOCALAPPDATA%. Names become PlayerNN, ids <ID_n>,
battle tickets <TICKET>. The script stops with a non-empty LEAKS list if anything real survives;
tests/Fourvale.Adapter.Tests/FixtureHygieneTests.cs checks the committed files again.
Update the capture file pattern below when regenerating from newer captures.
"""
import json, os, re, glob

CAP = os.path.expandvars(r"%LOCALAPPDATA%\FourvaleMultibox\captures")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tests", "fixtures", "messages")
os.makedirs(OUT, exist_ok=True)

files = sorted(glob.glob(os.path.join(CAP, "2026-10-01_15-07-51_slot*.jsonl")))
records = [json.loads(l) for f in files for l in open(f, encoding="utf-8")]

# --- Collect every player-ish name we can see, so all of them get replaced. ---
names = set()
def collect(node):
    if isinstance(node, dict):
        for k, v in node.items():
            if k in ("name", "username", "from", "leader", "sender") and isinstance(v, str):
                names.add(v)
            collect(v)
    elif isinstance(node, list):
        for v in node:
            collect(v)
for r in records:
    if r.get("kind") == "http" and r["url"].split("/api/")[-1] in ("me", "friends", "guild", "profile"):
        collect(r.get("body"))
    if r.get("type") in ("gldEvt", "frEvt"):
        collect(r.get("message"))
for r in records:  # names in battle log lines: "<name> (<Class>) joined the battle!"
    if r.get("type") == "log":
        m = re.match(r"^(\S+) \(", r["message"]["text"])
        if m:
            names.add(m.group(1))
names.discard("<REDACTED>")
# Guild names are player-chosen too.
guilds = {r["message"]["roster"]["name"] for r in records if r.get("type") == "gldEvt" and "roster" in r.get("message", {})}

name_map = {n: f"Player{i + 1:02d}" for i, n in enumerate(sorted(names, key=lambda s: (-len(s), s)))}
guild_map = {g: f"Guild{i + 1:02d}" for i, g in enumerate(sorted(guilds))}
id_map = {}
ID = re.compile(r"\b[A-Za-z0-9_-]{9}\b")

def placeholder_id(match):
    v = match.group(0)
    if v.isalpha() and v.islower():
        return v  # ordinary words such as "proximity"
    if v not in id_map:
        id_map[v] = f"<ID_{len(id_map) + 1}>"
    return id_map[v]

def clean_str(s, key):
    if key in ("ticket", "battleTicket", "battleKey"):
        return "<TICKET>"
    for real, fake in {**guild_map, **name_map}.items():
        s = re.sub(rf"(?<![A-Za-z0-9]){re.escape(real)}(?![A-Za-z0-9])", lambda _m, f=fake: f, s)
    if key in ("sessionId", "roomId", "processId", "sourceId", "targetId", "senderId", "id") or key.endswith("Id") and ID.fullmatch(s):
        s = ID.sub(placeholder_id, s)
    return s

def clean(node, key=""):
    if node == {"$ext": 0, "data": "AA=="}:
        return None  # msgpackr `undefined`, decoded as null since the capture tool fix (discovery C8)
    if isinstance(node, dict):
        return {k: clean(v, k) for k, v in node.items()}
    if isinstance(node, list):
        return [clean(v, key) for v in node]
    if isinstance(node, str):
        return clean_str(node, key)
    return node

def first(pred):
    return next(r for r in records if pred(r))

build = "client 0.98 Beta, bundle main-DZfOvN-x.js"
picks = {
    "matchmake-town": first(lambda r: r.get("kind") == "http" and "/matchmake/joinOrCreate/town" in r["url"]),
    "matchmake-battle": first(lambda r: r.get("kind") == "http" and "/matchmake/joinOrCreate/battle" in r["url"]),
    "hpSync": first(lambda r: r.get("type") == "hpSync"),
    "startBattle": first(lambda r: r.get("type") == "startBattle"),
    "damage-hit": first(lambda r: r.get("type") == "damage" and r["message"].get("kind") == "damage"),
    "battleOver": first(lambda r: r.get("type") == "battleOver"),
    "teleport": first(lambda r: r.get("type") == "teleport"),
    "consumables": first(lambda r: r.get("type") == "consumables"),
    "log-joined": first(lambda r: r.get("type") == "log" and "joined" in r["message"]["text"]),
}
heal = [r for f in sorted(glob.glob(os.path.join(CAP, "2026-10-01_14-15-23_slot1.jsonl"))) for l in open(f, encoding="utf-8")
        for r in [json.loads(l)] if r.get("type") == "damage" and r["message"].get("kind") == "heal"]
if heal:
    picks["damage-heal"] = heal[0]

for name, rec in picks.items():
    payload = rec.get("message") if rec.get("kind") == "ws" else rec.get("body")
    fixture = {
        "_fixture": {
            "captured": "2026-10-01",
            "fourvale": build,
            "source": "Fourvale.Capture, redacted at capture time, then sanitised (names -> PlayerNN, ids -> <ID_n>, tickets -> <TICKET>)",
            "kind": rec.get("kind"),
            "protocol": rec.get("protocol"),
            "type": rec.get("type"),
            "url": re.sub(r"https://api\.fourvale\.com", "https://api.fourvale.com", rec.get("url", "")) or None,
        },
        "payload": clean(payload),
    }
    with open(os.path.join(OUT, f"{name}.json"), "w", encoding="utf-8") as fh:
        json.dump(fixture, fh, indent=2, ensure_ascii=False)
        fh.write("\n")

# --- Verify: no real name, guild or id survives anywhere in the output. ---
text = "".join(open(os.path.join(OUT, f), encoding="utf-8").read() for f in os.listdir(OUT))
leaks = [n for n in list(names) + list(guilds) + list(id_map) if n and n in text]
print("fixtures:", sorted(os.listdir(OUT)))
print("names replaced:", len(name_map), "guilds:", len(guild_map), "ids:", len(id_map))
print("LEAKS:", leaks)
if leaks:
    raise SystemExit("Unsanitised values found; fixtures must not be committed.")
