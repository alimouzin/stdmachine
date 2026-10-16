using StdMachine.Core;

namespace StdMachine.Simulation.Demo;

public sealed record PartTraceLine(string Station, string Operation, DateTime Start, double DurationSeconds, Verdict Verdict, string KeyData, bool HasCurve);

public sealed record AuditLine(DateTime Time, string User, string What, string Before, string After);

public sealed record RightLine(string Group, string Code, string Name, int Level, bool Locked);

public sealed record DeployHistoryLine(DateTime At, string Hash, string By, string Summary, bool Current);

public sealed record IoLine(string Kind, int Index, string Name, string Terminal, string UsedBy);

public sealed record PressSegment(string Name, double Start, double End, double ForceMin, double ForceMax);

public sealed record PressRecipe(int Number, string Name, IReadOnlyList<PressSegment> Segments);

public sealed record RejectLine(string PartId, DateTime Time, string Operation, string Data);

/// <summary>Données de démonstration complémentaires des pages (historiques, journaux, tableaux) : MX-4, docs/08 § 6.</summary>
public static class DemoExtras
{
    public static IReadOnlyList<AlarmInfo> AlarmHistory(DateTime now)
    {
        DateTime d(int minutesAgo) => now.AddMinutes(-minutesAgo);
        var list = new List<AlarmInfo>
        {
            new AlarmInfo(901, d(38), d(36), "Pressage 01", "Robot XYZC", 210, 4, "Position non atteinte", "Axe Z : écart de poursuite", AlarmSeverity.StationFault, AlarmState.Acknowledged, 2, "Poser pièce"),
            new AlarmInfo(902, d(74), d(73), "Chargement", "Chargeur vibrant", 340, 1, "Plus de pièces", "Bac vide depuis 20 s", AlarmSeverity.Warning, AlarmState.Acknowledged, 1, "Alimenter"),
            new AlarmInfo(903, d(121), d(119), "Contrôle", "Vision Asyril", 420, 2, "Vision : temps dépassé", "Pas de réponse en 1,5 s", AlarmSeverity.Warning, AlarmState.Acknowledged, 4, "Mesurer"),
            new AlarmInfo(904, d(188), d(186), "Machine", null, 12, 0, "Pression d'air basse", "Seuil 5,5 bar", AlarmSeverity.MachineFault, AlarmState.Acknowledged, null, null),
            new AlarmInfo(905, d(256), d(250), "Pressage 02", "Presse", 520, 3, "Force hors fenêtre", "Segment 2 : 5,9 kN pour 4,0 à 5,5 kN", AlarmSeverity.Quality, AlarmState.Acknowledged, 3, "Presser pignon"),
            new AlarmInfo(906, d(301), d(300), "Déchargement", "Robot XYZC", 215, 1, "Pièce non saisie", "Dépression insuffisante", AlarmSeverity.StationFault, AlarmState.Acknowledged, 5, "Prendre pièce"),
            new AlarmInfo(907, d(402), d(401), "Pressage 01", "Aspiration", 330, 2, "Vide non atteint", "Seuil −55 kPa", AlarmSeverity.Warning, AlarmState.Acknowledged, 2, "Aspirer"),
            new AlarmInfo(908, d(515), d(512), "Machine", null, 3, 0, "Porte ouverte", "Circuit de sécurité 2", AlarmSeverity.MachineFault, AlarmState.Acknowledged, null, null),
            new AlarmInfo(909, d(640), d(639), "Rebut", "Bac rebut", 350, 1, "Bac plein", "Vider le bac", AlarmSeverity.Information, AlarmState.Acknowledged, 6, null),
            new AlarmInfo(910, d(790), d(785), "Pressage 02", "Robot XYZC", 210, 2, "Collision détectée", "Axe C : courant maximal", AlarmSeverity.StationFault, AlarmState.Acknowledged, 3, "Poser pièce"),
            new AlarmInfo(911, d(910), d(908), "Contrôle", "Lecteur de code", 430, 1, "Code illisible", "3 essais", AlarmSeverity.Warning, AlarmState.Acknowledged, 4, "Lire"),
            new AlarmInfo(912, d(1120), d(1116), "Chargement", "Chargeur vibrant", 341, 2, "Bourrage", "Pièce coincée à la goulotte", AlarmSeverity.StationFault, AlarmState.Acknowledged, 1, "Alimenter"),
        };
        return list;
    }

