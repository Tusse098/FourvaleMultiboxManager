using System.Windows;

namespace Multibox.App;

/// <summary>
/// Places slot panels so every game area has the game's own aspect ratio (Fourvale renders 1920×1080 with
/// Phaser Scale.FIT, so any other shape shows bars inside the game). Leftover space goes to the window edges,
/// not into every tile. Pure geometry, unit-tested.
/// </summary>
public static class LayoutPlanner
{
    /// <param name="Panels">Panel rectangles (including header and edge), in slot order; in Focus layout the large one is first.</param>
    /// <param name="RenderSize">The game-area size every slot renders at (the largest game area); smaller tiles scale it down.</param>
    public sealed record Plan(IReadOnlyList<Rect> Panels, Size RenderSize);

    /// <param name="count">Number of open slots.</param>
    /// <param name="focus">Focus layout (one large, others in a column or row) instead of an even grid.</param>
    /// <param name="available">Space for all panels.</param>
    /// <param name="aspect">Game width / height (16/9).</param>
    /// <param name="chrome">Per-panel space that is not game: edge on both sides and header.</param>
    /// <param name="maxTileShare">Focus layout: the small tiles may use at most this share of the width (column) or height (row).</param>
    public static Plan Arrange(int count, bool focus, Size available, double aspect, Size chrome, double maxTileShare)
    {
        if (count <= 0 || available.Width <= chrome.Width || available.Height <= chrome.Height)
        {
            return new Plan([], new Size(1, 1));
        }

        return focus && count > 1
            ? ArrangeFocus(count, available, aspect, chrome, maxTileShare)
            : ArrangeGrid(count, available, aspect, chrome);
    }

    /// <summary>The grid shape (columns × rows) that gives each game the largest area.</summary>
    private static Plan ArrangeGrid(int count, Size available, double aspect, Size chrome)
    {
        var best = (Columns: 1, Rows: count, Game: new Size(0, 0));
        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (int)Math.Ceiling(count / (double)columns);
            var game = Fit(available.Width / columns - chrome.Width, available.Height / rows - chrome.Height, aspect);
            if (game.Width > best.Game.Width)
            {
                best = (columns, rows, game);
            }
        }

        var tile = new Size(best.Game.Width + chrome.Width, best.Game.Height + chrome.Height);
        var top = (available.Height - best.Rows * tile.Height) / 2;
        var panels = new List<Rect>();
        for (var row = 0; row < best.Rows; row++)
        {
            // A last, partly filled row is centred too.
            var inRow = Math.Min(best.Columns, count - row * best.Columns);
            var left = (available.Width - inRow * tile.Width) / 2;
            for (var column = 0; column < inRow; column++)
            {
                panels.Add(new Rect(left + column * tile.Width, top + row * tile.Height, tile.Width, tile.Height));
            }
        }

        return new Plan(panels, best.Game);
    }

    /// <summary>
    /// One large panel with the others beside it: in a column on its right (wide screens) or in a row below it
    /// (16:10, 4:3, portrait), whichever gives the larger main game. The block is centred.
    /// </summary>
    private static Plan ArrangeFocus(int count, Size available, double aspect, Size chrome, double maxTileShare)
    {
        var column = FocusWithColumn(count - 1, available, aspect, chrome, maxTileShare);
        var row = FocusWithRow(count - 1, available, aspect, chrome, maxTileShare);
        return row.RenderSize.Width > column.RenderSize.Width ? row : column;
    }

    private static Plan FocusWithColumn(int small, Size available, double aspect, Size chrome, double maxTileShare)
    {
        // Small tiles: as tall as an even share of the height allows, but no wider than the allowed column share.
        var smallGame = Fit(available.Width * maxTileShare - chrome.Width, available.Height / small - chrome.Height, aspect);
        var smallTile = new Size(smallGame.Width + chrome.Width, smallGame.Height + chrome.Height);

        var bigGame = Fit(available.Width - smallTile.Width - chrome.Width, available.Height - chrome.Height, aspect);
        var bigTile = new Size(bigGame.Width + chrome.Width, bigGame.Height + chrome.Height);

        var left = (available.Width - bigTile.Width - smallTile.Width) / 2;
        var panels = new List<Rect> { new(left, (available.Height - bigTile.Height) / 2, bigTile.Width, bigTile.Height) };
        var columnTop = (available.Height - small * smallTile.Height) / 2;
        for (var i = 0; i < small; i++)
        {
            panels.Add(new Rect(left + bigTile.Width, columnTop + i * smallTile.Height, smallTile.Width, smallTile.Height));
        }

        return new Plan(panels, bigGame);
    }

    private static Plan FocusWithRow(int small, Size available, double aspect, Size chrome, double maxTileShare)
    {
        // Small tiles: as wide as an even share of the width allows, but no taller than the allowed row share.
        var smallGame = Fit(available.Width / small - chrome.Width, available.Height * maxTileShare - chrome.Height, aspect);
        var smallTile = new Size(smallGame.Width + chrome.Width, smallGame.Height + chrome.Height);

        var bigGame = Fit(available.Width - chrome.Width, available.Height - smallTile.Height - chrome.Height, aspect);
        var bigTile = new Size(bigGame.Width + chrome.Width, bigGame.Height + chrome.Height);

        var top = (available.Height - bigTile.Height - smallTile.Height) / 2;
        var panels = new List<Rect> { new((available.Width - bigTile.Width) / 2, top, bigTile.Width, bigTile.Height) };
        var rowLeft = (available.Width - small * smallTile.Width) / 2;
        for (var i = 0; i < small; i++)
        {
            panels.Add(new Rect(rowLeft + i * smallTile.Width, top + bigTile.Height, smallTile.Width, smallTile.Height));
        }

        return new Plan(panels, bigGame);
    }

    /// <summary>The largest width×height with the given aspect inside the box.</summary>
    private static Size Fit(double width, double height, double aspect)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        return width / height > aspect ? new Size(height * aspect, height) : new Size(width, width / aspect);
    }
}
