import re, sys, pathlib
# Signale les accès « instance.membre » où le membre est une variable interne (VAR) d'un bloc fonctionnel :
# en ST, seules les entrées, sorties, méthodes et propriétés sont accessibles de l'extérieur.
root = pathlib.Path(sys.argv[1])
files = list(root.rglob('*.st'))
def strip(t):
    t = re.sub(r'//.*', '', t)
    return re.sub(r'\(\*.*?\*\)', '', t, flags=re.S)
fbs = {}
for f in files:
    t = strip(f.read_text(encoding='utf-8'))
    m = re.match(r'\s*FUNCTION_BLOCK\s+(\w+)(?:\s+EXTENDS\s+(\w+))?', t)
    if not m: continue
    name, base = m.group(1), m.group(2)
    head = re.split(r'^(?:METHOD|PROPERTY)\b', t, flags=re.M)[0]
    internal, public = set(), set()
    for vm in re.finditer(r'^\s*(VAR(?:_INPUT|_OUTPUT|_IN_OUT)?)\b[^\n]*$(.*?)^\s*END_VAR', head, flags=re.M|re.S):
        kind = vm.group(1)
        for line in vm.group(2).split('\n'):
            mm = re.match(r'\s*([A-Za-z_]\w*)\s*:', line)
            if mm:
                (public if kind != 'VAR' else internal).add(mm.group(1).lower())
    members = {m.group(1).lower() for m in re.finditer(r'^(?:METHOD|PROPERTY)\s+(?:PRIVATE\s+|PROTECTED\s+|PUBLIC\s+|INTERNAL\s+)?(\w+)', t, flags=re.M)}
    fbs[name.lower()] = dict(base=(base or '').lower(), internal=internal, public=public|members)
def resolve(fb):
    internal, public = set(), set()
    while fb in fbs:
        internal |= fbs[fb]['internal']; public |= fbs[fb]['public']; fb = fbs[fb]['base']
    return internal - public
for f in files:
    t = strip(f.read_text(encoding='utf-8'))
    decl = {}
    for m in re.finditer(r'^\s*(\w+)\s*:\s*(?:REFERENCE TO\s+|POINTER TO\s+)?(?:ARRAY\s*\[[^\]]*\]\s*OF\s+(?:POINTER TO\s+)?)?(FB_\w+)', t, flags=re.M):
        decl[m.group(1).lower()] = m.group(2).lower()
    for var, fb in decl.items():
        bad = resolve(fb)
        for m in re.finditer(r'\b' + re.escape(var) + r'(?:\[[^\]]*\])?\^?\.(\w+)', t, flags=re.I):
            if m.group(1).lower() in bad:
                line = t[:m.start()].count('\n') + 1
                print(f'{f}:{line}: {var}.{m.group(1)} ({fb})')
