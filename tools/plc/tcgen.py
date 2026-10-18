#!/usr/bin/env python3
"""Convertit les sources ST lisibles de plc/src en fichiers TwinCAT 3 importables (plc/StdMachineLib).

Un fichier .st contient un seul objet TwinCAT :
- TYPE ... END_TYPE                         -> .TcDUT
- VAR_GLOBAL ... END_VAR (fichier GVL_*.st)  -> .TcGVL
- INTERFACE ... (méthodes et propriétés)     -> .TcIO
- FUNCTION / FUNCTION_BLOCK / PROGRAM        -> .TcPOU (méthodes et propriétés comprises)

Dans un FUNCTION_BLOCK ou une INTERFACE, chaque méthode commence par une ligne « METHOD ... » et finit par
« END_METHOD » ; chaque propriété commence par « PROPERTY ... », contient « GET » ... « END_GET » (et
« SET » ... « END_SET »), et finit par « END_PROPERTY ». Le corps du bloc lui-même est écrit avant la
première méthode ; le fichier se termine par « END_FUNCTION_BLOCK » (ou END_INTERFACE, END_FUNCTION,
END_PROGRAM), qui n'est pas recopié (TwinCAT ne le veut pas dans ses fichiers).

La déclaration d'un élément s'arrête au dernier END_VAR qui précède la première instruction.
Les identifiants (GUID) sont dérivés du nom : deux générations successives donnent les mêmes fichiers.

Usage : python3 tools/plc/tcgen.py [plc/src] [plc/StdMachineLib]
"""
import os
import re
import sys
import uuid
from xml.sax.saxutils import quoteattr

NS = uuid.UUID("5d0f4c0e-3a51-4f3c-9b7e-6a0c1d2e3f40")
VERSION = 'Version="1.1.0.1" ProductVersion="3.1.4024.12"'

VAR_START = re.compile(r"^\s*VAR(_INPUT|_OUTPUT|_IN_OUT|_GLOBAL|_INST|_STAT|_TEMP)?\b(\s+(CONSTANT|PERSISTENT|RETAIN))*\s*(//.*|\(\*.*)?$")
END_VAR = re.compile(r"^\s*END_VAR\b")


def guid(*parts):
    return "{" + str(uuid.uuid5(NS, "/".join(parts))) + "}"


def cdata(text):
    return "<![CDATA[" + text.replace("]]>", "]]]]><![CDATA[>") + "]]>"


def is_neutral(line):
    s = line.strip()
    return s == "" or s.startswith("//") or s.startswith("(*") or s.startswith("{")


def split_decl_impl(lines):
    """Déclaration = en-tête + blocs VAR (et commentaires, pragmas) ; le reste est l'implémentation."""
    i = 1
    last_decl = 0
    in_var = False
    while i < len(lines):
        line = lines[i]
        if in_var:
            if END_VAR.match(line):
                in_var = False
                last_decl = i
        elif VAR_START.match(line):
            in_var = True
        elif is_neutral(line):
            pass
        else:
            break
        i += 1
    decl = lines[: last_decl + 1] if last_decl else lines[:1]
    impl = lines[last_decl + 1:] if last_decl else lines[1:]
    while impl and impl[0].strip() == "":
        impl = impl[1:]
    while impl and impl[-1].strip() == "":
        impl = impl[:-1]
    return "\n".join(decl).rstrip() + "\n", "\n".join(impl) + ("\n" if impl else "")


def parse_members(lines, owner):
    """Sépare le corps du bloc, ses méthodes et ses propriétés. Les commentaires qui précèdent juste une
    méthode ou une propriété vont dans sa déclaration, pas dans le corps du bloc."""
    body, members, pending, i = [], [], [], 0

    def take_comments():
        comments = [l for l in pending if l.strip() != ""]
        pending.clear()
        return comments

    while i < len(lines):
        line = lines[i]
        s = line.strip()
        if s.startswith("METHOD ") or s.startswith("PROPERTY "):
            kind = "method" if s.startswith("METHOD ") else "property"
            end = "END_METHOD" if kind == "method" else "END_PROPERTY"
            j = i
            while not lines[j].strip().startswith(end):
                j += 1
            name = re.match(r"(?:METHOD|PROPERTY)\s+(?:(?:PUBLIC|PRIVATE|PROTECTED|INTERNAL|ABSTRACT|FINAL)\s+)*(\w+)", s).group(1)
            members.append((kind, name, lines[i:j], take_comments()))
            i = j + 1
        elif s == "" or s.startswith("//"):
            pending.append(line)
            i += 1
        else:
            body.extend(pending)
            pending.clear()
            body.append(line)
            i += 1
    if not members:
        body.extend(pending)
    return body, members


def accessor(lines, kind):
    """Extrait GET ... END_GET ou SET ... END_SET."""
    out, inside = None, False
    for line in lines:
        s = line.strip()
        if s == kind:
            inside, out = True, []
        elif s == "END_" + kind:
            inside = False
        elif inside:
            out.append(line)
    return out


def method_xml(owner, name, lines, interface, comments=()):
    prefix = "".join(c.rstrip() + "\n" for c in comments)
    if interface:
        decl = prefix + "\n".join(lines).rstrip() + "\n"
        return '    <Method Name=%s Id="%s">\n      <Declaration>%s</Declaration>\n    </Method>\n' % (
            quoteattr(name), guid(owner, "m", name), cdata(decl))
    decl, impl = split_decl_impl(lines)
    decl = prefix + decl
    return ('    <Method Name=%s Id="%s">\n      <Declaration>%s</Declaration>\n'
            '      <Implementation>\n        <ST>%s</ST>\n      </Implementation>\n    </Method>\n') % (
        quoteattr(name), guid(owner, "m", name), cdata(decl), cdata(impl))


