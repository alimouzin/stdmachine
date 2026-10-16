using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StdMachine.Hmi.ViewModels;

/// <summary>Élément de l'essai tactile : un nom d'action et l'objet sur lequel elle porte.</summary>
public sealed class TestItem : ObservableObject
{
    public TestItem(string name, string objectName, string icon)
    {
        Name = name; ObjectName = objectName; Icon = icon;
    }

    public string Name { get; }
    /// <summary>Objet utilisé : un même objet ne peut pas être dans deux branches parallèles.</summary>
    public string ObjectName { get; }
    public string Icon { get; }

    public TestItem Clone() => new TestItem(Name, ObjectName, Icon);
}

/// <summary>Système › Essai tactile : palette, liste réordonnable, deux branches parallèles, zone refusée, défilement automatique.</summary>
public sealed class DragDropTestViewModel : ViewModelBase
{
    public DragDropTestViewModel()
    {
        Palette = new ObservableCollection<TestItem>
        {
            new TestItem("Chercher pièce", "Vision", "center_focus_strong"),
            new TestItem("Obtenir pièce", "Vision", "center_focus_strong"),
            new TestItem("Prendre pièce", "Robot", "precision_manufacturing"),
            new TestItem("Poser pièce", "Robot", "precision_manufacturing"),
            new TestItem("Aller à", "Robot", "precision_manufacturing"),
            new TestItem("Exécuter recette", "Presse", "compress"),
            new TestItem("Aspirer", "Aspiration", "air"),
            new TestItem("Vibrer", "Chargeur", "vibration"),
        };
        Sequence = new ObservableCollection<TestItem>
        {
            new TestItem("Chercher pièce", "Vision", "center_focus_strong"),
            new TestItem("Prendre pièce", "Robot", "precision_manufacturing"),
            new TestItem("Poser pièce", "Robot", "precision_manufacturing"),
        };
        BranchA = new ObservableCollection<TestItem>();
        BranchB = new ObservableCollection<TestItem>();
        Long = new ObservableCollection<TestItem>();
        for (int i = 1; i <= 24; i++)
            Long.Add(new TestItem("Étape " + i, "Objet " + i, "swap_vert"));
    }

    public ObservableCollection<TestItem> Palette { get; }
    public ObservableCollection<TestItem> Sequence { get; }
    public ObservableCollection<TestItem> BranchA { get; }
    public ObservableCollection<TestItem> BranchB { get; }
    public ObservableCollection<TestItem> Long { get; }
}
