using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;

using Windows.Foundation;


namespace Espmon;

public enum ScreenViewHitType
{
    None = -1,
    TopLabel,
    TopValue1,
    TopValue1Bar,
    TopValue2,
    TopValue2Bar,
    BottomLabel,
    BottomValue1,
    BottomValue1Bar,
    BottomValue2,
    BottomValue2Bar,
    Graph
}
public class ScreenViewHitEventArgs
{
    public ScreenViewHitType HitType { get; }
    public double X { get; }
    public double Y { get; }
    public ScreenViewHitEventArgs(ScreenViewHitType hitType, double x, double y)
    {
        HitType = hitType;
        X = x;
        Y = y;
    }
}
public enum ScreenViewScalingMode
{
    None,           // Pixel-perfect, no DPI scaling
    DpiAware        // Scale with DPI like Avalonia version
}

public partial class ScreenView : Canvas
{
    private WriteableBitmap? _bitmap;
    private Image? _image;
    private IntPtr _handle;
    private Size _lastSize;
    private bool _screenInitialized = false;
    private bool _requiresClear = false;
    private Abi.ResponseScreen _responseScreen = default;
    private Abi.ResponseData _responseData = default;
    private double _lastRasterizationScale = 1.0;

    public ScreenView()
    {
        _handle = Abi.Create();
        _responseScreen.Top.Label = new byte[16];
        _responseScreen.Top.Value1.Suffix = new byte[12];
        _responseScreen.Top.Value2.Suffix = new byte[12];

        _responseScreen.Bottom.Label = new byte[16];
        _responseScreen.Bottom.Value1.Suffix = new byte[12];
        _responseScreen.Bottom.Value2.Suffix = new byte[12];

        // Create an image element as a child
        _image = new Image
        {
            Stretch = Stretch.Fill
        };
        Children.Add(_image);

        // Keep red background for error detection
        Background = new SolidColorBrush(Microsoft.UI.Colors.Red);
        PointerPressed += Control_PointerPressed;
        SizeChanged += Control_SizeChanged;
        Loaded += Control_Loaded;
        Unloaded += Control_Unloaded;
    }

    
    public static readonly DependencyProperty ScalingModeProperty =
        DependencyProperty.Register(
            nameof(ScalingMode),
            typeof(ScreenViewScalingMode),
            typeof(ScreenView),
            new PropertyMetadata(ScreenViewScalingMode.DpiAware, OnScalingModeChanged));
    public ScreenViewScalingMode ScalingMode
    {
        get => (ScreenViewScalingMode)GetValue(ScalingModeProperty);
        set => SetValue(ScalingModeProperty, value);
    }

