using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

using System.Runtime.Versioning;
using System;
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Espmon;

public record IntervalOption(string Display, int Milliseconds);
public static class IntervalOptions
{
    public record Option(string Display, int Milliseconds);

    public static Option[] All { get; } =
    {
        new("10 Hz", 100),
        new("5 Hz", 200),
        new("1 Hz", 1000),
    };
}
[SupportedOSPlatform("windows")]
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
        _suppressChange = true;
        screenPartComboBox.SelectedIndex = 0;
        _suppressChange = false;
        CollapseAll();
    }
    public static readonly DependencyProperty ScalingModeProperty =
       DependencyProperty.Register(
           nameof(ScalingMode),
           typeof(ScreenViewScalingMode),
           typeof(ScreenEditor),
           new PropertyMetadata(ScreenViewScalingMode.DpiAware, OnScalingModeChanged));
    public ScreenViewScalingMode ScalingMode
    {
        get => (ScreenViewScalingMode)GetValue(ScalingModeProperty);
        set => SetValue(ScalingModeProperty, value);
    }
    private static void OnScalingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScreenEditor editor)
        {
            editor.screenView.ScalingMode = (ScreenViewScalingMode)e.NewValue;
        }
    }
    public static readonly DependencyProperty SessionProperty =
    DependencyProperty.Register(
        nameof(Session),
        typeof(SessionController),
        typeof(ScreenEditor),
        new PropertyMetadata(null, OnSessionChanged));

    public SessionController? Session
    {
        get => (SessionController?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }
    

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScreenEditor editor)
        {
            // Unsubscribe from old screen tree
            if (e.OldValue is SessionController oldSession)
            {
                //oldSession.PropertyChanged -= Session_PropertyChanged;
            }

            // Subscribe to new screen tree
            if (e.NewValue is SessionController newSession)
            {
                editor.screenView.Session=newSession;
                //newSession.PropertyChanged += Session_PropertyChanged;
            }
        }
    }

 
    public static readonly DependencyProperty ScreenProperty =
    DependencyProperty.Register(
        nameof(Screen),
        typeof(ScreenController),
        typeof(ScreenEditor),
        new PropertyMetadata(null, null));

    public ScreenController? Screen
    {
        get => (ScreenController?)GetValue(ScreenProperty);
        set {
            // fails with a null ref even if value is non-null on initial set
            try
            {
                SetValue(ScreenProperty, value);
            }
            catch(NullReferenceException) { 
                throw;
            }
        }
    }


    //private static void OnScreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    //{
        
    //}
   
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

    private void ComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        var cb = (ComboBox)sender;
        if (cb.SelectedIndex == -1 && cb.SelectedValue != null)
        {
            var v = cb.SelectedValue;
            //cb.SelectedValue = null;   // clear the un-resolved state
            cb.SelectedValue = v;      // re-apply now that items are realized
        }
    }
}