    public static IReadOnlyList<PartTraceLine> PartHistory(string partId, DateTime now)
    {
        DateTime t(int minutesAgo, int seconds = 0) => now.AddMinutes(-minutesAgo).AddSeconds(seconds);
        return new List<PartTraceLine>
        {
            new PartTraceLine("Chargement", "Alimenter", t(14), 4.2, Verdict.Good, "Identifiant " + partId, false),
            new PartTraceLine("Pressage 01", "Pressage pignon › Presser pignon", t(13, 10), 12.6, Verdict.Good, "Force finale 5,12 kN · course 11,4 mm", true),
            new PartTraceLine("Pressage 02", "Sertissage › Sertir", t(12, 20), 9.8, Verdict.Good, "Force finale 7,94 kN · course 6,1 mm", true),
            new PartTraceLine("Contrôle", "Contrôle › Mesurer", t(11, 15), 6.1, Verdict.Good, "Cote A 12,02 mm · cote B 8,48 mm", false),
            new PartTraceLine("Déchargement", "Sortie › Évacuer", t(10, 5), 3.4, Verdict.Good, "Pièce évacuée", false),
        };
    }

    public static IReadOnlyList<(double Stroke, double Force)> PressCurve()
    {
        var pts = new List<(double, double)>();
        for (int i = 0; i <= 60; i++)
        {
            double s = i * 0.2;
            double f = s < 3 ? 0.05 * s : s < 8 ? 0.15 + (s - 3) * 0.55 : 2.9 + (s - 8) * 0.55 + Math.Sin(i * 0.7) * 0.05;
            pts.Add((s, Math.Round(f, 2)));
        }
        return pts;
    }

    public static IReadOnlyList<AuditLine> Audit(DateTime now) => new List<AuditLine>
    {
        new AuditLine(now.AddMinutes(-12), "A. Martin", "Pose du forçage « Pressage 01 hors production »", "—", "Réglage"),
        new AuditLine(now.AddMinutes(-47), "A. Martin", "Position « Pose presse » apprise", "non apprise", "12,0 ; 84,5 ; −31,6 ; 0,0"),
        new AuditLine(now.AddHours(-3), "C. Dupont", "Droit « Déployer » modifié", "Niveau 3", "Niveau 2"),
        new AuditLine(now.AddHours(-5), "Administrateur", "Utilisateur « Équipe du soir » créé", "—", "Niveau 0"),
        new AuditLine(now.AddHours(-26), "C. Dupont", "Seuil qualité « Machine » modifié", "3 %", "5 %"),
        new AuditLine(now.AddHours(-28), "B. Rossi", "Mover 3 mis hors service", "En service", "Hors service · nid abîmé"),
    };

    public static IReadOnlyList<RightLine> Rights() => new List<RightLine>
    {
        new RightLine("Machine", "machine.start", "Démarrer, arrêter, réarmer la machine", 0, false),
        new RightLine("Machine", "machine.abort", "Abort", 0, true),
        new RightLine("Machine", "machine.mode", "Changer le mode (Production / Maintenance)", 1, false),
        new RightLine("Machine", "machine.override", "Régler l'override machine", 1, false),
        new RightLine("Stations", "station.command", "Commander une station seule", 0, false),
        new RightLine("Stations", "quality.release", "Libérer après une réaction qualité", 1, false),
        new RightLine("Stations", "quality.reset", "Remettre à zéro les compteurs qualité", 2, false),
        new RightLine("Manuel", "manual.move", "Mouvements manuels et jog", 1, false),
        new RightLine("Manuel", "manual.teach", "Apprendre une position", 1, false),
        new RightLine("Manuel", "manual.force", "Forcer une entrée simulée", 2, false),
        new RightLine("Studio", "studio.edit", "Modifier le projet", 2, false),
        new RightLine("Studio", "studio.deploy", "Déployer", 2, false),
        new RightLine("Studio", "quality.config", "Régler les seuils qualité", 2, false),
        new RightLine("OF", "order.start", "Lancer, suspendre un OF", 0, false),
        new RightLine("OF", "order.cancel", "Annuler un OF", 1, false),
        new RightLine("Administration", "user.manage", "Gérer les utilisateurs et les droits", 3, false),
        new RightLine("Administration", "system.settings", "Régler les communications et le système", 3, false),
    };