    private static void OnScalingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScreenView view)
        {
            view._screenInitialized = false;
            view.InitializeBitmap();
            view.RefreshScreen();
        }
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
        if (d is ScreenView view)
        {
            // Unsubscribe from old screen tree
            if (e.OldValue is Screen oldScreen)
            {
                view.UnsubscribeFromScreen(oldScreen);
            }

            // Subscribe to new screen tree
            if (e.NewValue is Screen newScreen)
            {
                view.SubscribeToScreen(newScreen);
                view._requiresClear = true;
            }
            view._screenInitialized = false;
        }
    }


    public void Refresh(bool refreshHardwareInfo = true)
    {
        if (Screen != null)
        {
            if (refreshHardwareInfo)
            {
                Screen.Refresh();
            }
            if (!_screenInitialized)
            {
                RefreshScreen();
            }
            RefreshValues();
        }
    }

    private void SubscribeToScreenEntry(ScreenEntry entry)
    {
        entry.PropertyChanged += ScreenModel_PropertyChanged;
        if (entry.Value1 != null)
        {
            entry.Value1.PropertyChanged += ScreenModel_PropertyChanged;
        }
        if (entry.Value2 != null)
        {
            entry.Value2.PropertyChanged += ScreenModel_PropertyChanged;
        }
    }

    private void UnsubscribeFromScreenEntry(ScreenEntry entry)
    {
        entry.PropertyChanged -= ScreenModel_PropertyChanged;
        if (entry.Value1 != null)
        {
            entry.Value1.PropertyChanged -= ScreenModel_PropertyChanged;
        }
        if (entry.Value2 != null)
        {
            entry.Value2.PropertyChanged -= ScreenModel_PropertyChanged;
        }
    }

    private void SubscribeToScreen(Screen screen)
    {
        screen.PropertyChanged += ScreenModel_PropertyChanged;

        if (screen.Top != null)
        {
            SubscribeToScreenEntry(screen.Top);
        }
        if (screen.Bottom != null)
        {
            SubscribeToScreenEntry(screen.Bottom);
        }
    }

    private void UnsubscribeFromScreen(Screen screen)
    {
        screen.PropertyChanged -= ScreenModel_PropertyChanged;

        if (screen.Top != null)
        {
            UnsubscribeFromScreenEntry(screen.Top);
        }
        if (screen.Bottom != null)
        {
            UnsubscribeFromScreenEntry(screen.Bottom);
        }
    }

    private void ScreenModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ScreenValueEntry.Min) && e.PropertyName != nameof(ScreenValueEntry.Max) && e.PropertyName != nameof(ScreenValueEntry.Value))
        {
            _screenInitialized = false;
            RefreshScreen();
        }
    }

    private Abi.ResponseColor ToColor(int argb)
    {
        Abi.ResponseColor result;
        result.A = (byte)((argb >> 24) & 0xff);
        result.R = (byte)((argb >> 16) & 0xff);
        result.G = (byte)((argb >> 8) & 0xff);
        result.B = (byte)((argb >> 0) & 0xff);
        return result;
    }

    private void BuildResponseScreen()
    {
        if (Screen == null) return;
        _responseScreen.Header.Flags = 0;
        if (Screen.Top != null)
        {
            if (Screen.Top.Label != null)
            {
                var bytes = Encoding.UTF8.GetBytes(Screen.Top.Label);
                bytes.CopyTo(_responseScreen.Top.Label, 0);
                _responseScreen.Top.Label[bytes.Length] = 0;
            }
            else
            {
                _responseScreen.Top.Label[0] = 0;
            }
            _responseScreen.Top.Color = ToColor(Screen.Top.Color);

            if (Screen.Top.Value1 != null)
            {
                if (!string.IsNullOrEmpty(Screen.Top.Value1.Entry.Unit))
                {
                    var bytes = Encoding.UTF8.GetBytes(Screen.Top.Value1.Entry.Unit);
                    bytes.CopyTo(_responseScreen.Top.Value1.Suffix, 0);
                    _responseScreen.Top.Value1.Suffix[bytes.Length] = 0;
                }
                else
                {
                    _responseScreen.Top.Value1.Suffix[0] = 0;
                }
                _responseScreen.Top.Value1.Color = ToColor(Screen.Top.Value1.Color);
                if (Screen.Top.Value1.HasGradient)
                {
                    _responseScreen.Header.Flags |= (1 << 0);
                }
            }
            if (Screen.Top.Value2 != null)
            {
                if (!string.IsNullOrEmpty(Screen.Top.Value2.Entry.Unit))
                {
                    var bytes = Encoding.UTF8.GetBytes(Screen.Top.Value2.Entry.Unit);
                    bytes.CopyTo(_responseScreen.Top.Value2.Suffix, 0);
                    _responseScreen.Top.Value2.Suffix[bytes.Length] = 0;
                }
                else
                {
                    _responseScreen.Top.Value2.Suffix[0] = 0;
                }
                _responseScreen.Top.Value2.Color = ToColor(Screen.Top.Value2.Color);
                if (Screen.Top.Value2.HasGradient)
                {
                    _responseScreen.Header.Flags |= (1 << 1);
                }
            }
        }
        if (Screen.Bottom != null)
        {
            if (Screen.Bottom.Label != null)
            {
                var bytes = Encoding.UTF8.GetBytes(Screen.Bottom.Label);
                bytes.CopyTo(_responseScreen.Bottom.Label, 0);
                _responseScreen.Bottom.Label[bytes.Length] = 0;
            }
            else
            {
                _responseScreen.Bottom.Label[0] = 0;
            }
            _responseScreen.Bottom.Color = ToColor(Screen.Bottom.Color);
            if (Screen.Bottom.Value1 != null)
            {
                if (!string.IsNullOrEmpty(Screen.Bottom.Value1.Entry.Unit))
                {
                    var bytes = Encoding.UTF8.GetBytes(Screen.Bottom.Value1.Entry.Unit);
                    bytes.CopyTo(_responseScreen.Bottom.Value1.Suffix, 0);
                    _responseScreen.Bottom.Value1.Suffix[bytes.Length] = 0;
                }
                else
                {
                    _responseScreen.Bottom.Value1.Suffix[0] = 0;
                }
                _responseScreen.Bottom.Value1.Color = ToColor(Screen.Bottom.Value1.Color);
                if (Screen.Bottom.Value1.HasGradient)
                {
                    _responseScreen.Header.Flags |= (1 << 2);
                }
            }
            if (Screen.Bottom.Value2 != null)
            {
                if (!string.IsNullOrEmpty(Screen.Bottom.Value2.Entry.Unit))
                {
                    var bytes = Encoding.UTF8.GetBytes(Screen.Bottom.Value2.Entry.Unit);
                    bytes.CopyTo(_responseScreen.Bottom.Value2.Suffix, 0);
                    _responseScreen.Bottom.Value2.Suffix[bytes.Length] = 0;
                }
                else
                {
                    _responseScreen.Bottom.Value2.Suffix[0] = 0;
                }
                _responseScreen.Bottom.Value2.Color = ToColor(Screen.Bottom.Value2.Color);
                if (Screen.Bottom.Value2.HasGradient)
                {
                    _responseScreen.Header.Flags |= (1 << 3);
                }
            }
        }
    }

    private void BuildResponseData()
    {
        if (Screen == null || Screen.HardwareInfo == null) return;

        if (Screen.Top != null)
        {
            if (Screen.Top.Value1 != null)
            {
                _responseData.Top.Value1.Value = Screen.Top.Value1.Value;
                _responseData.Top.Value1.Scaled = Screen.Top.Value1.Scaled;
            }
            if (Screen.Top.Value2 != null)
            {
                _responseData.Top.Value2.Value = Screen.Top.Value2.Value;
                _responseData.Top.Value2.Scaled = Screen.Top.Value2.Scaled;
            }
        }
        if (Screen.Bottom != null)
        {
            if (Screen.Bottom.Value1 != null)
            {
                _responseData.Bottom.Value1.Value = Screen.Bottom.Value1.Value;
                _responseData.Bottom.Value1.Scaled = Screen.Bottom.Value1.Scaled;
            }
            if (Screen.Bottom.Value2 != null)
            {
                _responseData.Bottom.Value2.Value = Screen.Bottom.Value2.Value;
                _responseData.Bottom.Value2.Scaled = Screen.Bottom.Value2.Scaled;
            }
        }
    }

    private void RefreshScreen()
    {
        if (_screenInitialized) return;
        if (Screen == null || _handle == IntPtr.Zero || _bitmap == null)
            return;

        BuildResponseScreen();

        uint bufferBytes = (uint)(_bitmap.PixelWidth * _bitmap.PixelHeight * 4);

        using (var stream = _bitmap.PixelBuffer.AsStream())
        {
            unsafe
            {
                byte[] buffer = new byte[bufferBytes];

                // Read current bitmap data
                stream.Seek(0, System.IO.SeekOrigin.Begin);
                stream.Read(buffer, 0, (int)bufferBytes);

                fixed (byte* pBuffer = buffer)
                {
                    // Set the buffer pointer - Update will render directly into it
                    Abi.SetTransfer(_handle, (IntPtr)pBuffer, bufferBytes);

                    // Update renders into the buffer
                    uint dirtyCount = 0;
                    Abi.Update(_handle, 0, ref _responseScreen, IntPtr.Zero, ref dirtyCount);
                }

                // Write modified buffer back to bitmap
                stream.Seek(0, System.IO.SeekOrigin.Begin);
                stream.Write(buffer, 0, (int)bufferBytes);
            }
        }

        _bitmap.Invalidate();
        _screenInitialized = true;
    }

    private void RefreshValues()
    {
        if (!_screenInitialized)
        {
            RefreshScreen();
        }
        if (!_screenInitialized) return;
        BuildResponseData();
        if (Screen == null || Screen.HardwareInfo == null || _handle == IntPtr.Zero || _bitmap == null)
        {
            return;
        }

        uint bufferBytes = (uint)(_bitmap.PixelWidth * _bitmap.PixelHeight * 4);

        using (var stream = _bitmap.PixelBuffer.AsStream())
        {
            unsafe
            {
                byte[] buffer = new byte[bufferBytes];

                // Read current bitmap data
                stream.Seek(0, System.IO.SeekOrigin.Begin);
                stream.Read(buffer, 0, (int)bufferBytes);
                fixed (byte* pBuffer = buffer)
                {
                    // Set the buffer pointer - Update will render directly into it
                    Abi.SetTransfer(_handle, (IntPtr)pBuffer, bufferBytes);
                    if (_requiresClear && _handle != IntPtr.Zero)
                    {
                        _requiresClear = false;
                        Abi.ClearData(_handle);
                    }
                    // Update renders into the buffer
                    uint dirtyCount = 0;
                    Abi.Update(_handle, 1, ref _responseData, IntPtr.Zero, ref dirtyCount);
                }

                // Write modified buffer back to bitmap
                stream.Seek(0, System.IO.SeekOrigin.Begin);
                stream.Write(buffer, 0, (int)bufferBytes);
            }
        }

        _bitmap.Invalidate();
    }

    private void Control_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0 && e.NewSize != _lastSize)
        {
            _lastSize = e.NewSize;
            InitializeBitmap();
            RefreshScreen();
            RefreshValues();
        }
    }

    private void Control_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeBitmap();
    }

    private void Control_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe when unloading
        if (Screen != null)
        {
            UnsubscribeFromScreen(Screen);
        }

        if (_handle != IntPtr.Zero)
        {
            Abi.Destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _screenInitialized = false;
    }
    public event EventHandler<ScreenViewHitEventArgs>? Hit;
    private void Control_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("Hit check started");
        if (_handle == IntPtr.Zero) return;

        var position = e.GetCurrentPoint(this).Position;
        double x = position.X;
        double y = position.Y;

         // Get the scaling factor based on ScalingMode
        double scaling = 1.0;
        if (ScalingMode == ScreenViewScalingMode.DpiAware)
        {
            // Get XamlRoot rasterization scale (WinUI3's DPI scaling)
            if (XamlRoot != null)
            {
                scaling = XamlRoot.RasterizationScale;
                _lastRasterizationScale = scaling;
            }
        }
        int pixelX = (int)(x * scaling);
        int pixelY = (int)(y * scaling);

        sbyte index = -1;
        Abi.HitTest(_handle, pixelX, pixelY, out index);
        System.Diagnostics.Debug.WriteLine($"Hit reported to be {((ScreenViewHitType)index).ToString()}");
        // Keep x, y in DIPs for the event args
        Hit?.Invoke(this, new ScreenViewHitEventArgs((ScreenViewHitType)index, x, y));
    }
    private void InitializeBitmap()
    {
        double actualWidth = ActualWidth;
        double actualHeight = ActualHeight;

        if (actualWidth == 0 || actualHeight == 0)
        {
            return;
        }

        // Get the scaling factor based on ScalingMode
        double scaling = 1.0;
        if (ScalingMode == ScreenViewScalingMode.DpiAware)
        {
            // Get XamlRoot rasterization scale (WinUI3's DPI scaling)
            if (XamlRoot != null)
            {
                scaling = XamlRoot.RasterizationScale;
                _lastRasterizationScale = scaling;
            }
        }
        // else ScalingMode.None uses scaling = 1.0 (pixel-perfect)

        // DIPs to pixels
        int width = (int)Math.Ceiling(actualWidth *scaling);
        int height = (int)Math.Ceiling(actualHeight * scaling);

        if (_bitmap != null)
        {
            if (_bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                _bitmap = null;
            }
        }

        if (_bitmap == null)
        {
            _bitmap = new WriteableBitmap(width, height);

            // Set the image source
            if (_image != null)
            {
                _image.Source = _bitmap;
            }
        }

        if (_handle != IntPtr.Zero)
        {
            Abi.SetDimensions(_handle, (ushort)width, (ushort)height);
        }

        _screenInitialized = false;
    }

    private static class Abi
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ResponseValue
        {
            public float Value;
            public float Scaled;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ResponseValueEntry
        {
            public ResponseValue Value1;
            public ResponseValue Value2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ResponseData
        {
            public ResponseValueEntry Top;
            public ResponseValueEntry Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ResponseColor
        {
            public byte A;
            public byte R;
            public byte G;
            public byte B;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ResponseScreenValueEntry
        {
            public ResponseColor Color;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public byte[] Suffix; // UTF-8
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ResponseScreenEntry
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] Label; // UTF-8
            public ResponseColor Color;
            public ResponseScreenValueEntry Value1;
            public ResponseScreenValueEntry Value2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ResponseScreenHeader
        {
            public sbyte Index;
            public byte Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ResponseScreen
        {
            public ResponseScreenHeader Header;
            public ResponseScreenEntry Top;
            public ResponseScreenEntry Bottom;
        }

        [DllImport("libespmon.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Create();

        [DllImport("libespmon.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Destroy(IntPtr handle);

        [DllImport("libespmon.dll", EntryPoint = "Update", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Update(IntPtr handle, byte cmd, [In] ref ResponseData response, [In] IntPtr in_dirties_buffer, [In, Out] ref uint in_out_dirties_count);

        [DllImport("libespmon.dll", EntryPoint = "Update", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Update(IntPtr handle, byte cmd, [In] ref ResponseScreen response, [In] IntPtr in_dirties_buffer, [In, Out] ref uint in_out_dirties_count);

        [DllImport("libespmon.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetTransfer(IntPtr handle, IntPtr buffer, uint bufferBytes);

        [DllImport("libespmon.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetDimensions(IntPtr handle, ushort width, ushort height);

        [DllImport("libespmon.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetGraph(IntPtr handle, byte isEnabled);

        [DllImport("libespmon.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetMonochrome(IntPtr handle, byte isEnabled);

        [DllImport("libespmon.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void HitTest(IntPtr handle, int x, int y, out sbyte hitIndex);

        [DllImport("libespmon.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ClearData(IntPtr handle);
    }
}