using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Espmon;

public sealed partial class ScreenEditor : UserControl
{
    void CollapseAll()
    {
        topLabelEditorPanel.Visibility = Visibility.Collapsed;
        topValue1EditorPanel.Visibility = Visibility.Collapsed;
        topValue2EditorPanel.Visibility = Visibility.Collapsed;
        topValue1BarEditorPanel.Visibility = Visibility.Collapsed;
        topValue2BarEditorPanel.Visibility = Visibility.Collapsed;

        bottomLabelEditorPanel.Visibility = Visibility.Collapsed;
        bottomValue1EditorPanel.Visibility = Visibility.Collapsed;
        bottomValue2EditorPanel.Visibility = Visibility.Collapsed;
        bottomValue1BarEditorPanel.Visibility = Visibility.Collapsed;
        bottomValue2BarEditorPanel.Visibility = Visibility.Collapsed;
    }
    public ScreenEditor()
    {
        InitializeComponent();
        screenView.ScalingMode = ScreenViewScalingMode.None;
        _suppressChange = true;
        screenPartComboBox.SelectedIndex = 0;
        _suppressChange = false;
        CollapseAll();
        Screen = Screen.Default;
    }
    public static readonly DependencyProperty ScreenProperty =
    DependencyProperty.Register(
        nameof(Screen),
        typeof(Screen),
        typeof(ScreenView),
        new PropertyMetadata(null, OnScreenChanged));

    public Screen? Screen
    {
        get => (Screen?)GetValue(ScreenProperty);
        set => SetValue(ScreenProperty, value);
    }
    

    private static void OnScreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScreenEditor editor)
        {
            // Unsubscribe from old screen tree
            if (e.OldValue is Screen oldScreen)
            {
                
            }

            // Subscribe to new screen tree
            if (e.NewValue is Screen newScreen)
            {
                editor.screenView.Screen = newScreen;
            }
        }
    }
    public void Refresh()
    {
        screenView.Refresh();
    }
    private bool _suppressChange = false;
    private void SetHit(ScreenViewHitType hitType)
    {
        CollapseAll();
        switch (hitType)
        {
            case ScreenViewHitType.None: break;
            case ScreenViewHitType.TopLabel:
                topLabelEditorPanel.Visibility = Visibility.Visible;
                break;
            case ScreenViewHitType.BottomLabel:
                bottomLabelEditorPanel.Visibility = Visibility.Visible;
                break;
            case ScreenViewHitType.TopValue1:
                topValue1EditorPanel.Visibility = Visibility.Visible;
                break;
            case ScreenViewHitType.TopValue2:
                topValue2EditorPanel.Visibility = Visibility.Visible;
                break;
            case ScreenViewHitType.BottomValue1:
                bottomValue1EditorPanel.Visibility = Visibility.Visible;
                break;
            case ScreenViewHitType.BottomValue2:
                bottomValue2EditorPanel.Visibility = Visibility.Visible;
                break;
            case ScreenViewHitType.TopValue1Bar:
                topValue1BarEditorPanel.Visibility = Visibility.Visible;
                break;
            case ScreenViewHitType.TopValue2Bar:
                topValue2BarEditorPanel.Visibility = Visibility.Visible;
                break;
            case ScreenViewHitType.BottomValue1Bar:
                bottomValue1BarEditorPanel.Visibility = Visibility.Visible;
                break;
            case ScreenViewHitType.BottomValue2Bar:
                bottomValue2BarEditorPanel.Visibility = Visibility.Visible;
                break;
        }
    }
    private void screenView_Hit(object sender, ScreenViewHitEventArgs e)
    {
        SetHit(e.HitType);
        _suppressChange = true;
        screenPartComboBox.SelectedIndex = ((int)e.HitType) + 1;
        _suppressChange = false;
    }

    private void screenPartComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if(!_suppressChange && screenPartComboBox.SelectedIndex!=-1)
        {
            SetHit((ScreenViewHitType)(screenPartComboBox.SelectedIndex-1));
        }
    }
}
