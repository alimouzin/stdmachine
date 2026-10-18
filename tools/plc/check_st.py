"""Contrôles des sources ST de plc/src sans compilateur TwinCAT (ils ne le remplacent pas).

    python3 tools/plc/check_st.py [dossier]      (par défaut plc/src)

Les contrôles sont dans tools/plc/st_checks :
  doublons  : identifiants déclarés deux fois dans une méthode, à la casse près (ST ne distingue pas I de i)
  acces     : variable interne (VAR) d'un bloc lue ou écrite depuis l'extérieur
  chaines   : membre inexistant dans une chaîne a.b[i].c (blocs, interfaces, structures, GVL)
  arguments : nombre ou noms d'arguments d'un appel de méthode ou de fonction
"""
import pathlib
import subprocess
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else HERE.parents[1] / "plc" / "src"
CHECKS = ("doublons", "acces", "chaines", "arguments")


def main():
    files = [str(p) for p in sorted(ROOT.rglob("*.st"))]
    found = False
    for name in CHECKS:
        args = files if name == "doublons" else [str(ROOT)]
        r = subprocess.run([sys.executable, str(HERE / "st_checks" / (name + ".py"))] + args,
                           capture_output=True, text=True)
        if r.returncode != 0:
            print("[%s] erreur du contrôle : %s" % (name, r.stderr.strip()))
            found = True
        for line in sorted(set(l for l in r.stdout.splitlines() if l.strip())):
            print("[%s] %s" % (name, line))
            found = True
    if not found:
        print("Aucun problème trouvé.")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
