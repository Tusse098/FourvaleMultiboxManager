using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace Multibox.App;

public partial class SlotPanel : UserControl
{
    private readonly SlotCardViewModel _card;

    public SlotPanel(SlotCardViewModel card)
    {
        InitializeComponent();
        _card = card;
        DataContext = card;
        Attach(card.Session.View);
        card.Session.ViewReplaced += Swap;
        Header.MouseLeftButtonDown += (_, e) =>
        {
            // Buttons in the header handle their own clicks.
            if (e.OriginalSource is not DependencyObject source || FindButton(source) is null)
            {
                FocusRequested?.Invoke(_card);
            }
        };
    }

    /// <summary>The player clicked the header: focus this slot (and move keyboard focus into its game).</summary>
    public event Action<SlotCardViewModel>? FocusRequested;

    /// <summary>The game view received keyboard focus (e.g. the player clicked into the game).</summary>
    public event Action<SlotCardViewModel>? GameFocused;

    public void FocusGame() => _card.Session.View.Focus();

    /// <summary>The size the game renders at (page viewport). Same for every slot; the tile only scales it.</summary>
    public void SetRenderSize(Size size)
    {
        if (size.Width < 1 || size.Height < 1)
        {
            return;
        }

        if (double.IsNaN(GameHost.Width) || Math.Abs(GameHost.Width - size.Width) > 0.5 || Math.Abs(GameHost.Height - size.Height) > 0.5)
        {
            GameHost.Width = size.Width;
            GameHost.Height = size.Height;
        }
    }

    private void Attach(WebView2CompositionControl view)
    {
        GameHost.Children.Add(view);
        view.GotFocus += OnViewGotFocus;
    }

    private void OnViewGotFocus(object sender, RoutedEventArgs e) => GameFocused?.Invoke(_card);

    private void Swap(WebView2CompositionControl old, WebView2CompositionControl replacement)
    {
        old.GotFocus -= OnViewGotFocus;
        GameHost.Children.Remove(old);
        Attach(replacement);
    }

    private static Button? FindButton(DependencyObject? node)
    {
        while (node is not null and not SlotPanel)
        {
            if (node is Button button)
            {
                return button;
            }

            node = System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }

        return null;
    }
}
