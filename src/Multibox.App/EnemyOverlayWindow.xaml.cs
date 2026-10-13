using System.Collections.ObjectModel;
using System.Globalization;
using Multibox.Core;

namespace Multibox.App;

/// <summary>One enemy in the enemy overlay: only its HP.</summary>
public sealed class EnemyRowViewModel : Observable
{
    private string _hpText = "";
    private double _hpFraction;
    private bool _isAlive = true;

    public string HpText { get => _hpText; private set => Set(ref _hpText, value); }
    public double HpFraction { get => _hpFraction; private set => Set(ref _hpFraction, value); }

    /// <summary>A defeated enemy keeps its row (dimmed), so the others stay where they are.</summary>
    public bool IsAlive { get => _isAlive; private set => Set(ref _isAlive, value); }

    public void Show(EnemyState enemy)
    {
        HpText = string.Create(CultureInfo.CurrentCulture, $"{enemy.Hp:N0} / {enemy.MaxHp:N0}");
        HpFraction = enemy.MaxHp > 0 ? Math.Clamp(enemy.Hp / (double)enemy.MaxHp, 0, 1) : 0;
        IsAlive = enemy.Alive;
    }
}

/// <summary>
/// Enemy overlay: the HP of every enemy in the focused slot's battle, in the game's order. Reads the state
/// store only; when the list is not current there are no rows (never a stale value).
/// </summary>
public sealed class EnemyOverlayViewModel(double opacity, Action close) : OverlayViewModelBase(opacity, close)
{
    public ObservableCollection<EnemyRowViewModel> Rows { get; } = [];

    public bool HasEnemies => Rows.Count > 0;

    /// <param name="focused">The focused slot's state, or null when no slot is focused.</param>
    public void Refresh(SlotState? focused, FreshnessPolicy freshness, DateTimeOffset now)
    {
        var enemies = focused is null ? null : freshness.Current(focused.Character.Enemies, now)?.Value;
        var count = enemies?.Count ?? 0;

        while (Rows.Count > count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        for (var i = 0; i < count; i++)
        {
            if (i == Rows.Count)
            {
                Rows.Add(new EnemyRowViewModel());
            }

            Rows[i].Show(enemies![i]);
        }
    }
}

public partial class EnemyOverlayWindow : OverlayWindowBase
{
    public EnemyOverlayWindow(EnemyOverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        DragBy(Header);
        ResizeBy(Grip);
    }
}
