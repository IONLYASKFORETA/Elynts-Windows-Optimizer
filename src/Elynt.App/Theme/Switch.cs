using System.Windows;

namespace Elynt.App.Theme;

/// <summary>
/// What a <c>Switch</c> (Theme.xaml's on/off CheckBox) needs to know beyond being disabled: whether it is locked, so it
/// shows a lock and its state rather than fading like a switch whose change is still running.
/// </summary>
public static class Switch
{
    /// <summary>The switch can't be flipped either way, and the page says why (a tweak's <c>IsLocked</c>).</summary>
    public static readonly DependencyProperty IsLockedProperty = DependencyProperty.RegisterAttached(
        "IsLocked", typeof(bool), typeof(Switch), new PropertyMetadata(false));

    public static bool GetIsLocked(DependencyObject element) => (bool)element.GetValue(IsLockedProperty);

    public static void SetIsLocked(DependencyObject element, bool value) => element.SetValue(IsLockedProperty, value);
}
