using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;

namespace Espmon;
[SupportedOSPlatform("windows")]
public sealed partial class ScreenSelector : UserControl
{
    // Right list (SelectedScreensList) - these stay the same
    public static readonly DependencyProperty DeviceScreensProperty =
        DependencyProperty.Register(
            nameof(DeviceScreens),
            typeof(ObservableCollection<string>),
            typeof(ScreenSelector),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedScreenIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedScreenIndex),
            typeof(int),
            typeof(ScreenSelector),
            new PropertyMetadata(-1));

    // Left list (AvailableScreensList) - new simplified properties
    public static readonly DependencyProperty ScreensProperty =
        DependencyProperty.Register(
            nameof(Screens),
            typeof(ObservableCollection<ScreenListEntry>),
            typeof(ScreenSelector),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AvailableSelectedItemProperty =
        DependencyProperty.Register(
            nameof(AvailableSelectedItem),
            typeof(ScreenListEntry),
            typeof(ScreenSelector),
            new PropertyMetadata(null, OnAvailableSelectedItemChanged));

    public static readonly DependencyProperty AvailableSelectedIndexProperty =
        DependencyProperty.Register(
            nameof(AvailableSelectedIndex),
            typeof(int),
            typeof(ScreenSelector),
            new PropertyMetadata(-1, OnAvailableSelectedIndexChanged));
    public event SelectionChangedEventHandler? ScreenSelectionChanged;
    // Right list properties
    public ObservableCollection<string> DeviceScreens
    {
        get => (ObservableCollection<string>)GetValue(DeviceScreensProperty);
        set => SetValue(DeviceScreensProperty, value);
    }
    
    public int SelectedScreenIndex
    {
        get => (int)GetValue(SelectedScreenIndexProperty);
        set => SetValue(SelectedScreenIndexProperty, value);
    }

    // Left list properties
    public ObservableCollection<ScreenListEntry> Screens
    {
        get => (ObservableCollection<ScreenListEntry>)GetValue(ScreensProperty);
        set => SetValue(ScreensProperty, value);
    }

    public ScreenListEntry? AvailableSelectedItem
    {
        get => (ScreenListEntry?)GetValue(AvailableSelectedItemProperty);
        set => SetValue(AvailableSelectedItemProperty, value);
    }

    public int AvailableSelectedIndex
    {
        get => (int)GetValue(AvailableSelectedIndexProperty);
        set => SetValue(AvailableSelectedIndexProperty, value);
    }

    private static void OnAvailableSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScreenSelector selector && !selector._isUpdatingSelection)
        {
            selector._isUpdatingSelection = true;
            selector.AvailableScreensList.SelectedItem = e.NewValue;
            selector._isUpdatingSelection = false;
        }
    }

    private static void OnAvailableSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScreenSelector selector && !selector._isUpdatingSelection)
        {
            selector._isUpdatingSelection = true;
            selector.AvailableScreensList.SelectedIndex = (int)e.NewValue;
            selector._isUpdatingSelection = false;
        }
    }

    private bool _isUpdatingSelection = false;

    public ScreenSelector()
    {
        this.InitializeComponent();
        this.Loaded += (s, e) =>
        {
            AvailableScreensList.SelectionChanged += AvailableScreensList_SelectionChanged;
        };
    }

  
    private void AvailableScreensList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        //if (_isUpdatingSelection)
        //{
        //    return;
        //}
        _isUpdatingSelection = true;
        AvailableSelectedItem = AvailableScreensList.SelectedItem as ScreenListEntry;
        AvailableSelectedIndex = AvailableScreensList.SelectedIndex;
        _isUpdatingSelection = false;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceScreens == null) return;

        if (AvailableScreensList.SelectedItem is ScreenListEntry selectedScreen)
        {
            DeviceScreens.Add(selectedScreen.Name);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceScreens == null) return;

        if (SelectedScreensList.SelectedItem is string selectedScreen)
        {
            var index = SelectedScreensList.SelectedIndex;
            DeviceScreens.RemoveAt(index);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceScreens == null) return;

        var index = SelectedScreensList.SelectedIndex;
        if (index > 0)
        {
            var item = DeviceScreens[index];
            DeviceScreens.RemoveAt(index);
            DeviceScreens.Insert(index - 1, item);
            SelectedScreensList.SelectedIndex = index - 1;
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceScreens == null) return;

        var index = SelectedScreensList.SelectedIndex;
        if (index >= 0 && index < DeviceScreens.Count - 1)
        {
            var item = DeviceScreens[index];
            DeviceScreens.RemoveAt(index);
            DeviceScreens.Insert(index + 1, item);
            SelectedScreensList.SelectedIndex = index + 1;
        }
    }

    private void SelectedScreensList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ScreenSelectionChanged?.Invoke(this,e);
        
    }
}