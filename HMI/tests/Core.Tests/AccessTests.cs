using Core.Access;

namespace Core.Tests;

public class AccessTests
{
    private sealed class MemoryStore : IUserStore
    {
        public AccessSettings Settings = AccessSettings.CreateDefault();
        public int Saves;
        public AccessSettings Load() => Settings;
        public void Save(AccessSettings settings) { Settings = settings; Saves++; }
    }

    private static (AccessPolicy Policy, MemoryStore Store, User Admin, User Operator) Create()
    {
        var store = new MemoryStore();
        var policy = new AccessPolicy(store);
        return (policy, store, policy.Login("Admin", "0000")!, policy.Login("Opérateur", "1111")!);
    }

    [Fact]
    public void Login_requires_matching_name_and_pin()
    {
        var (p, _, _, _) = Create();
        Assert.NotNull(p.Login("Admin", "0000"));
        Assert.Null(p.Login("Admin", "1111"));
        Assert.Null(p.Login("Inconnu", "0000"));
    }

    [Fact]
    public void Nobody_logged_in_can_do_nothing()
    {
        var (p, _, _, _) = Create();
        Assert.All(Enum.GetValues<ActionType>(), a => Assert.False(p.CanDo(null, a)));
    }

    [Fact]
    public void Default_levels_follow_required_ranks()
    {
        var (p, _, admin, op) = Create();
        Assert.True(p.CanDo(op, ActionType.Command));
        Assert.True(p.CanDo(op, ActionType.Abort));
        Assert.False(p.CanDo(op, ActionType.ManualAction));
        Assert.False(p.CanDo(op, ActionType.UserAdmin));
        Assert.True(p.CanDo(admin, ActionType.UserAdmin));
        Assert.True(p.CanDo(admin, ActionType.ManualAction));
    }

    [Fact]
    public void Admin_can_change_the_level_required_for_an_action()
    {
        var (p, store, admin, op) = Create();
        var saves = store.Saves;

        Assert.True(p.SetRequiredRank(admin, ActionType.ManualAction, 0));
        Assert.True(p.CanDo(op, ActionType.ManualAction));
        Assert.Equal(saves + 1, store.Saves);

        Assert.True(p.SetRequiredRank(admin, ActionType.Command, 2));
        Assert.False(p.CanDo(op, ActionType.Command));
    }

    [Fact]
    public void Non_admin_cannot_modify_anything()
    {
        var (p, store, _, op) = Create();
        var saves = store.Saves;

        Assert.False(p.SetRequiredRank(op, ActionType.Command, 3));
        Assert.False(p.AddLevel(op, "Test"));
        Assert.False(p.AddUser(op, "Paul", "1", 0));
        Assert.False(p.RemoveUser(op, "Régleur"));
        Assert.False(p.SetRequiredRank(null, ActionType.Command, 3));
        Assert.Equal(saves, store.Saves);
        Assert.True(p.CanDo(op, ActionType.Command));
    }

    [Fact]
    public void Required_rank_must_target_an_existing_level()
    {
        var (p, _, admin, _) = Create();
        Assert.False(p.SetRequiredRank(admin, ActionType.Command, 42));
    }

    [Fact]
    public void Admin_cannot_lock_himself_out_of_administration()
    {
        var (p, _, _, _) = Create();
        var process = p.Login("Process", "3333")!;
        Assert.False(p.SetRequiredRank(p.Login("Admin", "0000"), ActionType.UserAdmin, 99));
        // Un utilisateur Process, une fois admin, ne peut pas relever le seuil au-dessus de lui.
        p.SetRequiredRank(p.Login("Admin", "0000"), ActionType.UserAdmin, 2);
        Assert.False(p.SetRequiredRank(process, ActionType.UserAdmin, 3));
    }

    [Fact]
    public void Levels_can_be_added_renamed_and_removed_when_unused()
    {
        var (p, _, admin, _) = Create();

        Assert.True(p.AddLevel(admin, "Maintenance"));
        var added = p.Levels.Last();
        Assert.Equal(4, added.Rank);

        Assert.True(p.RenameLevel(admin, 4, "Technicien"));
        Assert.Equal("Technicien", p.LevelName(4));

        Assert.True(p.RemoveLevel(admin, 4));
        Assert.DoesNotContain(p.Levels, l => l.Rank == 4);
    }

    [Fact]
    public void Level_in_use_or_duplicated_is_refused()
    {
        var (p, _, admin, _) = Create();
        Assert.False(p.AddLevel(admin, "Admin"));
        Assert.False(p.RenameLevel(admin, 0, "Admin"));
        Assert.False(p.RemoveLevel(admin, 0)); // utilisé par un utilisateur
        p.AddLevel(admin, "Libre");
        p.SetRequiredRank(admin, ActionType.OrderEdit, 4);
        Assert.False(p.RemoveLevel(admin, 4)); // requis par une action
    }

    [Fact]
    public void Users_can_be_added_changed_and_removed()
    {
        var (p, _, admin, _) = Create();
        Assert.True(p.AddUser(admin, "Paul", "4321", 1));
        Assert.Equal(1, p.Login("Paul", "4321")!.LevelRank);
        Assert.False(p.AddUser(admin, "Paul", "9", 0)); // doublon

        Assert.True(p.SetUserLevel(admin, "Paul", 2));
        Assert.Equal(2, p.Login("Paul", "4321")!.LevelRank);

        Assert.True(p.RemoveUser(admin, "Paul"));
        Assert.Null(p.Login("Paul", "4321"));
    }

    [Fact]
    public void Last_administrator_cannot_be_removed_or_demoted()
    {
        var (p, _, admin, _) = Create();
        Assert.False(p.RemoveUser(admin, "Admin"));
        Assert.False(p.SetUserLevel(admin, "Admin", 0));
        Assert.Equal(3, p.Login("Admin", "0000")!.LevelRank);
    }

    [Fact]
    public void Json_store_round_trips_and_falls_back_to_defaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stdmachine-tests-" + Guid.NewGuid());
        var path = Path.Combine(dir, "access.json");
        try
        {
            var first = new AccessPolicy(new JsonUserStore(path)); // fichier absent : défauts
            var admin = first.Login("Admin", "0000")!;
            first.AddUser(admin, "Paul", "4321", 1);
            first.SetRequiredRank(admin, ActionType.OrderEdit, 1);

            var second = new AccessPolicy(new JsonUserStore(path));
            Assert.NotNull(second.Login("Paul", "4321"));
            Assert.Equal(1, second.RequiredRank(ActionType.OrderEdit));

            File.WriteAllText(path, "{ pas du json");
            var third = new AccessPolicy(new JsonUserStore(path));
            Assert.Null(third.Login("Paul", "4321"));
            Assert.NotNull(third.Login("Admin", "0000"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
