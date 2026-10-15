using Hmi.Mvvm;

namespace Hmi.ViewModels;

/// <summary>Une page de la navigation latérale.</summary>
public abstract class PageViewModel : ViewModelBase
{
    protected PageViewModel(string title) => Title = title;

    public string Title { get; }
}
