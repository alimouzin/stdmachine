import re, sys, pathlib
# Signale, par méthode, les identifiants déclarés deux fois sans tenir compte de la casse (ST est insensible à la casse)
for p in sys.argv[1:]:
    txt = pathlib.Path(p).read_text(encoding='utf-8')
    txt = re.sub(r'//.*', '', txt)
    txt = re.sub(r'\(\*.*?\*\)', '', txt, flags=re.S)
    blocks = re.split(r'^(?=METHOD|PROPERTY|FUNCTION_BLOCK|FUNCTION )', txt, flags=re.M)
    fbvars = set()
    for b in blocks:
        head = b.split('\n',1)[0].strip()
        decls = []
        for m in re.finditer(r'^\s*VAR\w*(?:\s+PERSISTENT)?\s*$(.*?)^\s*END_VAR', b, flags=re.M|re.S):
            for line in m.group(1).split('\n'):
                mm = re.match(r'\s*([A-Za-z_]\w*)\s*:', line)
                if mm: decls.append(mm.group(1))
        low = [d.lower() for d in decls]
        if head.startswith('FUNCTION_BLOCK'):
            fbvars = set(low)
        dups = {d for d in low if low.count(d) > 1}
        # nom de la méthode = variable de retour
        mname = re.match(r'(?:METHOD|PROPERTY)\s+(?:PRIVATE\s+|PROTECTED\s+|PUBLIC\s+)?(\w+)', head)
        if mname and mname.group(1).lower() in low:
            dups.add(mname.group(1).lower()+' (nom de méthode)')
        if dups:
            print(p, head[:60], sorted(dups))
