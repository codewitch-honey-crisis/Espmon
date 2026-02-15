using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using System;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Versioning;

using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Espmon;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class MainWindow : Window
{
    DispatcherTimer _timer = new DispatcherTimer();
    public MainWindow()
    {

        InitializeComponent();
        ViewModel = new MainViewModel();
        ViewModel.HardwareInfo.MinimumTrackingInterval = TimeSpan.FromMilliseconds(100);
        ViewModel.HardwareInfo.StartAll();
        HideAllSecondaryPanels();
        currentScreenView.Screen = Screen.Default;
        _timer.Interval = ViewModel.HardwareInfo.MinimumTrackingInterval;
        _timer.Tick += _timer_Tick;
        _timer.Start();
        ViewModel.Log.CollectionChanged += Log_CollectionChanged;
    }

    private void Log_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            if (openFlashLogScroller.Visibility == Visibility.Visible)
            {
                openFlashLogScroller.ChangeView(null, openFlashLogScroller.ScrollableHeight, null);
            } else if(openLogScroller.Visibility==Visibility.Visible)
            {
                openLogScroller.ChangeView(null,openLogScroller.ScrollableHeight, null);
            }
        }
    }

    private void _timer_Tick(object? sender, object e)
    {
        if (ViewModel != null)
        {
            ViewModel.Refresh();
            if (ViewModel.HardwareInfo != null)
            {
                ViewModel.HardwareInfo.ExpireTracking();
                if (screensPanel.Visibility == Visibility.Visible && preview.Screen != null)
                {

                    preview.Refresh();
                }
                if (devicesPanel.Visibility == Visibility.Visible)
                {
                    if(currentScreenView.Screen==null && ViewModel.SelectedSession?.CurrentScreen!=null)
                    {
                        currentScreenView.Screen = ViewModel.SelectedSession.CurrentScreen;
                    }
                    currentScreenView.Refresh();
                }
            }
        }
        
    }
    
    public MainViewModel ViewModel { get; }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        ViewModel.HardwareInfo.Dispose();
    }
    void SetPanelVisibility(Grid panel, Visibility visibility)
    {
        if (visibility == Visibility.Collapsed)
        {
            //foreach (var ctl in panel.Children)
            //{
            //    ctl.Visibility = visibility;
            //}
            panel.Visibility = visibility;
        }
        else
        {
            panel.Visibility = visibility;
            //foreach (var ctl in panel.Children)
            //{
            //    ctl.Visibility = visibility;
            //}
        }
    }
    private void navView_SelectionChanged(object sender, NavigationViewSelectionChangedEventArgs e)
    {
        // e.SelectedItem - the item that was selected
        // e.SelectedItemContainer - the NavigationViewItem container
        // e.IsSettingsSelected - bool
    }

    private void navView_ItemInvoked(object sender, NavigationViewItemInvokedEventArgs args)
    {
        // e.InvokedItem - the item
        // e.InvokedItemContainer - the container
        // e.IsSettingsInvoked - bool
        string? tag = null;

        if (args.InvokedItemContainer != null && args.InvokedItemContainer.Tag != null)
        {
            tag = args.InvokedItemContainer.Tag.ToString();
        }

        // Toggle behavior
        if (tag == lastInvokedTag)
        {
            HideAllSecondaryPanels();
            lastInvokedTag = null;
            return;
        }

        lastInvokedTag = tag;
        HideAllSecondaryPanels();

        switch (tag)
        {
            case "screens":
                SetPanelVisibility(screensPanel, Visibility.Visible);
                _timer.Start();
                break;
            case "devices":
                SetPanelVisibility(devicesPanel, Visibility.Visible);
                break;
            case "providers":
                SetPanelVisibility(providersPanel, Visibility.Visible);
                break;
        }
    }
    private void HideAllSecondaryPanels()
    {
        SetPanelVisibility(screensPanel, Visibility.Collapsed); 
        SetPanelVisibility(devicesPanel, Visibility.Collapsed);
        SetPanelVisibility(providersPanel, Visibility.Collapsed);

    }
    private void navView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        // When nav collapses to compact/minimal, hide secondary panels
        if (args.DisplayMode == NavigationViewDisplayMode.Compact ||
            args.DisplayMode == NavigationViewDisplayMode.Minimal)
        {
            HideAllSecondaryPanels();
        }
    }
    private string? lastInvokedTag = null;
    private void navView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        if (lastInvokedTag != null)
        {
            switch (lastInvokedTag)
            {
                case "screens":
                    SetPanelVisibility(devicesPanel, Visibility.Collapsed);
                    SetPanelVisibility(providersPanel, Visibility.Collapsed);
                    SetPanelVisibility(screensPanel, Visibility.Visible); 

                    break;
                case "devices":
                    SetPanelVisibility(devicesPanel, Visibility.Visible);
                    SetPanelVisibility(providersPanel, Visibility.Collapsed);
                    SetPanelVisibility(screensPanel, Visibility.Collapsed);

                    break;
                case "providers":
                    SetPanelVisibility(providersPanel, Visibility.Visible);
                    SetPanelVisibility(devicesPanel, Visibility.Collapsed);
                    SetPanelVisibility(screensPanel, Visibility.Collapsed); 

                    break;
            }
        }
        else
        {
            HideAllSecondaryPanels();
        }
    }
    private void navView_PaneOpening(NavigationView sender, object args)
    {
        // Restore the last active secondary panel
        if (lastInvokedTag != null)
        {
            switch (lastInvokedTag)
            {
                case "screens":
                    SetPanelVisibility(screensPanel, Visibility.Visible); 

                    break;
                case "devices":
                    ViewModel.SessionEntries.Clear();
                    SetPanelVisibility(devicesPanel, Visibility.Visible); 
                    break;
                case "providers":
                    SetPanelVisibility(providersPanel, Visibility.Visible);
                    break;
            }
        }
    }

    private void screenItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var toUnset in e.RemovedItems)
        {
            var scrToUnset = ((ScreenListEntry)toUnset)?.Screen;
            if (scrToUnset != null)
            {
                scrToUnset.HardwareInfo = null;
            }
        }
        if (e.AddedItems.Count > 0)
        {
            var scrListEntry = ((ScreenListEntry)e.AddedItems.First());
            var scr = scrListEntry?.Screen;
            if (scrListEntry != null && scr != null)
            {
                System.Diagnostics.Debug.WriteLine($"Screen is {scrListEntry.Name}");
                scr.HardwareInfo = ViewModel.HardwareInfo;
                preview.Screen = scr;
            }
        } else 
        {
            preview.Screen = null;
        }
    }
    private void NameEdit_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            CommitRename((TextBox)sender);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelRename((TextBox)sender);
        }
    }

    private void NameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitRename((TextBox)sender);
    }

    private void CommitRename(TextBox textBox)
    {
        var grid = (Grid)textBox.Parent;
        var entry = (ScreenListEntry)textBox.DataContext;
        var newName = textBox.Text.Trim();

        textBox.Visibility = Visibility.Collapsed;
        ((TextBlock)grid.FindName("nameDisplay")).Visibility = Visibility.Visible;

        if (string.IsNullOrEmpty(newName) || newName == entry.Name || entry.Screen==null || ViewModel==null) return;
        ViewModel.RenameScreen(entry.Screen, newName);
    }

    private void CancelRename(TextBox textBox)
    {
        var grid = (Grid)textBox.Parent;
        textBox.Text = ((ScreenListEntry)textBox.DataContext).Name;
        textBox.Visibility = Visibility.Collapsed;
        ((TextBlock)grid.FindName("nameDisplay")).Visibility = Visibility.Visible;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = (ScreenListEntry)((Button)sender).DataContext;

        if (entry.IsDefault && entry.Screen!=null)
        {
            var newName = GenerateScreenName();
            Screen scr = entry.Screen;
            ViewModel.AddScreen(newName, scr);
        }
        else if(entry.Screen!=null)
        {
            ViewModel.SaveScreen(entry.Screen);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = (ScreenListEntry)((Button)sender).DataContext;
        if (entry != null && entry.Screen!=null)
        {
            ViewModel.DeleteScreen(entry.Screen);
        }
    }

    private string GenerateScreenName()
    {
        
        int i = 1;
        while (ViewModel.ScreenItems.Any(s => s.Name == $"Screen {i}"))
            i++;
        return $"Screen {i}";
    }

    private void nameDisplay_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var grid = (Grid)((TextBlock)sender).Parent;
        ((TextBlock)grid.FindName("nameDisplay")).Visibility = Visibility.Collapsed;
        var textBox = (TextBox)grid.FindName("nameEdit");
        textBox.Visibility = Visibility.Visible;
        textBox.SelectAll();
        textBox.Focus(FocusState.Keyboard);
    }
    private void screenItem_Loaded(object sender, RoutedEventArgs e)
    {
        var grid = (Grid)sender;
        var entry = (ScreenListEntry)grid.DataContext;
        if (entry == null) return;
        if (entry.IsDefault)
        {
            ((TextBlock)grid.FindName("nameDisplay")).Visibility = Visibility.Collapsed;
            ((TextBlock)grid.FindName("nameDisplayDefault")).Visibility = Visibility.Visible;
            ((Button)grid.FindName("deleteButton")).Visibility = Visibility.Collapsed;
        }
    }

    private void devicePortsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        
    }

    private async void flashButton_Click(object sender, RoutedEventArgs e)
    {
        if(ViewModel!=null && ViewModel.SelectedSession!=null)
        {
            var session = ViewModel.SelectedSession;
            var idx = flashCombo.SelectedIndex;
            if (idx>-1)
            {
                ViewModel.Log.Clear();
                var reporter = new OpenFlashProgressReporter(ViewModel.Log);
                await session.FlashAsync(ViewModel.FirmwareEntries[idx], reporter);
                ViewModel.Log.Clear();
            }

        }
    }

    private void openButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.SelectedSession != null)
        {
            var session = ViewModel.SelectedSession;
            session.Open();
        }
    }

    private void closeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.SelectedSession != null)
        {
            var session = ViewModel.SelectedSession;
            session.Close();
        }
    }

    private void deviceScreens_ScreenSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null && ViewModel.SelectedSession!= null)
        {
            ViewModel.SelectedSession.CurrentScreenIndex = deviceScreens.SelectedScreenIndex;
            currentScreenView.Screen = ViewModel.SelectedSession.CurrentScreen;
        }
        
        
    }

    private async void retryConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.SelectedSession != null)
        {
            var session = ViewModel.SelectedSession;
            session.Close();
            session.Update();
            ViewModel.Log.Clear();
            var reporter = new OpenFlashProgressReporter(ViewModel.Log);
            await session.ResetAsync(reporter);
            ViewModel.Log.Clear();

            session.Open();
        }
    }

    private void screenItemList_Loaded(object sender, RoutedEventArgs e)
    {
        if(ViewModel.SelectedScreen==null && ViewModel.ScreenItems.Count>0)
        {
            ViewModel.SelectedScreenIndex = 0;
        }
    }
}