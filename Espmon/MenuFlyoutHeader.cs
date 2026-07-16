using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Espmon
{
    /// <summary>
    /// A non-interactive text label for grouping items inside a <see cref="MenuFlyout"/>.
    /// </summary>
    /// <remarks>
    /// Derives from <see cref="MenuFlyoutSeparator"/> because that is a
    /// <c>MenuFlyoutItemBase</c> the flyout will accept, and it is already excluded from
    /// focus and keyboard traversal — a header must not be tab-stoppable or arrow-navigable.
    /// <para>
    /// This control deliberately does NOT set <c>DefaultStyleKey</c>, so it needs no
    /// Themes/Generic.xaml entry. The style is assigned explicitly at construction time
    /// (see <c>QuerySelector.MakeHeader</c>). That matters: a MenuFlyout renders in a popup
    /// outside the UserControl's visual tree, so an implicit style declared in
    /// <c>UserControl.Resources</c> would not reach it. An explicitly assigned Style always does.
    /// </para>
    /// </remarks>
    public sealed partial class MenuFlyoutHeader : MenuFlyoutSeparator
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(MenuFlyoutHeader),
                new PropertyMetadata(string.Empty));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public MenuFlyoutHeader()
        {
            IsTabStop = false;
            IsEnabled = false; // belt-and-braces: keeps it out of the focus order entirely.
                               // The template ignores the disabled visual state, so it does not gray out.
        }
    }
}