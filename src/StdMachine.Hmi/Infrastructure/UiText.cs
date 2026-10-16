using StdMachine.Core;
using StdMachine.Hmi.Resources;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Infrastructure;

/// <summary>Libellés et tons partagés par les pages (un seul endroit pour dire « Verdict mauvais » ou « Arrêt qualité »).</summary>
public static class UiText
{
    public static string State(PackMlState s) => s.ToString();

    public static string StateDescription(PackMlState s) => Res.Get("State." + s);

    public static Tone StateTone(PackMlState s)
    {
        switch (s)
        {
            case PackMlState.Execute:
            case PackMlState.Idle: return Tone.Ok;
            case PackMlState.Held:
            case PackMlState.Resetting:
            case PackMlState.Stopping: return Tone.Warn;
            case PackMlState.Aborted:
            case PackMlState.Clearing: return Tone.Err;
            default: return Tone.Neutral;
        }
    }

    public static string Severity(AlarmSeverity s)
    {
        switch (s)
        {
            case AlarmSeverity.Information: return "Information";
            case AlarmSeverity.Warning: return "Avertissement";
            case AlarmSeverity.StationFault: return "Défaut de station";
            case AlarmSeverity.MachineFault: return "Défaut machine";
            default: return "Qualité";
        }
    }

    public static Tone SeverityTone(AlarmSeverity s)
    {
        switch (s)
        {
            case AlarmSeverity.Information: return Tone.Neutral;
            case AlarmSeverity.Warning: return Tone.Warn;
            case AlarmSeverity.Quality: return Tone.Busy;
            default: return Tone.Err;
        }
    }

    public static string SeverityIcon(AlarmSeverity s)
    {
        switch (s)
        {
            case AlarmSeverity.Information: return "info";
            case AlarmSeverity.Warning: return "warning";
            case AlarmSeverity.Quality: return "fact_check";
            default: return "report";
        }
    }

    public static string AlarmStateText(AlarmState s)
    {
        switch (s)
        {
            case AlarmState.Active: return "Active";
            case AlarmState.ClearedUnacknowledged: return "Disparue, non acquittée";
            default: return "Acquittée";
        }
    }

    public static string MoverState(MoverState s)
    {
        switch (s)
        {
            case Core.MoverState.Free: return "Libre";
            case Core.MoverState.EnRoute: return "En route";
            case Core.MoverState.Queued: return "En file";
            case Core.MoverState.InStation: return "En station";
            case Core.MoverState.Error: return "En erreur";
            case Core.MoverState.ToEmpty: return "À vider";
            default: return "Hors service";
        }
    }

    public static Tone MoverTone(MoverState s)
    {
        switch (s)
        {
            case Core.MoverState.EnRoute: return Tone.Busy;
            case Core.MoverState.Queued: return Tone.Busy;
            case Core.MoverState.InStation: return Tone.Ok;
            case Core.MoverState.Error: return Tone.Err;
            case Core.MoverState.ToEmpty: return Tone.Busy;
            default: return Tone.Neutral;
        }
    }

    public static string Verdict(Verdict v) => v == Core.Verdict.Good ? "Bon" : v == Core.Verdict.Bad ? "Mauvais" : "Non défini";

    public static Tone VerdictTone(Verdict v) => v == Core.Verdict.Good ? Tone.Ok : v == Core.Verdict.Bad ? Tone.Err : Tone.Neutral;

    public static string PartState(PartState s)
    {
        switch (s)
        {
            case Core.PartState.InProcess: return "En cours";
            case Core.PartState.Good: return "Bonne";
            case Core.PartState.Bad: return "Mauvaise";
            case Core.PartState.Exited: return "Sortie";
            case Core.PartState.Rejected: return "Rebutée";
            default: return "Retirée";
        }
    }

    public static string Declared(DeclaredState s) => StdMachine.Simulation.Demo.DemoText.Declared(s);

    /// <summary>« depuis 02 h 14 » : durée dans l'état.</summary>
    public static string Since(DateTime since)
    {
        var d = DateTime.Now - since;
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return DisplayFormat.Duration(d);
    }

    public static string Plural(int n, string singular, string plural) => n + " " + (n > 1 ? plural : singular);

    /// <summary>Pourquoi une commande est indisponible, et que faire (docs/08 § 6.1).</summary>
    public static string WhyUnavailable(StateCommand c, PackMlState s, out StateCommand? suggestion)
    {
        suggestion = null;
        switch (c)
        {
            case StateCommand.Start:
                if (s == PackMlState.Stopped) { suggestion = StateCommand.Reset; return "Démarrer est possible depuis Idle ou Held. La machine est Stopped : réarmez d'abord."; }
                if (s == PackMlState.Aborted) { suggestion = StateCommand.Clear; return "Démarrer est possible depuis Idle ou Held. La machine est Aborted : acquittez d'abord."; }
                return "Démarrer est possible depuis Idle ou Held. La machine est " + s + " : attendez la fin de la transition.";
            case StateCommand.Reset:
                if (s == PackMlState.Aborted) { suggestion = StateCommand.Clear; return "Réarmer n'est possible qu'à l'état Stopped. La machine est Aborted : acquittez d'abord."; }
                return "Réarmer n'est possible qu'à l'état Stopped. La machine est " + s + ".";
            case StateCommand.Hold:
                return "Pause n'est possible qu'en Execute. La machine est " + s + ".";
            case StateCommand.Stop:
                return "Arrêter est possible depuis Resetting, Idle, Execute ou Held. La machine est " + s + ".";
            case StateCommand.Clear:
                return "Acquitter n'est possible qu'à l'état Aborted. La machine est " + s + ".";
            default:
                return "Commande indisponible.";
        }
    }

    public static string CommandLabel(StateCommand c) => Res.Get("Cmd." + c);
}
