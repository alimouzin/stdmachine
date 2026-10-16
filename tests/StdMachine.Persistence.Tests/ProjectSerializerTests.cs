using StdMachine.Core;
using StdMachine.Persistence;
using StdMachine.Simulation.Demo;
using Xunit;

namespace StdMachine.Persistence.Tests;

public class ProjectSerializerTests
{
    [Fact]
    public void Demo_project_round_trips_without_loss()
    {
        var json = ProjectSerializer.Serialize(DemoProject.Build());
        var back = ProjectSerializer.Deserialize(json);
        Assert.Equal(json, ProjectSerializer.Serialize(back));
    }

    [Fact]
    public void Sample_file_is_up_to_date_and_round_trips()
    {
        string expected = Samples.ReadOrUpdate(Samples.SamplePath, SampleProject.Build(Samples.Catalog()));
        string onDisk = File.ReadAllText(Samples.SamplePath).Replace("\r\n", "\n");
        Assert.True(expected == onDisk, "samples/MX-4.stdm.json n'est plus à jour : relancer avec STDM_UPDATE_SAMPLES=1.");
        Assert.Equal(onDisk, ProjectSerializer.Serialize(ProjectSerializer.Deserialize(onDisk)));
    }

    [Fact]
    public void Validation_errors_sample_is_up_to_date()
    {
        string expected = Samples.ReadOrUpdate(Samples.ValidationErrorsPath, SampleProject.BuildValidationErrors(Samples.Catalog()));
        Assert.True(expected == File.ReadAllText(Samples.ValidationErrorsPath).Replace("\r\n", "\n"), "Fichier validation-errors à régénérer (STDM_UPDATE_SAMPLES=1).");
    }

    [Fact]
    public void Process_steps_keep_their_type_and_parallel_branches()
    {
        var original = DemoProject.Build();
        var back = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(original));
        var a = original.Stations.SelectMany(s => s.Processes).SelectMany(p => p.AllSteps()).Select(s => s.GetType().Name).ToList();
        var b = back.Stations.SelectMany(s => s.Processes).SelectMany(p => p.AllSteps()).Select(s => s.GetType().Name).ToList();
        Assert.Equal(a, b);
        Assert.Contains("ParallelBlock", a);
        var par = back.Stations.SelectMany(s => s.Processes).SelectMany(p => p.AllSteps()).OfType<ParallelBlock>().First();
        Assert.Equal(original.Stations.SelectMany(s => s.Processes).SelectMany(p => p.AllSteps()).OfType<ParallelBlock>().First().Branches.Count, par.Branches.Count);
    }

    [Fact]
    public void Project_without_schema_version_is_migrated()
    {
        var json = ProjectSerializer.Serialize(DemoProject.Build()).Replace("\"schemaVersion\": 1,", "");
        Assert.DoesNotContain("schemaVersion", json);
        var p = ProjectSerializer.Deserialize(json);
        Assert.Equal(MachineProject.CurrentSchemaVersion, p.SchemaVersion);
    }

    [Fact]
    public void Newer_schema_is_refused()
    {
        var json = ProjectSerializer.Serialize(DemoProject.Build()).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(json));
    }

    [Fact]
    public void Save_then_load_leaves_no_temporary_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "stdm-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "project.stdm.json");
        try
        {
            ProjectSerializer.Save(DemoProject.Build(), path);
            ProjectSerializer.Save(DemoProject.Build(), path);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Equal("Ligne d'assemblage MX-4", ProjectSerializer.Load(path).Name);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
