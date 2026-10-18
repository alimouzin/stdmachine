#!/usr/bin/env python3
"""Génère plc/src/00_Constants/GVL_Catalog.st depuis catalog/catalog.json (docs/04 § 1, principe 6).

Le fichier généré n'est jamais édité à la main : il donne au PLC les numéros de types, d'actions et
d'erreurs, et les tables dont la base des objets a besoin (actions connues, actions de mouvement,
actions réservées au manuel, actions immédiates, timeouts par défaut du catalogue).

Usage : python3 tools/plc/gen_catalog.py [catalog/catalog.json] [plc/src/00_Constants/GVL_Catalog.st]
"""
import json
import re
import sys
import unicodedata

MAX_TYPE = 149
MAX_ACTION = 31

# Erreurs communes (docs/03 § 1.3) : noms anglais fixes, les numéros viennent du catalogue.
COMMON_NAMES = {
    1: "Timeout", 2: "Interlocked", 3: "NotReady", 4: "UnknownAction", 5: "BadParam", 6: "Aborted",
    7: "Disabled", 8: "CommLost", 9: "NotInitialized", 10: "NotWired", 11: "ManualOnly",
}


def ident(text):
    """« Move travail » -> « MoveTravail » (identifiant ASCII tiré du libellé du catalogue)."""
    text = text.replace("+", " Plus").replace("\u2212", " Moins").replace(" -", " Moins")
    text = unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode()
    words = re.findall(r"[A-Za-z0-9]+", text)
    out = "".join(w[:1].upper() + w[1:] for w in words)
    return out if out and not out[0].isdigit() else "N" + out


def compress(values):
    """Liste d'initialisation ST avec répétitions : [1, 0, 0, 0] -> 1, 3(0)."""
    parts, i = [], 0
    while i < len(values):
        j = i
        while j < len(values) and values[j] == values[i]:
            j += 1
        n = j - i
        parts.append(str(values[i]) if n == 1 else "%d(%s)" % (n, values[i]))
        i = j
    return ", ".join(parts)


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "catalog/catalog.json"
    dst = sys.argv[2] if len(sys.argv) > 2 else "plc/src/00_Constants/GVL_Catalog.st"
    with open(src, encoding="utf-8") as fh:
        cat = json.load(fh)

    action_mask = [0] * (MAX_TYPE + 1)
    motion_mask = [0] * (MAX_TYPE + 1)
    manual_mask = [0] * (MAX_TYPE + 1)
    immediate_mask = [0] * (MAX_TYPE + 1)
    timeouts = [0] * ((MAX_TYPE + 1) * MAX_ACTION)

    lines = []
    w = lines.append
    w("// Généré par tools/plc/gen_catalog.py depuis catalog/catalog.json : ne pas modifier à la main.")
    w("{attribute 'qualified_only'}")
    w("VAR_GLOBAL CONSTANT")
    w("    CatalogVersion : UINT := %d;" % cat.get("catalogVersion", 1))
    w("")
    w("    // Erreurs communes à tous les objets (docs/03 § 1.3)")
    for e in cat["commonErrors"]:
        w("    Err_%s : UINT := %d;   // %s" % (COMMON_NAMES.get(e["code"], "E%d" % e["code"]), e["code"], e["text"]))
    w("")
    w("    // Types d'objets et de modules")
    for t in cat["types"] + cat["modules"]:
        w("    Type_%s : UINT := %d;   // %s" % (t["code"], t["id"], t["name"]))

    for t in cat["types"]:
        tid, code = t["id"], t["code"]
        w("")
        w("    // %s (type %d) : actions et erreurs" % (t["name"], tid))
        seen = set()
        for a in t.get("actions", []):
            no = a["no"]
            name = code + "_" + ident(a["name"])
            while name in seen:
                name += "_"
            seen.add(name)
            w("    %s : UINT := %d;" % (name, no))
            bit = 1 << (no - 1)
            action_mask[tid] |= bit
            if a.get("motion"):
                motion_mask[tid] |= bit
            if a.get("manualOnly"):
                manual_mask[tid] |= bit
            if a.get("immediate"):
                immediate_mask[tid] |= bit
            if isinstance(a.get("timeoutMs"), (int, float)):
                timeouts[tid * MAX_ACTION + (no - 1)] = int(a["timeoutMs"])
        for e in t.get("errors", []):
            w("    %s_Err%d : UINT := %d;   // %s" % (code, e["code"], e["code"], e["text"]))

    w("")
    w("    // Tables par n° de type (bit n-1 = action n)")
    w("    aActionMask    : ARRAY[0..%d] OF UDINT := [%s];" % (MAX_TYPE, compress(action_mask)))
    w("    aMotionMask    : ARRAY[0..%d] OF UDINT := [%s];" % (MAX_TYPE, compress(motion_mask)))
    w("    aManualOnlyMask: ARRAY[0..%d] OF UDINT := [%s];" % (MAX_TYPE, compress(manual_mask)))
    w("    aImmediateMask : ARRAY[0..%d] OF UDINT := [%s];" % (MAX_TYPE, compress(immediate_mask)))
    w("    // Timeout par défaut du catalogue (ms), [type, action] ; 0 = calculé par l'objet ou sans timeout")
    w("    aDefaultTimeoutMs : ARRAY[0..%d, 1..%d] OF UDINT := [%s];" % (MAX_TYPE, MAX_ACTION, compress(timeouts)))
    w("END_VAR")

    with open(dst, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines) + "\n")
    print("GVL_Catalog écrit dans %s" % dst)


if __name__ == "__main__":
    main()
