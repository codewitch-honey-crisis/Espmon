using Htcw;

using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
namespace SerialFrameDemo;

[SupportedOSPlatform("windows")]
internal class Program
{
    static bool _connected = false;
    static int _gpioNum = -1;
    static bool _waiting = false;
    static void Main(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("A serial port must be specified.");
        _connected = true;
        var session = new EspSerialSession(args[0],true);
        var buffer = new byte[InterfaceMaxSize.Value];
        Console.Error.WriteLine($"Connected to {args[0]}");
        session.ConnectionError += Session_ConnectionError;
        session.FrameReceived += Session_FrameReceived;
        session.FrameError += Session_FrameError;
        while (_connected)
        {
            while (_waiting)
            {
                Thread.Sleep(100);
            }
            
            Console.Write(">");
            var cmd = Console.ReadLine();
            if (cmd == null)
            {
                continue;
            }
            cmd = cmd.ToUpperInvariant();
            byte b;
            var sa = cmd.Split(' ');
            int bytesWritten;
            if (sa.Length > 0)
            {
                switch(sa[0])
                {
                    case "LOG":
                        Console.WriteLine(Encoding.UTF8.GetString(session.GetNextLogData()));
                        break;
                    case "RANDOM":
                        {
                            STRngMessage rng = new STRngMessage();

                            if (rng.TryWrite(buffer, out bytesWritten))
                            {
                                _waiting = true;
                                session.Send((byte)STMessageCommand.CmdRng, buffer.AsSpan(0,bytesWritten));
                            }
                        }
                        break;
                    case "GPIO":
                        {
                            switch (sa.Length)
                            {
                                case 0:
                                case 1:
                                    Console.Error.WriteLine("Not enough arguments");
                                    break;
                                case 2:
                                    if (!byte.TryParse(sa[1], CultureInfo.InvariantCulture.NumberFormat, out b))
                                    {
                                        Console.Error.WriteLine("The first argument must be a number");
                                        break;
                                    }
                                    _gpioNum = b;
                                    var gpioGet = new STGpioGetMessage();
                                    gpioGet.Mask = unchecked((ulong)(1UL << b));
                                    if (gpioGet.TryWrite(buffer, out bytesWritten))
                                    {
                                        _waiting = true;
                                        session.Send((byte)STMessageCommand.CmdGpioGet, buffer.AsSpan(0,bytesWritten));
                                    }
                                    break;
                                case 3:
                                    {
                                        if (!byte.TryParse(sa[1], CultureInfo.InvariantCulture.NumberFormat, out b))
                                        {
                                            Console.Error.WriteLine("The first argument must be a number");
                                            break;
                                        }
                                        var mode = false;
                                        var modeKind = STGpioMode.ModeInput;
                                        if (sa[2] != "ON" && sa[2] != "OFF")
                                        {
                                            if (sa[2] == "INPUT")
                                            {
                                                mode = true;
                                                modeKind = STGpioMode.ModeInput;
                                            }
                                            else if (sa[2] == "OUTPUT")
                                            {
                                                mode = true;
                                                modeKind = STGpioMode.ModeOutput;
                                            }
                                            else
                                            {
                                                Console.Error.WriteLine("The second argument must be on, off, input or output");
                                                break;
                                            }
                                        }
                                        if (!mode)
                                        {
                                            var gpioSet = new STGpioSetMessage();
                                            gpioSet.Mask = unchecked((ulong)(1UL << b));
                                            if (sa[2] == "ON")
                                            {
                                                gpioSet.Values = unchecked((ulong)(1UL << b));
                                            }
                                            if (gpioSet.TryWrite(buffer, out bytesWritten))
                                            {
                                                session.Send((byte)STMessageCommand.CmdGpioSet, buffer.AsSpan(0,bytesWritten));
                                            }
                                        }
                                        else
                                        {
                                            var gpioMode = new STGpioModeMessage();
                                            gpioMode.Gpio = b;
                                            Debug.WriteLine($"Gpio mode set for {b}");
                                            gpioMode.Mode = modeKind;
                                            if (gpioMode.TryWrite(buffer, out bytesWritten))
                                            {
                                                session.Send((byte)STMessageCommand.CmdGpioMode, buffer.AsSpan(0, bytesWritten));
                                            }
                                        }
                                    }
                                    break;
                                case 4:
                                    {
                                        if (!byte.TryParse(sa[1], CultureInfo.InvariantCulture.NumberFormat, out b))
                                        {
                                            Console.Error.WriteLine("The first argument must be a number");
                                            break;
                                        }

                                        if (sa[2] != "INPUT" && sa[2] != "OUTPUT")
                                        {
                                            Console.Error.WriteLine("The third argument is only valid when the second is input or output");
                                            break;
                                        }
                                        var modeKind = STGpioMode.ModeInput;
                                        if (sa[2] == "INPUT")
                                        {
                                            if (sa[3] == "PULLUP")
                                            {
                                                modeKind = STGpioMode.ModeInputPullup;
                                            }
                                            else if (sa[3] == "PULLDOWN")
                                            {
                                                modeKind = STGpioMode.ModeInputPulldown;
                                            }
                                            else
                                            {
                                                Console.Error.WriteLine("The third argument must be pullup or pulldown when specified if the second is input");
                                                break;
                                            }
                                        }
                                        else if (sa[2] == "OUTPUT")
                                        {

                                            modeKind = STGpioMode.ModeOutput;
                                            if (sa[3] == "OD")
                                            {
                                                Console.Error.WriteLine("The third argument must be OD when specified if the second is output");
                                                modeKind = STGpioMode.ModeOutputOpenDrain;
                                            }
                                        }
                                        else
                                        {
                                            Console.Error.WriteLine("The second argument must be on, off, input or output");
                                            break;
                                        }


                                        var gpioMode = new STGpioModeMessage();
                                        gpioMode.Gpio = b;
                                        gpioMode.Mode = modeKind;
                                        if (gpioMode.TryWrite(buffer, out bytesWritten))
                                        {
                                            session.Send((byte)STMessageCommand.CmdGpioMode, buffer.AsSpan(0,bytesWritten));
                                        }

                                    }
                                    break;
                                default:
                                    Console.Error.WriteLine("Too many arguments");
                                    break;
                            }
                            break;
                        }
                    case "HELP":
                    case "?":
                        Console.Error.WriteLine("LOG      Gets the most recent log entries since the last time log was used");
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("RANDOM   Gets a value from the ESP32 hardware RNG");
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("GPIO     Gets or sets the GPIO level and mode");
                        Console.Error.WriteLine("    GPIO <pin number> retrieves the current level");
                        Console.Error.WriteLine("    GPIO <pin number> ON sets the level high");
                        Console.Error.WriteLine("    GPIO <pin number> OFF sets the level low");
                        Console.Error.WriteLine("    GPIO <pin number> OUTPUT sets the mode to ouput");
                        Console.Error.WriteLine("    GPIO <pin number> OUTPUT OD sets the mode to ouput open drain");
                        Console.Error.WriteLine("    GPIO <pin number> INPUT sets the mode to input floating");
                        Console.Error.WriteLine("    GPIO <pin number> INPUT PULLUP sets the mode to input w/ pullup");
                        Console.Error.WriteLine("    GPIO <pin number> INPUT PULLDOWN sets the mode to input w/ pulldown");
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("QUIT\tExits the application");
                        Console.Error.WriteLine();
                        break;
                    case "QUIT":
                        {
                            _connected = false;
                            session.Dispose();
                        }
                        break;
                    default:
                        Console.Error.WriteLine("Unrecognized command. Type HELP or ? for available commands");
                        Console.Error.WriteLine();
                        break;
                }
            }
        }
        Console.Error.WriteLine($"Disconnected");

    }

