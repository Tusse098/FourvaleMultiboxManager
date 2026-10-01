using System.Windows;
using Multibox.App;

namespace Multibox.App.Tests;

public class LayoutPlannerTests
{
    private const double Aspect = 16.0 / 9.0;
    private static readonly Size Chrome = new(4, 26);

    private static Size Game(Rect panel) => new(panel.Width - Chrome.Width, panel.Height - Chrome.Height);

    private static void AssertGameIs16By9(Rect panel) => Assert.Equal(Aspect, Game(panel).Width / Game(panel).Height, 3);

    private static void AssertInside(Rect panel, Size available)
    {
        Assert.True(panel.Left >= -0.01 && panel.Top >= -0.01, $"{panel} starts outside");
        Assert.True(panel.Right <= available.Width + 0.01 && panel.Bottom <= available.Height + 0.01, $"{panel} ends outside");
    }

    [Theory]
    [InlineData(1, 1920, 1080)]
    [InlineData(2, 1920, 1080)]
    [InlineData(3, 3458, 1380)]  // the player's ultrawide screen
    [InlineData(4, 3458, 1380)]
    [InlineData(5, 2560, 1400)]
    public void Grid_tiles_are_exactly_the_game_shape_and_fit(int count, double width, double height)
    {
        var available = new Size(width, height);

        var plan = LayoutPlanner.Arrange(count, focus: false, available, Aspect, Chrome, 0.3);

        Assert.Equal(count, plan.Panels.Count);
        Assert.All(plan.Panels, p => { AssertGameIs16By9(p); AssertInside(p, available); });
        Assert.All(plan.Panels, p => Assert.Equal(plan.RenderSize.Width, Game(p).Width, 3)); // grid: all the same size
    }

    [Fact]
    public void Grid_picks_the_shape_with_the_largest_games()
    {
        // 4 slots on 16:9: 2×2 beats 4×1 and 1×4.
        var plan = LayoutPlanner.Arrange(4, false, new Size(1920, 1080), Aspect, Chrome, 0.3);

        Assert.Equal(2, plan.Panels.Select(p => p.Top).Distinct().Count());
        Assert.True(plan.RenderSize.Width > 900);
    }

    [Fact]
    public void Panels_do_not_overlap()
    {
        foreach (var focus in new[] { false, true })
        {
            var plan = LayoutPlanner.Arrange(5, focus, new Size(3458, 1380), Aspect, Chrome, 0.3);
            for (var i = 0; i < plan.Panels.Count; i++)
            {
                for (var j = i + 1; j < plan.Panels.Count; j++)
                {
                    var overlap = Rect.Intersect(plan.Panels[i], plan.Panels[j]);
                    Assert.True(overlap.IsEmpty || overlap.Width < 0.01 || overlap.Height < 0.01, $"{i} and {j} overlap ({focus})");
                }
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Focus_has_one_large_game_and_smaller_games_all_in_the_game_shape(int count)
    {
        var available = new Size(3458, 1380);

        var plan = LayoutPlanner.Arrange(count, focus: true, available, Aspect, Chrome, 0.3);

        Assert.Equal(count, plan.Panels.Count);
        Assert.All(plan.Panels, p => { AssertGameIs16By9(p); AssertInside(p, available); });
        Assert.Equal(plan.RenderSize.Width, Game(plan.Panels[0]).Width, 3);
        Assert.All(plan.Panels.Skip(1), p => Assert.True(Game(p).Width < plan.RenderSize.Width));
        Assert.All(plan.Panels.Skip(1), p => Assert.True(p.Width <= available.Width * 0.3 + 0.01));
    }

    [Fact]
    public void Nothing_to_place_gives_an_empty_plan()
    {
        Assert.Empty(LayoutPlanner.Arrange(0, false, new Size(1920, 1080), Aspect, Chrome, 0.3).Panels);
        Assert.Empty(LayoutPlanner.Arrange(3, false, new Size(2, 2), Aspect, Chrome, 0.3).Panels);
    }
}
