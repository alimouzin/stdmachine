import re, sys, pathlib
# Vérifie les chaînes d'accès « a.b[i].c » : chaque membre doit exister dans le type du maillon précédent
# (blocs, interfaces, structures, GVL). Les types inconnus (bibliothèques Beckhoff) arrêtent la vérification.
root = pathlib.Path(sys.argv[1])
files = list(root.rglob('*.st'))
def strip(t):
    t = re.sub(r"'[^'\n]*'", "''", t)
    t = re.sub(r'//.*', '', t)
    return re.sub(r'\(\*.*?\*\)', '', t, flags=re.S)
TYPEPAT = r'(?:REFERENCE TO\s+|POINTER TO\s+)?(?:ARRAY\s*\[[^\]]*\]\s*OF\s+(?:POINTER TO\s+|REFERENCE TO\s+)?)?(\w+)'
types = {}   # nom -> (bases, {membre: type ou None})
def parse_vars(text):
    d = {}
    for line in text.split('\n'):
        mm = re.match(r'\s*([A-Za-z_]\w*)\s*:\s*' + TYPEPAT, line)
        if mm: d[mm.group(1).lower()] = mm.group(2).lower()
    return d
for f in files:
    t = strip(f.read_text(encoding='utf-8'))
    m = re.match(r'\s*(?:\{[^}]*\}\s*)*(FUNCTION_BLOCK|INTERFACE|TYPE|VAR_GLOBAL)\s*(\w*)(?:\s*:)?(?:\s+EXTENDS\s+(\w+))?(?:\s+IMPLEMENTS\s+([\w, ]+))?', t)
    if not m: continue
    kind = m.group(1)
    if kind == 'VAR_GLOBAL':
        name = f.stem
        types[name.lower()] = ([], parse_vars(t))
        continue
    name = m.group(2)
    bases = [b.strip().lower() for b in ((m.group(3) or '') + ',' + (m.group(4) or '')).split(',') if b.strip()]
    if kind == 'TYPE':
        if 'STRUCT' not in t: continue
        types[name.lower()] = (bases, parse_vars(t))
        continue
    head = re.split(r'^(?:METHOD|PROPERTY)\b', t, flags=re.M)[0]
    mem = {}
    for vm in re.finditer(r'^\s*VAR\w*\b[^\n]*$(.*?)^\s*END_VAR', head, flags=re.M|re.S):
        mem.update(parse_vars(vm.group(1)))
    for mm in re.finditer(r'^(?:METHOD|PROPERTY)\s+(?:PRIVATE\s+|PROTECTED\s+|PUBLIC\s+|INTERNAL\s+)?(\w+)(?:\s*:\s*' + TYPEPAT + ')?', t, flags=re.M):
        mem[mm.group(1).lower()] = (mm.group(2) or '').lower() or None
    types[name.lower()] = (bases, mem)
def member(tn, mname, seen=None):
    seen = seen or set()
    if tn not in types or tn in seen: return 'UNKNOWN_TYPE'
    seen.add(tn)
    b, m = types[tn]
    if mname in m: return m[mname]
    for x in b:
        r = member(x, mname, seen)
        if r != 'UNKNOWN_TYPE' and r != 'MISSING': return r
    return 'MISSING'
seen_err = set()
for f in files:
    t = strip(f.read_text(encoding='utf-8'))
    blocks = re.split(r'^(?=METHOD|PROPERTY|FUNCTION_BLOCK|FUNCTION )', t, flags=re.M)
    fbname = None
    mfb = re.match(r'\s*FUNCTION_BLOCK\s+(\w+)', t)
    if mfb: fbname = mfb.group(1).lower()
    pos = 0
    for blk in blocks:
        off = t.find(blk, pos); pos = off + len(blk)
        decl = {}
        if fbname: decl.update({k: v for k, v in (types.get(fbname, ([], {}))[1].items()) if v})
        for vm in re.finditer(r'^\s*VAR\w*\b[^\n]*$(.*?)^\s*END_VAR', blk, flags=re.M|re.S):
            decl.update(parse_vars(vm.group(1)))
        for g in types:
            if g.startswith('gvl_'): decl[g] = g
        for m in re.finditer(r'(?<![\w.\]])([A-Za-z_]\w*)((?:\[[^\[\]]*(?:\[[^\[\]]*\][^\[\]]*)*\]|\^)*(?:\.\w+(?:\[[^\[\]]*(?:\[[^\[\]]*\][^\[\]]*)*\]|\^|\(\))*)+)', blk):
            root_name = m.group(1).lower()
            if root_name not in decl: continue
            tn = decl[root_name]
            chain = re.findall(r'\.(\w+)', re.sub(r'\[[^\[\]]*(?:\[[^\[\]]*\][^\[\]]*)*\]', '', m.group(2)))
            for c in chain:
                r = member(tn, c.lower())
                if r == 'UNKNOWN_TYPE' or r is None: break
                if r == 'MISSING':
                    line = t[:off].count('\n') + blk[:m.start()].count('\n') + 1
                    key = (str(f), line, c)
                    if key not in seen_err:
                        seen_err.add(key)
                        print(f'{f.relative_to(root)}:{line}: {m.group(0)[:60]} : « {c} » absent de {tn}')
                    break
                tn = r
