using System.Text;
using StdMachine.Core;

namespace StdMachine.Simulation.Demo;

/// <summary>Résumé texte du scénario (option --dump-demo) : sert à vérifier que le scénario place bien ses événements.</summary>
public static class DemoDump
{
    public static string Run()
    {
        var project = DemoProject.Build();
        var script = new DemoScript(project);
        var sb = new StringBuilder();
        sb.AppendLine("Défaut Pressage 01 à " + script.AbortAt.ToString("0.0"));
        sb.AppendLine("Produits : " + script.Flow.Products.Count);
        foreach (var v in script.Flow.Visits.OrderBy(v => v.Start))
            sb.AppendLine("P" + v.Product + " mover " + v.Mover + " st" + v.Station + " " + v.Operation + " arr " + v.Arrive.ToString("0.0") + " deb " + v.Start.ToString("0.0")
                + " fin " + v.End.ToString("0.0") + " dep " + v.Leave.ToString("0.0") + (v.Aborted ? " ABORT" : "") + (v.WaitExtra > 0 ? " ATTENTE" : ""));
        foreach (double t in new[] { 0.0, 5, 10, 30, 45, 60, 75, 90, 100, 120, 142 })
        {
            var s = script.Evaluate(t, DateTime.Now, PlcConnectionState.Demo, new DemoUserState());
            sb.AppendLine("--- T=" + t + " machine " + s.Machine.State + " alarmes " + s.Alarms.Count + " OF " + s.Orders.Current?.Good + "/" + s.Orders.Current?.Quantity);
            foreach (var st in s.Stations)
                sb.AppendLine("  " + st.Name + " " + st.State + " " + (st.Operation ?? "-") + " " + st.OperationElapsedSeconds.ToString("0.0") + "/" + st.OperationEstimatedSeconds.ToString("0.0")
                    + " cartes en cours : " + string.Join(", ", st.Steps.Where(r => r.State == StepRunState.Running || r.State == StepRunState.WaitingCondition || r.State == StepRunState.Error || r.State == StepRunState.Retry).Select(r => r.State.ToString())));
            sb.AppendLine("  movers : " + string.Join(" ", s.Movers.Select(m => m.Number + ":" + m.State + "@" + (int)m.X + "," + (int)m.Y)));
        }
        return sb.ToString();
    }
}
