using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace Multibox.App;

/// <summary>
/// Attached <c>SmoothProgress.Value</c> for a <see cref="RangeBase"/> (progress bar): a rise glides linearly to the new
/// value over one read interval, a drop (action timer reset, damage) shows at once. The bar only moves between values
/// actually read, never ahead of them, so a paused timer (party attack animation) stays put instead of overshooting.
/// </summary>
public static class SmoothProgress
{
    /// <summary>How long a rise takes; set from the read interval at startup so the bar arrives as the next value comes in.</summary>
    public static Duration Duration { get; set; } = new(TimeSpan.FromMilliseconds(250));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(double), typeof(SmoothProgress), new PropertyMetadata(0.0, OnValueChanged));

    public static double GetValue(DependencyObject element) => (double)element.GetValue(ValueProperty);

    public static void SetValue(DependencyObject element, double value) => element.SetValue(ValueProperty, value);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RangeBase bar)
        {
            return;
        }

        var target = (double)e.NewValue;
        if (target >= bar.Value && bar.IsVisible)
        {
            bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(target, Duration), HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            bar.BeginAnimation(RangeBase.ValueProperty, null);
            bar.Value = target;
        }
    }
}
