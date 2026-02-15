using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Linq;
namespace Espmon;

public class ScreenErrorEventArgs : EventArgs
{
    public ScreenErrorEventArgs(string filePath, Exception exception)
    {
        FilePath = filePath;
        Exception = exception;
    }

    public string FilePath { get; }
    public Exception Exception { get; }
}

public partial class ScreenWatcher : Component
{
    private readonly FileSystemWatcher _fsw;

    private readonly SynchronizationContext? _synchronizationContext;
    private readonly Dictionary<Screen, string> _screenToFile = new Dictionary<Screen, string>();
    private const string filePattern = "*.screen.json";

    public event EventHandler<ScreenErrorEventArgs>? ScreenError;

    public ScreenWatcher(string folderPath, SynchronizationContext? synchronizationContext = null)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"The folder '{folderPath}' does not exist.");
        }

       _synchronizationContext = synchronizationContext;
        _fsw = new FileSystemWatcher(folderPath, filePattern);

        InitializeComponent();  
        InitializeWatcher();
        if (components != null)
        {
            components.Add(_fsw);
        }
        LoadInitialScreens();
    }

    public ScreenWatcher(string folderPath, SynchronizationContext? synchronizationContext, IContainer container)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"The folder '{folderPath}' does not exist.");
        }

        _synchronizationContext = synchronizationContext;
        _fsw = new FileSystemWatcher(folderPath, filePattern);
        container.Add(this);
        InitializeComponent();
        InitializeWatcher();
        LoadInitialScreens();
    }

    public ObservableCollection<Screen> Screens { get; } = new ObservableCollection<Screen>();

    /// <summary>
    /// Gets the name (the "something" part of "something.screen.json") for a given screen,
    /// or null if the screen is not associated with a file.
    /// </summary>
    public string? GetName(Screen screen)
    {
        if (_screenToFile.TryGetValue(screen, out string? filePath))
        {
            return ExtractScreenName(Path.GetFileName(filePath));
        }
        return null;
    }

    private void Post(Action action)
    {
        if (_synchronizationContext != null)
        {
            _synchronizationContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    private void InitializeWatcher()
    {
        _fsw.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
        _fsw.Created += OnFileCreated;
        _fsw.Changed += OnFileChanged;
        _fsw.Deleted += OnFileDeleted;
        _fsw.Renamed += OnFileRenamed;
        _fsw.EnableRaisingEvents = true;
    }

    private void LoadInitialScreens()
    {
        try
        {
            var screenFiles = Directory.GetFiles(_fsw.Path, filePattern);
            foreach (var filePath in screenFiles)
            {
                LoadScreen(filePath);
            }
        }
        catch (Exception ex)
        {
            RaiseScreenError(string.Empty, ex);
        }
    }

    private void LoadScreen(string filePath)
    {
        try
        {
            using (var reader = new StreamReader(filePath))
            {
                var screen = Screen.ReadFrom(reader);
                Post(() =>
                {
                    _screenToFile[screen] = filePath;
                    Screens.Add(screen);
                    
                });
            }
        }
        catch (Exception ex)
        {
            RaiseScreenError(filePath, ex);
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        LoadScreen(e.FullPath);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Find and remove the old screen associated with this file
        Screen? oldScreen = null;
        Post(() =>
        {
            oldScreen = _screenToFile.FirstOrDefault(kvp => kvp.Value == e.FullPath).Key;
            if (oldScreen != null)
            {
                Screens.Remove(oldScreen);
                _screenToFile.Remove(oldScreen);
            }
        });

        // Load the updated screen
        LoadScreen(e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        Post(() =>
        {
            var screen = _screenToFile.FirstOrDefault(kvp => kvp.Value == e.FullPath).Key;
            if (screen != null)
            {
                Screens.Remove(screen);
                _screenToFile.Remove(screen);
            }
        });
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Check if the new name matches the pattern
        if (Path.GetFileName(e.FullPath).EndsWith(".screen.json", StringComparison.Ordinal))
        {
            // Update the file path in the dictionary
            Post(() =>
            {
                var screen = _screenToFile.FirstOrDefault(kvp => kvp.Value == e.OldFullPath).Key;
                if (screen != null)
                {
                    _screenToFile[screen] = e.FullPath;
                    Screens.Remove(screen);
                    Screens.Add(screen); // force a collection change
                }
                
            });
            
        }
        else
        {
            // The new name doesn't match the pattern, treat as deletion
            OnFileDeleted(sender, e);
        }
    }

    private void RaiseScreenError(string filePath, Exception exception)
    {
        Post(() =>
        {
            ScreenError?.Invoke(this, new ScreenErrorEventArgs(filePath, exception));
        });
    }

    /// <summary>
    /// Extracts the "something" part from "something.screen.json"
    /// </summary>
    private static string ExtractScreenName(string fileName)
    {
        const string suffix = ".screen.json";
        if (fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return fileName.Substring(0, fileName.Length - suffix.Length);
        }
        return fileName;
    }
}