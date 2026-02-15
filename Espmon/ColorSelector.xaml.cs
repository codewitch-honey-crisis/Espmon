using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.UI;

namespace Espmon;

internal sealed partial class ColorSelector : UserControl
{
    private bool _suppressEvents = false;
    private const int CustomIndex = 0;

    public ColorSelector()
    {
        InitializeComponent();
        InitializeColorComboBox();
    }

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(ColorSelector),
            new PropertyMetadata(Colors.White, SelectedColor_PropertyChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public static readonly DependencyProperty SelectedColorValueProperty =
        DependencyProperty.Register(
            nameof(SelectedColorValue),
            typeof(int),
            typeof(ColorSelector),
            new PropertyMetadata(unchecked((int)0xFFFFFFFF), SelectedColorValue_PropertyChanged));

    public int SelectedColorValue
    {
        get => (int)GetValue(SelectedColorValueProperty);
        set => SetValue(SelectedColorValueProperty, value);
    }

    private static void SelectedColor_PropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorSelector control && !control._suppressEvents)
        {
            var color = (Color)e.NewValue;

            // Sync SelectedColorValue from SelectedColor
            int colorValue = unchecked((int)((uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B));

            control._suppressEvents = true;
            control.SetValue(SelectedColorValueProperty, colorValue);
            control.UpdateControlsFromColor(color);
            control._suppressEvents = false;
        }
    }

    private static void SelectedColorValue_PropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorSelector control && !control._suppressEvents)
        {
            int value = (int)e.NewValue;

            unchecked
            {
                byte a = (byte)((value >> 24) & 0xFF);
                byte r = (byte)((value >> 16) & 0xFF);
                byte g = (byte)((value >> 8) & 0xFF);
                byte b = (byte)(value & 0xFF);
                var color = Color.FromArgb(a, r, g, b);

                control._suppressEvents = true;
                control.SetValue(SelectedColorProperty, color);
                control.UpdateControlsFromColor(color);
                control._suppressEvents = false;
            }
        }
    }

    private void InitializeColorComboBox()
    {
        _suppressEvents = true;

        ColorComboBox.Items.Add("(Custom)");

        foreach (var colorItem in ColorItem.AllColors)
        {
            ColorComboBox.Items.Add(colorItem.DisplayName);
        }

        ColorComboBox.SelectedIndex = CustomIndex;
        _suppressEvents = false;
    }

    private void UpdateControlsFromColor(Color color)
    {
        // Update color picker
        ColorPickerControl.Color = color;

        // Try to find matching predefined color
        int colorValue = unchecked((int)((uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B));
        int matchIndex = -1;

        for (int i = 0; i < ColorItem.AllColors.Length; i++)
        {
            if (ColorItem.AllColors[i].Value == colorValue)
            {
                matchIndex = i + 1;
                break;
            }
        }

        ColorComboBox.SelectedIndex = matchIndex >= 0 ? matchIndex : CustomIndex;
    }

    private void ColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ColorComboBox.SelectedIndex < 0)
            return;

        if (ColorComboBox.SelectedIndex == CustomIndex)
            return;

        int colorItemIndex = ColorComboBox.SelectedIndex - 1;
        var colorItem = ColorItem.AllColors[colorItemIndex];

        // Don't suppress - let the property system handle it
        SelectedColorValue = colorItem.Value;
    }

    private void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_suppressEvents)
            return;

        // Don't suppress - let the property system handle it
        SelectedColor = args.NewColor;
    }
}