    public static IReadOnlyList<DeployHistoryLine> DeployHistory() => new List<DeployHistoryLine>
    {
        new DeployHistoryLine(new DateTime(2026, 10, 14, 16, 5, 0), "9F3A1C07D2E84B66", "C. Dupont", "Recette 3 ajustée · process Pressage 02 modifié", true),
        new DeployHistoryLine(new DateTime(2026, 10, 9, 11, 40, 0), "5B1E92A0C7731D4F", "C. Dupont", "Station Déchargement ajoutée", false),
        new DeployHistoryLine(new DateTime(2026, 9, 29, 15, 12, 0), "0C47E3D8A9F2B510", "C. Dupont", "Première version du projet MX-4", false),
    };

    public static IReadOnlyList<IoLine> IoImage() => new List<IoLine>
    {
        new IoLine("Entrée", 0, "Présence nid presse", "KL1408 · 1", "Pressage 01 › Présence nid presse"),
        new IoLine("Entrée", 1, "Aspiration vide OK", "KL1408 · 2", "Pressage 01 › Aspiration"),
        new IoLine("Entrée", 2, "Presse haut", "KL1408 · 3", "Pressage 01 › Presse"),
        new IoLine("Entrée", 3, "Presse bas", "KL1408 · 4", "Pressage 01 › Presse"),
        new IoLine("Entrée", 4, "Présence nid sertissage", "KL1408 · 5", "Pressage 02 › Présence nid"),
        new IoLine("Entrée", 5, "Porte sécurité 1", "KL1408 · 6", "Machine › Sécurités"),
        new IoLine("Sortie", 0, "Aspiration marche", "KL2408 · 1", "Pressage 01 › Aspiration"),
        new IoLine("Sortie", 1, "Soufflage", "KL2408 · 2", "Pressage 01 › Aspiration"),
        new IoLine("Sortie", 2, "Balise rouge", "KL2408 · 3", "Machine › Balise"),
        new IoLine("Sortie", 3, "Balise orange", "KL2408 · 4", "Machine › Balise"),
        new IoLine("Sortie", 4, "Balise verte", "KL2408 · 5", "Machine › Balise"),
        new IoLine("Sortie", 5, "Balise bleue", "KL2408 · 6", "Machine › Balise"),
        new IoLine("Analogique", 0, "Force presse", "KL3202 · 1", "Pressage 01 › Presse"),
        new IoLine("Analogique", 1, "Course presse", "KL3202 · 2", "Pressage 01 › Presse"),
    };

    public static IReadOnlyList<PressRecipe> PressRecipes() => new List<PressRecipe>
    {
        new PressRecipe(3, "Pignon Ø12", new List<PressSegment>
        {
            new PressSegment("Approche", 0, 3, 0, 0.3),
            new PressSegment("Emmanchement", 3, 8, 2.0, 5.5),
            new PressSegment("Fin de course", 8, 11.5, 4.0, 5.5),
        }),
        new PressRecipe(5, "Sertissage cap", new List<PressSegment>
        {
            new PressSegment("Approche", 0, 2, 0, 0.5),
            new PressSegment("Sertissage", 2, 6.5, 5.5, 9.0),
        }),
    };

    public static IReadOnlyList<RejectLine> RecentRejects(DateTime now) => new List<RejectLine>
    {
        new RejectLine("P-000642", now.AddMinutes(-3), "Presser pignon", "Force finale 5,9 kN (limite 5,5 kN)"),
        new RejectLine("P-000638", now.AddMinutes(-4), "Presser pignon", "Force finale 5,7 kN (limite 5,5 kN)"),
        new RejectLine("P-000635", now.AddMinutes(-6), "Sertir", "Cote A 11,8 mm (limite 11,9 mm)"),
    };
}
