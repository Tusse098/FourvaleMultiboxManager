using Multibox.Core;

namespace Multibox.Core.Tests;

public class FreshnessPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly FreshnessPolicy Policy = new(new Dictionary<string, TimeSpan>
    {
        [FreshnessPolicy.DefaultKey] = TimeSpan.FromSeconds(5),
        ["slow-source"] = TimeSpan.FromMinutes(10),
    });

    [Fact]
    public void Field_is_fresh_up_to_its_source_limit()
    {
        var field = new Field<int>(42, T0, "room-state");

        Assert.True(Policy.IsFresh(field, T0.AddSeconds(5)));
        Assert.False(Policy.IsFresh(field, T0.AddSeconds(5.1)));
        Assert.Null(Policy.Current(field, T0.AddSeconds(6)));
    }

    [Fact]
    public void Each_source_has_its_own_limit()
    {
        var field = new Field<int>(42, T0, "slow-source");

        Assert.True(Policy.IsFresh(field, T0.AddMinutes(9)));
        Assert.Equal(TimeSpan.FromMinutes(10), Policy.LimitFor("slow-source"));
        Assert.Equal(TimeSpan.FromSeconds(5), Policy.LimitFor("anything-else"));
    }

    [Fact]
    public void Missing_field_is_never_fresh()
    {
        Assert.False(Policy.IsFresh<int>(null, T0));
    }

    [Fact]
    public void Config_without_default_or_with_non_positive_limits_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new FreshnessPolicy(new Dictionary<string, TimeSpan> { ["x"] = TimeSpan.FromSeconds(1) }));
        Assert.Throws<ArgumentException>(() => new FreshnessPolicy(new Dictionary<string, TimeSpan> { [FreshnessPolicy.DefaultKey] = TimeSpan.Zero }));
    }
}

public class StateStoreTests
{
    [Fact]
    public void Update_changes_only_the_given_slot_and_reports_it()
    {
        var store = new StateStore();
        var changed = new List<SlotId>();
        store.Changed += changed.Add;

        store.Update(new SlotId(2), s => s.Character.Level = new Field<int>(5, DateTimeOffset.UtcNow, "test"));

        Assert.Equal([new SlotId(2)], changed);
        Assert.Equal(5, store.Get(new SlotId(2)).Character.Level!.Value);
        Assert.Null(store.Get(new SlotId(1)).Character.Level);
    }

    [Fact]
    public void Reset_forgets_a_slot()
    {
        var store = new StateStore();
        store.Update(new SlotId(1), s => s.Health = AdapterHealth.Ok);

        store.Reset(new SlotId(1));

        Assert.Equal(AdapterHealth.Waiting, store.Get(new SlotId(1)).Health);
    }
}

public class LogTests
{
    [Fact]
    public void Every_message_is_redacted_and_exception_messages_are_not_written()
    {
        var path = Path.Combine(Path.GetTempPath(), "multibox-tests", $"{Guid.NewGuid():N}.log");
        using (var log = new Log(path, s => s.Replace("secret", "<REDACTED>")))
        {
            log.Info("token secret here");
            log.Error("decode failed", new FormatException("payload with secret text"));
        }

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("secret", text);
        Assert.Contains("decode failed (FormatException)", text);
        File.Delete(path);
    }
}
