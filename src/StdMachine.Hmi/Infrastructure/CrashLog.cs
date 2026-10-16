using System.IO;

namespace StdMachine.Hmi.Infrastructure;

/// <summary>Journal minimal des exceptions non gérées (le PoC n'a pas Serilog) : crash.log à côté de l'exécutable.</summary>
public static class CrashLog
{
    public static void Write(Exception? ex)
    {
        try
        {
            var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
        }
        catch (IOException) { }
    }
}

/// <summary>Trace de diagnostic (active si la variable d'environnement STDMACHINE_TRACE est définie).</summary>
public static class Trace
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("STDMACHINE_TRACE") != null;

    public static void Line(string text)
    {
        if (!Enabled) return;
        try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trace.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine); }
        catch (IOException) { }
    }
}
