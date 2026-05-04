using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
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
[SupportedOSPlatform("windows")]
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
    ScreenDataEventArgs? _lastDataArgs = null;
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
            view._bitmap = null;
            view.EnsureBitmap();
            view.RefreshScreen();
        }
    }
    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(
            nameof(Session),
            typeof(SessionController),
            typeof(ScreenView),
            new PropertyMetadata(null, OnSessionChanged));

    public SessionController? Session
    {
        get => (SessionController?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    private void UnsubscribeFromSession(SessionController session)
    {
        session.ScreenCleared -= Session_ScreenCleared;
        session.ScreenChanged -= Session_ScreenChanged;
        session.ScreenData -= Session_ScreenData;
    }
    private void SubscribeToSession(SessionController session)
    {
        session.ScreenCleared += Session_ScreenCleared;
        session.ScreenChanged += Session_ScreenChanged;
        session.ScreenData += Session_ScreenData;
    }


    private void Session_ScreenData(object sender, ScreenDataEventArgs args)
    {
        _lastDataArgs = args;
        RefreshValues();
    }

    private void Session_ScreenChanged(object sender, ScreenChangedEventArgs args)
    {
        _screenInitialized = false;
        RefreshScreen();
    }

    private void Session_ScreenCleared(object sender, EventArgs args)
    {
        _screenInitialized = false;
        ClearScreen();
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScreenView view)
        {
            // Unsubscribe from old screen tree
            if (e.OldValue is SessionController oldSession)
            {
                view.UnsubscribeFromSession(oldSession);
            }

            // Subscribe to new screen tree
            if (e.NewValue is SessionController newSession)
            {
                view.SubscribeToSession(newSession);
                view._requiresClear = true;
            }
            view._screenInitialized = false;
        }
    }


    //public void Refresh()
    //{
    //    if (Session != null)
    //    { 
    //       Session.Refresh();
    //    }
    //}

    
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
        if (Session == null || Session.Screen==null) return;
        var scr = Session.Screen;
        _responseScreen.Header.Flags = 0;
        if (scr.Top != null)
        {
            if (scr.Top.Label != null)
            {
                var bytes = Encoding.UTF8.GetBytes(scr.Top.Label);
                bytes.CopyTo(_responseScreen.Top.Label, 0);
                _responseScreen.Top.Label[bytes.Length] = 0;
            }
            else
            {
                _responseScreen.Top.Label[0] = 0;
            }
            _responseScreen.Top.Color = ToColor(scr.Top.Color);

            if (scr.Top.Value1 != null)
            {
                
                if (!string.IsNullOrEmpty(scr.Top.Value1.Unit))
                {
                    var bytes = Encoding.UTF8.GetBytes(scr.Top.Value1.Unit);
                    bytes.CopyTo(_responseScreen.Top.Value1.Suffix, 0);
                    _responseScreen.Top.Value1.Suffix[bytes.Length] = 0;
                }
                else
                {
                    _responseScreen.Top.Value1.Suffix[0] = 0;
                }
                _responseScreen.Top.Value1.Color = ToColor(scr.Top.Value1.Color);
                if (scr.Top.Value1.HasGradient)
                {
                    _responseScreen.Header.Flags |= (1 << 0);
                }
            }
            if (scr.Top.Value2 != null)
            {
                if (!string.IsNullOrEmpty(scr.Top.Value2.Entry.Unit))
                {
                    var bytes = Encoding.UTF8.GetBytes(scr.Top.Value2.Unit);
                    bytes.CopyTo(_responseScreen.Top.Value2.Suffix, 0);
                    _responseScreen.Top.Value2.Suffix[bytes.Length] = 0;
                }
                else
                {
                    _responseScreen.Top.Value2.Suffix[0] = 0;
                }
                _responseScreen.Top.Value2.Color = ToColor(scr.Top.Value2.Color);
                if (scr.Top.Value2.HasGradient)
                {
                    _responseScreen.Header.Flags |= (1 << 1);
                }
            }
        }
        if (scr.Bottom != null)
        {
            if (scr.Bottom.Label != null)
            {
                var bytes = Encoding.UTF8.GetBytes(scr.Bottom.Label);
                bytes.CopyTo(_responseScreen.Bottom.Label, 0);
                _responseScreen.Bottom.Label[bytes.Length] = 0;
            }
            else
            {
                _responseScreen.Bottom.Label[0] = 0;
            }
            _responseScreen.Bottom.Color = ToColor(scr.Bottom.Color);
            if (scr.Bottom.Value1 != null)
            {
                if (!string.IsNullOrEmpty(scr.Bottom.Value1.Entry.Unit))
                {
                    var bytes = Encoding.UTF8.GetBytes(scr.Bottom.Value1.Unit);
                    bytes.CopyTo(_responseScreen.Bottom.Value1.Suffix, 0);
                    _responseScreen.Bottom.Value1.Suffix[bytes.Length] = 0;
                }
                else
                {
                    _responseScreen.Bottom.Value1.Suffix[0] = 0;
                }
                _responseScreen.Bottom.Value1.Color = ToColor(scr.Bottom.Value1.Color);
                if (scr.Bottom.Value1.HasGradient)
                {
                    _responseScreen.Header.Flags |= (1 << 2);
                }
            }
            if (scr.Bottom.Value2 != null)
            {
                if (!string.IsNullOrEmpty(scr.Bottom.Value2.Entry.Unit))
                {
                    var bytes = Encoding.UTF8.GetBytes(scr.Bottom.Value2.Unit);
                    bytes.CopyTo(_responseScreen.Bottom.Value2.Suffix, 0);
                    _responseScreen.Bottom.Value2.Suffix[bytes.Length] = 0;
                }
                else
                {
                    _responseScreen.Bottom.Value2.Suffix[0] = 0;
                }
                _responseScreen.Bottom.Value2.Color = ToColor(scr.Bottom.Value2.Color);
                if (scr.Bottom.Value2.HasGradient)
                {
                    _responseScreen.Header.Flags |= (1 << 3);
                }
            }
        }
    }

    private void BuildResponseData()
    {
        if (_lastDataArgs==null) return;

        _responseData.Top.Value1.Value = _lastDataArgs.TopValue1;
        _responseData.Top.Value1.Scaled = _lastDataArgs.TopScaled1;
           
        _responseData.Top.Value2.Value = _lastDataArgs.TopValue2;
        _responseData.Top.Value2.Scaled = _lastDataArgs.TopScaled2;

        _responseData.Bottom.Value1.Value = _lastDataArgs.BottomValue1;
        _responseData.Bottom.Value1.Scaled = _lastDataArgs.BottomScaled1;

        _responseData.Bottom.Value2.Value = _lastDataArgs.BottomValue2;
        _responseData.Bottom.Value2.Scaled = _lastDataArgs.BottomScaled2;

    }
    private void ClearScreen()
    {
        if (!_screenInitialized) return;
        if (_handle == IntPtr.Zero) return;
        Abi.ClearData(_handle);
    }
    private void RefreshScreen()
    {
        if (_screenInitialized) return;
        if ( _handle == IntPtr.Zero || !EnsureBitmap() || _bitmap==null)
            return;
        Abi.ClearData(_handle);
        BuildResponseScreen();

        uint bufferBytes = (uint)(_bitmap.PixelWidth * _bitmap.PixelHeight * 4);

        using (var stream = _bitmap.PixelBuffer.AsStream())
        {
            unsafe
            {
                byte[] buffer = new byte[bufferBytes];

                // Read current bitmap data
                stream.Seek(0, System.IO.SeekOrigin.Begin);
                stream.ReadExactly(buffer, 0, (int)bufferBytes);

                fixed (byte* pBuffer = buffer)
                {
                    // Set the buffer pointer - Update will render directly into it
                    Abi.SetTransfer(_handle, (IntPtr)pBuffer, bufferBytes);

                    // Update renders into the buffer
                    uint dirtyCount = 0;
                    Abi.Update(_handle, 1, ref _responseScreen, IntPtr.Zero, ref dirtyCount);
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
        if (_handle == IntPtr.Zero || !EnsureBitmap())
            return;

        if (!_screenInitialized)
        {
            RefreshScreen();
        }
        if (!_screenInitialized) return;
        BuildResponseData();
        if ( _handle == IntPtr.Zero || _bitmap == null)
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
                stream.ReadExactly(buffer, 0, (int)bufferBytes);
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
                    Abi.Update(_handle,2, ref _responseData, IntPtr.Zero, ref dirtyCount);
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
            _bitmap = null;
            EnsureBitmap();
            RefreshScreen();
            RefreshValues();
        }
    }

    private void Control_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureBitmap();
    }

    private void Control_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe when unloading
        if (Session != null)
        {
            UnsubscribeFromSession(Session);
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
    private bool EnsureBitmap()
    {
        if(_bitmap!=null) return true;
        double actualWidth = ActualWidth;
        double actualHeight = ActualHeight;

        if (actualWidth == 0 || actualHeight == 0)
        {
            return false;
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
        return true;
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