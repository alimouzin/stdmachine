import re, sys, pathlib
# Vérifie les appels de méthodes (inst.M(...)) et de fonctions F_x(...) : nombre d'arguments positionnels
# et noms des arguments nommés, contre VAR_INPUT / VAR_IN_OUT / VAR_OUTPUT de la déclaration.
root = pathlib.Path(sys.argv[1])
files = list(root.rglob('*.st'))
def strip(t):
    t = re.sub(r"'[^'\n]*'", "''", t)
    t = re.sub(r'//.*', '', t)
    return re.sub(r'\(\*.*?\*\)', '', t, flags=re.S)
TYPEPAT = r'(?:REFERENCE TO\s+|POINTER TO\s+)?(?:ARRAY\s*\[[^\]]*\]\s*OF\s+(?:POINTER TO\s+|REFERENCE TO\s+)?)?(\w+)'
def sig(text):
    ins, outs = [], []
    for vm in re.finditer(r'^\s*(VAR_INPUT|VAR_IN_OUT|VAR_OUTPUT)\b[^\n]*$(.*?)^\s*END_VAR', text, flags=re.M|re.S):
        for line in vm.group(2).split('\n'):
            mm = re.match(r'\s*([A-Za-z_]\w*)\s*:', line)
            if mm: (outs if vm.group(1) == 'VAR_OUTPUT' else ins).append(mm.group(1).lower())
    return ins, outs
types, funcs = {}, {}
for f in files:
    t = strip(f.read_text(encoding='utf-8'))
    m = re.match(r'\s*(?:\{[^}]*\}\s*)*(FUNCTION_BLOCK|INTERFACE|FUNCTION)\s+(\w+)(?:\s*:\s*\w+)?(?:\s+EXTENDS\s+(\w+))?', t)
    if not m: continue
    kind, name, base = m.group(1), m.group(2).lower(), (m.group(3) or '').lower()
    if kind == 'FUNCTION':
        funcs[name] = sig(t); continue
    meths = {}
    for mm in re.finditer(r'^METHOD\s+(?:PRIVATE\s+|PROTECTED\s+|PUBLIC\s+|INTERNAL\s+)?(\w+)[^\n]*\n(.*?)(?=^END_METHOD|^METHOD)', t, flags=re.M|re.S):
        body = mm.group(2)
        decl = body.split('\nEND_VAR')[0] if 'END_VAR' in body else ''
        # déclaration : jusqu'au dernier END_VAR avant la première instruction
        parts = re.split(r'^(?!\s*(?:VAR\w*|END_VAR|[A-Za-z_]\w*\s*:|$))', body, maxsplit=1, flags=re.M)
        meths[mm.group(1).lower()] = sig(parts[0])
    head = re.split(r'^(?:METHOD|PROPERTY)\b', t, flags=re.M)[0]
    var = {}
    for vm in re.finditer(r'^\s*VAR\w*\b[^\n]*$(.*?)^\s*END_VAR', head, flags=re.M|re.S):
        for line in vm.group(1).split('\n'):
            mm = re.match(r'\s*([A-Za-z_]\w*)\s*:\s*' + TYPEPAT, line)
            if mm: var[mm.group(1).lower()] = mm.group(2).lower()
    types[name] = (base, meths, var, sig(head))
def method(tn, mn):
    while tn in types:
        if mn in types[tn][1]: return types[tn][1][mn]
        tn = types[tn][0]
    return None
def split_args(s):
    depth, cur, out = 0, '', []
    for ch in s:
        if ch in '([': depth += 1
        if ch in ')]': depth -= 1
        if ch == ',' and depth == 0: out.append(cur); cur = ''
        else: cur += ch
    if cur.strip(): out.append(cur)
    return [a.strip() for a in out]
def find_call_args(text, start):
    depth = 0
    for i in range(start, len(text)):
        if text[i] == '(': depth += 1
        elif text[i] == ')':
            depth -= 1
            if depth == 0: return text[start+1:i]
    return None
def check(f, line, label, s, args):
    ins, outs = s
    named = [a for a in args if re.match(r'\w+\s*(:=|=>)', a)]
    if named:
        for a in named:
            n = re.match(r'(\w+)', a).group(1).lower()
            if n not in ins and n not in outs: print(f'{f}:{line}: {label} : argument « {n} » inconnu')
    elif len(args) != len(ins):
        print(f'{f}:{line}: {label} : {len(args)} arguments, {len(ins)} attendus')
for f in files:
    t = strip(f.read_text(encoding='utf-8'))
    mfb = re.match(r'\s*FUNCTION_BLOCK\s+(\w+)', t)
    fbn = mfb.group(1).lower() if mfb else None
    for blk_m in re.finditer(r'^(?:METHOD|PROPERTY|FUNCTION_BLOCK|FUNCTION )[^\n]*\n(.*?)(?=^METHOD|^PROPERTY|^END_FUNCTION_BLOCK|\Z)', t, flags=re.M|re.S):
        blk = blk_m.group(0); off = blk_m.start()
        decl = dict(types[fbn][2]) if fbn in types else {}
        for vm in re.finditer(r'^\s*VAR\w*\b[^\n]*$(.*?)^\s*END_VAR', blk, flags=re.M|re.S):
            for line in vm.group(1).split('\n'):
                mm = re.match(r'\s*([A-Za-z_]\w*)\s*:\s*' + TYPEPAT, line)
                if mm: decl[mm.group(1).lower()] = mm.group(2).lower()
        for m in re.finditer(r'(?<![\w.])(\w+)(?:\[[^\]]*\])?\^?\.(\w+)\s*\(', blk):
            v, mn = m.group(1).lower(), m.group(2).lower()
            if v not in decl: continue
            s = method(decl[v], mn)
            if s is None: continue
            a = find_call_args(blk, m.end() - 1)
            if a is None: continue
            check(f.relative_to(root), t[:off].count('\n') + blk[:m.start()].count('\n') + 1, f'{m.group(1)}.{m.group(2)}', s, split_args(a))
        for m in re.finditer(r'(?<![\w.])(F_\w+|_\w+)\s*\(', blk):
            n = m.group(1).lower()
            s = funcs.get(n) or (method(fbn, n) if fbn else None)
            if s is None: continue
            a = find_call_args(blk, m.end() - 1)
            if a is None: continue
            check(f.relative_to(root), t[:off].count('\n') + blk[:m.start()].count('\n') + 1, m.group(1), s, split_args(a))
