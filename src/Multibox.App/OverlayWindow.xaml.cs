using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Multibox.App;

/// <summary>Party overlay: a compact row per open slot, read from the state store via the slot cards.</summary>
public sealed class OverlayViewModel : OverlayViewModelBase
{
    private bool _isMinimized;
    private bool _onlyBattleRows;
    private string _summary = "";

    public OverlayViewModel(ObservableCollection<SlotCardViewModel> cards, double opacity, Action close, Action changed)
        : base(opacity, close)
    {
        Cards = cards;
        ToggleMinimized = new RelayCommand(() =>
        {
            IsMinimized = !IsMinimized;
            changed();
        });
    }

    public ObservableCollection<SlotCardViewModel> Cards { get; }

    /// <summary>Hide rows of characters that are not in a battle.</summary>
    public bool OnlyBattleRows { get => _onlyBattleRows; set => Set(ref _onlyBattleRows, value); }

    public ICommand ToggleMinimized { get; }

    public bool IsMinimized
    {
        get => _isMinimized;
        set
        {
            if (Set(ref _isMinimized, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(MinimizeGlyph));
            }
        }
    }

    public bool IsExpanded => !IsMinimized;

    public string MinimizeGlyph => IsMinimized ? "▢" : "–";

    /// <summary>One line for the header, so the minimised overlay still says who needs attention.</summary>
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public void Refresh()
    {
        var ready = Cards.Where(c => c.IsReady).Select(c => c.Session.Id.Number).ToList();
        var inBattle = Cards.Count(c => c.IsInBattle);
        var problems = Cards.Count(c => c.IsProblem);
        var parts = new List<string>();
        if (ready.Count > 0) parts.Add($"READY: {string.Join(", ", ready)}");
        if (inBattle > 0) parts.Add(string.Create(CultureInfo.CurrentCulture, $"{inBattle} in battle"));
        if (problems > 0) parts.Add(string.Create(CultureInfo.CurrentCulture, $"{problems} with problems"));
        Summary = parts.Count > 0 ? string.Join(" · ", parts) : $"{Cards.Count} slots";
    }
}

public partial class OverlayWindow : OverlayWindowBase
{
    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        DragBy(Header);
        ResizeBy(Grip);
    }

    /// <summary>The player clicked a character row: focus that slot.</summary>
    public event Action<SlotCardViewModel>? RowClicked;

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SlotCardViewModel card })
        {
            RowClicked?.Invoke(card);
        }
    }
}
