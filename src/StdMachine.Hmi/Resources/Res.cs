using System.Reflection;
using System.Resources;
using System.Windows.Markup;

namespace StdMachine.Hmi.Resources;

/// <summary>Accès aux textes de l'interface (Resources/Strings.resx, français).</summary>
public static class Res
{
    private static readonly ResourceManager Manager =
        new ResourceManager("StdMachine.Hmi.Resources.Strings", Assembly.GetExecutingAssembly());

    public static string Get(string key)
    {
        try { return Manager.GetString(key, System.Globalization.CultureInfo.InvariantCulture) ?? "⟦" + key + "⟧"; }
        catch (MissingManifestResourceException) { return "⟦" + key + "⟧"; }
    }

    public static string Format(string key, params object[] args) => string.Format(StdMachine.Core.DisplayFormat.Culture, Get(key), args);
}

/// <summary>Extension XAML : <c>Text="{res:T Machine.Reset}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { Key = string.Empty; }
    public TExtension(string key) { Key = key; }
    [ConstructorArgument("key")] public string Key { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider) => Res.Get(Key);
}