def property_xml(owner, name, lines, interface, comments=()):
    header = "".join(c.rstrip() + "\n" for c in comments) + lines[0].rstrip() + "\n"
    out = '    <Property Name=%s Id="%s">\n      <Declaration>%s</Declaration>\n' % (
        quoteattr(name), guid(owner, "p", name), cdata(header))
    for kind, tag in (("GET", "Get"), ("SET", "Set")):
        acc = accessor(lines, kind)
        if acc is None:
            continue
        if interface:
            out += '      <%s Name="%s" Id="%s">\n        <Declaration>%s</Declaration>\n      </%s>\n' % (
                tag, tag, guid(owner, "p", name, tag), cdata("VAR\nEND_VAR\n"), tag)
            continue
        decl, impl = split_decl_impl(["(header)"] + acc)
        decl = decl.split("\n", 1)[1] if "\n" in decl else ""
        if decl.strip() == "":
            decl = "VAR\nEND_VAR\n"
        out += ('      <%s Name="%s" Id="%s">\n        <Declaration>%s</Declaration>\n'
                '        <Implementation>\n          <ST>%s</ST>\n        </Implementation>\n      </%s>\n') % (
            tag, tag, guid(owner, "p", name, tag), cdata(decl), cdata(impl), tag)
    return out + "    </Property>\n"


def strip_end(lines, keyword):
    while lines and lines[-1].strip() == "":
        lines = lines[:-1]
    if lines and lines[-1].strip().startswith(keyword):
        lines = lines[:-1]
    return lines


def convert(text, filename):
    raw = text.replace("\r\n", "\n").split("\n")
    # pragmas et commentaires de tête : ils restent dans la déclaration
    head_idx = next(i for i, l in enumerate(raw) if not is_neutral(l))
    first = raw[head_idx].strip()
    name = os.path.splitext(os.path.basename(filename))[0]

    if first.startswith("TYPE"):
        body = "\n".join(raw).strip() + "\n"
        return name, ".TcDUT", ('<?xml version="1.0" encoding="utf-8"?>\n<TcPlcObject %s>\n'
                                '  <DUT Name=%s Id="%s">\n    <Declaration>%s</Declaration>\n  </DUT>\n</TcPlcObject>\n') % (
            VERSION, quoteattr(name), guid(name), cdata(body))

    if first.startswith("VAR_GLOBAL"):
        body = "\n".join(raw).strip() + "\n"
        return name, ".TcGVL", ('<?xml version="1.0" encoding="utf-8"?>\n<TcPlcObject %s>\n'
                                '  <GVL Name=%s Id="%s">\n    <Declaration>%s</Declaration>\n  </GVL>\n</TcPlcObject>\n') % (
            VERSION, quoteattr(name), guid(name), cdata(body))

    if first.startswith("INTERFACE"):
        lines = strip_end(raw, "END_INTERFACE")
        body, members = parse_members(lines, name)
        out = '<?xml version="1.0" encoding="utf-8"?>\n<TcPlcObject %s>\n  <Itf Name=%s Id="%s">\n    <Declaration>%s</Declaration>\n' % (
            VERSION, quoteattr(name), guid(name), cdata("\n".join(body).strip() + "\n"))
        for kind, mname, mlines, mcomments in members:
            out += (method_xml(name, mname, mlines, True, mcomments) if kind == "method"
                    else property_xml(name, mname, mlines, True, mcomments))
        return name, ".TcIO", out + "  </Itf>\n</TcPlcObject>\n"

    for kw in ("FUNCTION_BLOCK", "FUNCTION", "PROGRAM"):
        if re.match(r"^(?:\{[^}]*\}\s*)*" + kw + r"\b", first):
            lines = strip_end(raw, "END_" + kw)
            body, members = parse_members(lines, name)
            decl, impl = split_decl_impl(body[head_idx:] if head_idx else body)
            if head_idx:
                decl = "\n".join(raw[:head_idx]) + "\n" + decl
            out = ('<?xml version="1.0" encoding="utf-8"?>\n<TcPlcObject %s>\n  <POU Name=%s Id="%s" SpecialFunc="None">\n'
                   '    <Declaration>%s</Declaration>\n    <Implementation>\n      <ST>%s</ST>\n    </Implementation>\n') % (
                VERSION, quoteattr(name), guid(name), cdata(decl), cdata(impl))
            for kind, mname, mlines, mcomments in members:
                out += (method_xml(name, mname, mlines, False, mcomments) if kind == "method"
                        else property_xml(name, mname, mlines, False, mcomments))
            return name, ".TcPOU", out + "  </POU>\n</TcPlcObject>\n"

    raise ValueError("%s : type d'objet inconnu (« %s »)" % (filename, first))


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "plc/src"
    dst = sys.argv[2] if len(sys.argv) > 2 else "plc/StdMachineLib"
    count = 0
    for root, _, files in os.walk(src):
        for f in sorted(files):
            if not f.endswith(".st"):
                continue
            path = os.path.join(root, f)
            with open(path, encoding="utf-8") as fh:
                name, ext, xml = convert(fh.read(), path)
            rel = os.path.relpath(root, src)
            out_dir = os.path.join(dst, rel)
            os.makedirs(out_dir, exist_ok=True)
            with open(os.path.join(out_dir, name + ext), "w", encoding="utf-8", newline="\r\n") as fh:
                fh.write(xml)
            count += 1
    print("%d fichiers TwinCAT écrits dans %s" % (count, dst))


if __name__ == "__main__":
    main()
