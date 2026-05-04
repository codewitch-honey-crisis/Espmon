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
    
    public MainWindow()
    {

        InitializeComponent();
        ViewModel = new MainViewModel();
        HideAllSecondaryPanels();
        
        ViewModel.Log.CollectionChanged += Log_CollectionChanged;
    }
    
    private void Log_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

  
    public MainViewModel ViewModel { get; }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
      
        ViewModel.Dispose();
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
        string? tag = null;
        if(args.IsSettingsInvoked)
        {
            tag = "settings";
        }
        if (tag==null && args.InvokedItemContainer != null && args.InvokedItemContainer.Tag != null)
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
            case "settings":
                SetPanelVisibility(settingsPanel, Visibility.Visible);
                break;
            case "screens":
                SetPanelVisibility(screensPanel, Visibility.Visible);
                break;
            case "devices":
                SetPanelVisibility(devicesPanel, Visibility.Visible);
                ViewModel?.RefreshDevices();
                break;
            case "providers":
                SetPanelVisibility(providersPanel, Visibility.Visible);
                break;
        }
    }
    private void HideAllSecondaryPanels()
    {
        SetPanelVisibility(settingsPanel, Visibility.Collapsed);
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
                case "settings":
                    SetPanelVisibility(settingsPanel, Visibility.Visible);
                    SetPanelVisibility(devicesPanel, Visibility.Collapsed);
                    SetPanelVisibility(providersPanel, Visibility.Collapsed);
                    SetPanelVisibility(screensPanel, Visibility.Collapsed);
                    break;
                case "screens":
                    SetPanelVisibility(settingsPanel, Visibility.Collapsed);
                    SetPanelVisibility(devicesPanel, Visibility.Collapsed);
                    SetPanelVisibility(providersPanel, Visibility.Collapsed);
                    SetPanelVisibility(screensPanel, Visibility.Visible); 
                    break;
                case "devices":
                    SetPanelVisibility(settingsPanel, Visibility.Collapsed);
                    SetPanelVisibility(devicesPanel, Visibility.Visible);
                    SetPanelVisibility(providersPanel, Visibility.Collapsed);
                    SetPanelVisibility(screensPanel, Visibility.Collapsed);

                    break;
                case "providers":
                    SetPanelVisibility(settingsPanel, Visibility.Collapsed);
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
                case "settings":
                    SetPanelVisibility(settingsPanel, Visibility.Visible);
                    break;
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
    
        //if (e.AddedItems.Count > 0)
        //{
        //    var scrListEntry = ((ScreenListEntry)e.AddedItems.First());
        //    var scr = scrListEntry?.Screen;
        //    if (scrListEntry != null && scr != null)
        //    {
        //        preview.Session = ViewModel.PortController.ViewSession;
                
        //    }
        //} else 
        //{
        //    preview.Session= null;
        //}
    }
    private void screenNameEdit_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            CommitScreenRename((TextBox)sender);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelScreenRename((TextBox)sender);
        }
    }

    private void screenNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitScreenRename((TextBox)sender);
    }

    private void CommitScreenRename(TextBox textBox)
    {
        var grid = (Grid)textBox.Parent;
        var entry = (ScreenListEntry)textBox.DataContext;
        var newName = textBox.Text.Trim();

        textBox.Visibility = Visibility.Collapsed;
        ((TextBlock)grid.FindName("screenNameDisplay")).Visibility = Visibility.Visible;

        if (string.IsNullOrEmpty(newName) || newName == entry.Name || entry.Screen==null || ViewModel==null) return;
        if(entry.Screen==null)
        {
            throw new InvalidOperationException("The screen has not been established");
        }
        entry.Screen.Name= newName;
    }

    private void CancelScreenRename(TextBox textBox)
    {
        var grid = (Grid)textBox.Parent;
        textBox.Text = ((ScreenListEntry)textBox.DataContext).Name;
        textBox.Visibility = Visibility.Collapsed;
        ((TextBlock)grid.FindName("screenNameDisplay")).Visibility = Visibility.Visible;
    }

    private void CommitDeviceRename(TextBox textBox)
    {
        var grid = (Grid)textBox.Parent;
        var entry = (SessionEntry)textBox.DataContext;
        var newName = textBox.Text.Trim();

        textBox.Visibility = Visibility.Collapsed;
        ((TextBlock)grid.FindName("deviceNameDisplay")).Visibility = Visibility.Visible;

        if (string.IsNullOrEmpty(newName) || newName == entry.Name || entry.Session == null || ViewModel == null) return;
        if(entry.Session==null || entry.Session.Device==null)
        {
            throw new InvalidOperationException("The session device has not been established");
        }
        entry.Name= newName;
    }

    private void CancelDeviceRename(TextBox textBox)
    {
        var grid = (Grid)textBox.Parent;
        textBox.Text = ((SessionEntry)textBox.DataContext).Name;
        textBox.Visibility = Visibility.Collapsed;
        ((TextBlock)grid.FindName("deviceNameDisplay")).Visibility = Visibility.Visible;
    }

    

    private void screenDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = (ScreenListEntry)((Button)sender).DataContext;
        if (entry != null && entry.Screen!=null)
        {
            ViewModel.PortController.Screens.Remove(entry.Screen); 
        }
    }

    private void screenNameDisplay_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var grid = (Grid)((TextBlock)sender).Parent;
        ((TextBlock)grid.FindName("screenNameDisplay")).Visibility = Visibility.Collapsed;
        var textBox = (TextBox)grid.FindName("screenNameEdit");
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
            ((TextBlock)grid.FindName("screenNameDisplay")).Visibility = Visibility.Collapsed;
            ((TextBlock)grid.FindName("screenNameDisplayDefault")).Visibility = Visibility.Visible;
            ((Button)grid.FindName("screenDeleteButton")).Visibility = Visibility.Collapsed;
        }
    }

    private void devicePortsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.SelectedSession != null)
        {
            switch (ViewModel.SelectedSession.Status)
            {
                case SessionStatus.Closed:
                    ViewModel.DevicePanelIndex = 0;
                    break;
                case SessionStatus.RequiresFlash:
                    ViewModel.DevicePanelIndex = 2;
                    break;
                default:
                    ViewModel.DevicePanelIndex = 1;
                    break;
            }
        } else
        {
            ViewModel.DevicePanelIndex = 0;
        }
    }

    private async void flashButton_Click(object sender, RoutedEventArgs e)
    {
        if(ViewModel!=null && ViewModel.SelectedSession!=null)
        {
            var session = ViewModel.SelectedSession;
            var idx = flashCombo.SelectedIndex;
            if (idx>-1)
            {
                ViewModel.DevicePanelIndex = 2;
                ViewModel.Log.Clear();
                var reporter = new OpenFlashProgressReporter(ViewModel.Log);
                await session.FlashAsync(FirmwareEntry.GetFirmwareEntries()[idx], reporter);
                ViewModel.Log.Clear();
                ViewModel.DevicePanelIndex = 0;
                
            } else
            {
                ViewModel.DevicePanelIndex=2;
            }

        }
    }

    private void openButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.SelectedSession != null)
        {
            var session = ViewModel.SelectedSession;
            session.Connect();
            ViewModel.DevicePanelIndex = 1;
        }
    }

    private void deviceScreens_ScreenSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null && ViewModel.SelectedSession!= null)
        {
            currentScreenView.Session = ViewModel.SelectedSession;
            ViewModel.SelectedSession.ScreenIndex = deviceScreens.SelectedScreenIndex;
           
        }
        
        
    }

    private async void retryConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.SelectedSession != null)
        {
            var session = ViewModel.SelectedSession;
            session.Disconnect();
            session.Refresh();
            ViewModel.Log.Clear();
            var reporter = new OpenFlashProgressReporter(ViewModel.Log);
            await session.ResetAsync(reporter);
            ViewModel.Log.Clear();
            session.Connect();
        }
    }

    private void screenItemList_Loaded(object sender, RoutedEventArgs e)
    {
        if(ViewModel.SelectedScreen==null && ViewModel.ScreenItems.Count>0)
        {
            ViewModel.SelectedScreenIndex = 0;
        }
    }

    private async void manualFlashButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.SelectedSession != null)
        {
            var session = ViewModel.SelectedSession;
            var idx = flashCombo.SelectedIndex;
            if (idx > -1)
            {
                ViewModel.Log.Clear();
               
                var reporter = new OpenFlashProgressReporter(ViewModel.Log);
                await session.FlashAsync(FirmwareEntry.GetFirmwareEntries()[idx], reporter);
                ViewModel.Log.Clear();
            }

        }
    }

    private void deviceNameDisplay_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var grid = (Grid)((TextBlock)sender).Parent;
        ((TextBlock)grid.FindName("deviceNameDisplay")).Visibility = Visibility.Collapsed;
        var textBox = (TextBox)grid.FindName("deviceNameEdit");
        textBox.Visibility = Visibility.Visible;
        textBox.SelectAll();
        textBox.Focus(FocusState.Keyboard);
    }

    private void deviceDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = (SessionEntry)((Button)sender).DataContext;
        if (entry != null && entry.Session!= null && entry.Session.Device!=null)
        {
            ViewModel.PortController.Devices.Remove(entry.Session.Device);
        }
    }

    
    private void deviceNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitDeviceRename((TextBox)sender);
    }

    private void deviceNameEdit_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            CommitDeviceRename((TextBox)sender);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelDeviceRename((TextBox)sender);
        }
    }

    private void screenNewButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NewScreen();
    }
}