    private static void Session_ConnectionError(object? sender, EventArgs e)
    {
        _connected = false;
        Thread.MemoryBarrier();
    }

    private static void Session_FrameError(object? sender, FrameReceivedEventArgs e)
    {
        Console.Error.WriteLine("Frame error");
    }

    private static void Session_FrameReceived(object? sender, FrameReceivedEventArgs e)
    {
        switch((STMessageCommand)e.Command)
        {
            case STMessageCommand.CmdRngResponse:
                {
                    if (STRngResponseMessage.TryRead(e.Data, out var rng, out var _))
                    {
                        Console.WriteLine("RNG Random Response:");
                        Console.WriteLine($"  {rng.Value}");
                    }
                }
                break;
            case STMessageCommand.CmdGpioGetResponse:
                if (STGpioGetResponseMessage.TryRead(e.Data, out var gpioGet, out var _))
                {
                    Console.WriteLine("GPIO Get Response:");
                    var state = 0 == (gpioGet.Values & (1UL << _gpioNum)) ? "off" : "on";
                    Console.WriteLine($"  GPIO {_gpioNum} is {state}");
                }
                break;
            default:
                Console.WriteLine($"Unexpected frame {e.Command} received");
                break;
        }
        Console.WriteLine();
        _waiting = false;
        Thread.MemoryBarrier();
    }
}
