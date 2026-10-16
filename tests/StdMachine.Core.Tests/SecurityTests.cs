using StdMachine.Core;
using StdMachine.Core.Security;
using Xunit;

namespace StdMachine.Core.Tests;

public class SecurityTests
{
    [Fact]
    public void Default_levels_match_the_table_of_doc_02()
    {
        var p = new PermissionService();
        Assert.Equal(0, p.RequiredLevel("machine.run"));
        Assert.Equal(1, p.RequiredLevel("machine.mode"));
        Assert.Equal(2, p.RequiredLevel("project.deploy"));
        Assert.Equal(3, p.RequiredLevel("user.manage"));
        Assert.Equal(1, p.RequiredLevel("quality.release"));
        Assert.Equal(2, p.RequiredLevel("force.test"));
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.All.Select(d => d.Code).Distinct().Count());
        Assert.Equal(35, PermissionCatalog.All.Count);
    }

    [Fact]
    public void Access_is_granted_from_the_required_level_and_refused_below_with_a_reason()
    {
        var p = new PermissionService();
        Assert.True(p.Can("manual.execute", 1).Allowed);
        Assert.True(p.Can("manual.execute", 3).Allowed);
        var refused = p.Can("project.deploy", 1);
        Assert.False(refused.Allowed);
        Assert.Equal(2, refused.RequiredLevel);
        Assert.Contains("Ingénieur process", refused.Reason);
    }

    [Fact]
    public void Abort_is_always_allowed_and_cannot_be_changed()
    {
        var p = new PermissionService();
        Assert.True(p.Can("machine.abort", 0).Allowed);
        Assert.Throws<InvalidOperationException>(() => p.SetLevel("machine.abort", 3));
        Assert.True(p.Can("machine.abort", 0).Allowed);
    }

    [Fact]
    public void An_administrator_can_move_a_right_to_another_level()
    {
        var p = new PermissionService();
        p.SetLevel("machine.mode", 2);
        Assert.False(p.Can("machine.mode", 1).Allowed);
        Assert.True(p.Can("machine.mode", 2).Allowed);
        Assert.Throws<ArgumentOutOfRangeException>(() => p.SetLevel("machine.mode", 12));
        Assert.Throws<ArgumentException>(() => p.SetLevel("inconnu", 1));
    }

    [Fact]
    public void Without_login_the_level_is_0_and_login_with_password_or_pin_sets_the_account_level()
    {
        var now = new DateTime(2026, 10, 16, 8, 0, 0);
        var s = new UserSession(now);
        Assert.Equal(0, s.Level);
        var account = new UserAccount("amartin", "A. Martin", 2, PasswordHasher.Hash("secret!"), PasswordHasher.Hash("4711"), true, false);
        Assert.False(s.LogIn(account, "mauvais", now));
        Assert.Equal(0, s.Level);
        Assert.True(s.LogIn(account, "secret!", now));
        Assert.Equal(2, s.Level);
        s.LogOut();
        Assert.True(s.LogIn(account, "4711", now));
        Assert.False(s.LogIn(account with { Active = false }, "secret!", now));
    }

    [Fact]
    public void Idle_session_falls_back_to_level_0_after_the_timeout_and_a_touch_postpones_it()
    {
        var t0 = new DateTime(2026, 10, 16, 8, 0, 0);
        var s = new UserSession(t0);
        var account = new UserAccount("a", "A", 3, PasswordHasher.Hash("x"), null, true, false);
        s.LogIn(account, "x", t0);
        Assert.False(s.Tick(t0.AddMinutes(9)));
        s.Touch(t0.AddMinutes(9));
        Assert.False(s.Tick(t0.AddMinutes(18)));
        Assert.True(s.Tick(t0.AddMinutes(20)));
        Assert.Equal(0, s.Level);
        Assert.False(s.Tick(t0.AddMinutes(40)));
    }

    [Fact]
    public void Password_hashes_are_salted_and_verify_in_constant_form()
    {
        string a = PasswordHasher.Hash("secret"), b = PasswordHasher.Hash("secret");
        Assert.NotEqual(a, b);
        Assert.StartsWith("100000$", a);
        Assert.True(PasswordHasher.Verify("secret", a));
        Assert.False(PasswordHasher.Verify("Secret", a));
        Assert.False(PasswordHasher.Verify("secret", "invalide"));
    }

    [Fact]
    public void Effective_override_is_the_product_of_all_levels_with_the_doc_example()
    {
        Assert.Equal(40.0, Overrides.Effective(80, 50, 100), 6);
        Assert.Equal(100.0, Overrides.Effective(), 6);
        Assert.Equal(0.0, Overrides.Effective(100, 0, 100), 6);
        Assert.Equal(100.0, Overrides.Effective(250, 100), 6); // plafond à 100 %
    }

    [Fact]
    public void Effective_timeout_is_nominal_divided_by_override_and_suspended_at_zero()
    {
        Assert.Equal(4000.0, Overrides.EffectiveTimeoutMs(2000, 50));
        Assert.Equal(2000.0, Overrides.EffectiveTimeoutMs(2000, 100));
        Assert.Null(Overrides.EffectiveTimeoutMs(2000, 0));
        Assert.True(Overrides.IsStopped(0));
        Assert.False(Overrides.IsStopped(1));
    }
}
