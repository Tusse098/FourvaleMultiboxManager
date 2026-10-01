using Fourvale.Adapter.Colyseus;

namespace Fourvale.Adapter.Tests.Colyseus;

/// <summary>Real handshake captured from Fourvale 0.98 on 2026-10-01 (see tests/fixtures/colyseus/README.md).</summary>
public class HandshakeFixtureTests
{
    private static SchemaContext Load() =>
        SchemaContext.FromHandshake(Convert.FromBase64String(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "colyseus", "handshake-2026-10-01.b64")).Trim()));

    [Fact]
    public void Town_room_root_has_a_map_of_players()
    {
        var context = Load();

        Assert.Equal(6, context.Types.Count);
        Assert.Equal(3, context.RootTypeId);
        Assert.Equal(["mapId: string", "players: map<ref<2>>"], context.RootType.Fields.Select(f => f.ToString()));
    }

    [Fact]
    public void Player_type_has_class_and_level()
    {
        var player = Load().GetType(2);

        Assert.Equal("classId: string", player.Fields[player.IndexOf("classId")].ToString());
        Assert.Equal("level: int16", player.Fields[player.IndexOf("level")].ToString());
    }

    [Fact]
    public void Battle_combatant_type_has_hp_and_action_meter()
    {
        var context = Load();
        var battle = context.GetType(1);
        var combatant = context.GetType(0);

        Assert.Equal("combatants: map<ref<0>>", battle.Fields[0].ToString());
        Assert.Equal(
            ["hp: int32", "maxHp: int32", "sp: int32", "maxSp: int32", "actionMeter: number", "attackRateMs: number", "alive: boolean"],
            new[] { "hp", "maxHp", "sp", "maxSp", "actionMeter", "attackRateMs", "alive" }
                .Select(n => combatant.Fields[combatant.IndexOf(n)].ToString()));
    }
}
