using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

using Windows.Storage;
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
        var iconPath = Path.Combine(AppContext.BaseDirectory, "espmon.ico");
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(iconPath);
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
        if (e.Key == VirtualKey.Enter && sender is FileNameTextBox fntb) 
        {
            if (fntb.IsFileNameValid)
            {
                e.Handled = true;
                CommitScreenRename((TextBox)sender);
            }
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelScreenRename((TextBox)sender);
        }
    }

    private void screenNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if(sender is FileNameTextBox fntb)
        {
           CommitScreenRename(fntb);
        }

        
    }

    private void CommitScreenRename(TextBox textBox)
    {
        var grid = (Grid)textBox.Parent;
        var entry = (ScreenListEntry)textBox.DataContext;
        var newName = textBox.Text.Trim();

        textBox.Visibility = Visibility.Collapsed;
        ((TextBlock)grid.FindName("screenNameDisplay")).Visibility = Visibility.Visible;

        if (string.IsNullOrEmpty(newName) || entry.Screen==null || ViewModel==null) return;
        if(entry.Screen==null)
        {
            throw new InvalidOperationException("The screen has not been established");
        }
        entry.Name= newName;
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
    private bool _flashInProgress = false;
    private async void flashButton_Click(object sender, RoutedEventArgs e)
    {
        if (_flashInProgress) return;
        if(ViewModel!=null && ViewModel.SelectedSession!=null)
        {
            _flashInProgress = true;
            var session = ViewModel.SelectedSession;
            var idx = flashCombo.SelectedIndex;
            if (idx>-1)
            {
                ViewModel.DevicePanelIndex = 2;
                ViewModel.Log.Clear();
                var reporter = new OpenFlashProgressReporter(ViewModel.Log);
                try
                {
                    await session.FlashAsync(FirmwareEntry.GetFirmwareEntries()[idx], reporter);
                } catch
                {
                    try
                    {
                        await session.FlashAsync(FirmwareEntry.GetFirmwareEntries()[idx], reporter);
                    }
                    catch
                    {
                        await session.FlashAsync(FirmwareEntry.GetFirmwareEntries()[idx], reporter);
                    }
                }
                ViewModel.Log.Clear();
                ViewModel.DevicePanelIndex = 0;
                
            } else
            {
                ViewModel.DevicePanelIndex=2;
            }
            _flashInProgress = false;
            flashCombo.SelectedIndex = -1;

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
        screenItemList.AddHandler(
       UIElement.PointerPressedEvent,
       new PointerEventHandler(CommitFocusBeforeSelect),
       handledEventsToo: true);
    }
    private void CommitFocusBeforeSelect(object sender, PointerRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(screenItemList.XamlRoot) as FrameworkElement;
        if (focused is not null && focused != screenItemList)
            screenItemList.Focus(FocusState.Pointer);
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
        if (ViewModel.SelectedSession == null || ViewModel.SelectedSession.Device == null) return;
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
            entry.Session.Disconnect();
            ViewModel.PortController.RefreshSessions();
            ViewModel.RefreshDevices();
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

    private async void screenOpenButton_Click(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker(this.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        openPicker.FileTypeFilter.Add(".json");
        openPicker.FileTypeFilter.Add("*");

        PickFileResult result = await openPicker.PickSingleFileAsync();
        if (result != null)
        {
            string path = result.Path;
            using var reader = new StreamReader(path);
            ViewModel.PortController.ImportScreens(reader, path);
            reader.Close();
        }
    }

    private async void screenSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedScreen == null) return;
        var savePicker = new FileSavePicker(this.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = ViewModel.SelectedScreen.Name
        };
        savePicker.FileTypeChoices.Add("JSON file", new List<string>() { ".json" });

        PickFileResult result = await savePicker.PickSaveFileAsync();
        if (result != null)
        {
            using var writer = new StreamWriter(result.Path, false, Encoding.UTF8);
            ViewModel.PortController.ExportScreen(writer,ViewModel.SelectedScreen.Name);
            writer.Close();
        }
    }

    private async void screenExportButton_Click(object sender, RoutedEventArgs e)
    {
        var savePicker = new FileSavePicker(this.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "New Export",
        };
        savePicker.FileTypeChoices.Add("JSON file", new List<string>() { ".json" });

        PickFileResult result = await savePicker.PickSaveFileAsync();
        if (result != null && ViewModel.PortController!=null)
        {
            using var writer = new StreamWriter(result.Path, false, Encoding.UTF8);
            ViewModel.PortController.ExportScreens(writer);
            writer.Close();
        }
    }
    private void deviceNameEdit_Validating(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (ViewModel.SelectedSession==null || ViewModel.SelectedSessionEntry==null) { return; }
        string name = ((FileNameTextBox)sender).Text;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var idx = ViewModel.SessionEntries.IndexOf(ViewModel.SelectedSessionEntry);
        e.Cancel = false;
        for (int i = 0; i < ViewModel.ScreenItems.Count; i++)
        {
            if (i == idx) continue;
            if (name.Equals(ViewModel.SessionEntries[i].Name, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                return;
            }
        }
    }
    private void screenNameEdit_Validating(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (ViewModel.SelectedScreen == null) { return; }
        string name = ((FileNameTextBox)sender).Text;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        var idx = ViewModel.SelectedScreenIndex;
        e.Cancel = false;
        for (int i = 0; i < ViewModel.ScreenItems.Count; i++)
        {
            if (i == idx) continue;
            if (name.Equals(ViewModel.ScreenItems[i].Name,StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                return;
            }
        }
    }
}