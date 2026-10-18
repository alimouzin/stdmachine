// Nombre pseudo-aléatoire dans [0, 1) pour les objets simulés (générateur congruentiel, Numerical Recipes)
FUNCTION F_SimRandom : LREAL
GVL_Sim.nSeed := GVL_Sim.nSeed * 1664525 + 1013904223;
F_SimRandom := UDINT_TO_LREAL(GVL_Sim.nSeed) / 4294967296.0;
END_FUNCTION
