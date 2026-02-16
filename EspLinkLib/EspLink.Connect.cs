using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace EL
{
	/// <summary>
	/// The connection mode
	/// </summary>
	public enum EspConnectMode
	{
		/// <summary>
		/// Standard attempt to reset into the bootloader and sync
		/// </summary>
		Default = 0,
		/// <summary>
		/// Do not reset first (assumes already in bootloader)
		/// </summary>
		NoReset = 1,
		/// <summary>
		/// Do not sync
		/// </summary>
		NoSync = 2,
		/// <summary>
		/// Do not reset or sync
		/// </summary>
		NoResetNoSync = 3,
		/// <summary>
		/// Use USB reset technique
		/// </summary>
		UsbReset=4
	}
	partial class EspLink
	{
		static readonly byte[] _syncPacket = new byte[] { 0x07, 0x07, 0x12, 0x20, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55 };
		static readonly Regex _bootloaderRegex = new Regex(@"boot:0x([0-9a-fA-F]+)(.*waiting for download)?", RegexOptions.CultureInvariant);
		bool _inBootloader = false;
		void CheckReady(bool checkConnected = true)
		{
			if (checkConnected)
			{
				if (Device == null)
				{
					throw new InvalidOperationException("The device is not connected");
				}
			}
			if (!_inBootloader)
			{
				throw new InvalidOperationException("The bootloader is not entered");
			}
		}

		async Task SyncAsync(int timeout, int prog, IProgress<int>? progress, CancellationToken cancellationToken = default)
        {
			var cmdRet = await CommandAsync(0x08, _syncPacket, 0, timeout, cancellationToken);
            Debug.WriteLine("ESP Link: Sent sync command");
            progress?.Report(prog++);
			int stubDetected = cmdRet.Value == 0 ? 1 : 0;
			Exception? lastEx = null;
			for (var i = 0; i < 7; ++i)
			{
				try
				{
					cmdRet = await CommandAsync(-1, null, 0, timeout, cancellationToken);
					progress?.Report(prog++);
					stubDetected &= cmdRet.Value == 0 ? 1 : 0;
				}
				catch (TimeoutException ex)
				{
					lastEx = ex;
				}
			}
			if (lastEx != null)
			{
				throw lastEx;
			}
		}
		struct StrategyEntry
		{
			public readonly ResetStrategyAsync? ResetStrategyAsync;
            public readonly ResetStrategy? ResetStrategy;
            public readonly int Delay;
			public StrategyEntry(ResetStrategyAsync resetStrategy, int delay = 0)
			{
				ResetStrategyAsync = resetStrategy;
				Delay = delay;
			}
            public StrategyEntry(ResetStrategy resetStrategy, int delay = 0)
            {
                ResetStrategy = resetStrategy;
                Delay = delay;
            }
        }
		
		StrategyEntry[] BuildConnectStrategyAsync(EspConnectMode connectMode,int defaultResetDelay=50,int extraDelay=550)
		{
			// Serial JTAG USB
			if(connectMode==EspConnectMode.UsbReset || IsUsbSerialJtag)
			{
				return [new StrategyEntry(SerialJtagResetStrategyAsync)];
			}
			return [new StrategyEntry(ClassicResetStrategyAsync, defaultResetDelay), new StrategyEntry(ClassicResetStrategyAsync, extraDelay)];

		}
		async Task ConnectAttemptAsync(StrategyEntry strategy, EspConnectMode mode, int prog, int timeout = -1, IProgress<int>? progress=null, CancellationToken cancellationToken = default)
        {
			if (mode == EspConnectMode.NoResetNoSync)
			{
				return;
			}

			var bootLogDetected = false;
			var downloadMode = false;
			ushort bootMode = 0;
			var port = GetOrOpenPort(true);
			if (port == null) throw new IOException("Could not open serial port");
			progress?.Report(prog++);

			if (mode != EspConnectMode.NoReset)
			{
				DiscardInput();
				if (strategy.ResetStrategyAsync != null)
				{
					await strategy.ResetStrategyAsync.Invoke(port, cancellationToken);
				}
				progress?.Report(prog++);
				while(port.BytesToRead<10)
					await Task.Delay(100);
				var str = Encoding.ASCII.GetString(ReadExistingInput());
				var match = _bootloaderRegex.Match(str);
				if (match.Success && ushort.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture.NumberFormat, out bootMode))
				{
					Debug.WriteLine("ESP Link: Boot log detected");
					bootLogDetected = true;

					if (match.Groups.Count > 2)
					{
                        Debug.WriteLine("ESP Link: Download mode detected");
                        downloadMode = true;
					}
				}
			}
			Exception? ex = null;
			for (var i = 0; i < 5; ++i)
			{
				progress?.Report(prog++);
				try
				{
					DiscardInput();
                    Debug.WriteLine("ESP Link: Sync attempt");
                    await SyncAsync(timeout, prog, progress, cancellationToken);
					return;
				}
				catch (Exception e)
				{
					ex = e;
				}
			}
			if (ex != null) { throw ex; }
			if (bootLogDetected)
			{
				if (downloadMode)
				{
					throw new IOException("Download mode detected, but getting no sync reply");
				}
				throw new IOException("Wrong boot mode detected. MCU must be in download mode");
			}
			
		}
        /// <summary>
        /// Connects to an Espressif MCU device
        /// </summary>
        /// <param name="mode">The <see cref="EspConnectMode"/> to use</param>
        /// <param name="attempts">The number of attempts to make</param>
        /// <param name="detecting">True if only detecting, and no actual connection should be made</param>
        /// <param name="timeout">The timeout for each suboperation (not the total)</param>
        /// <param name="progress">A <see cref="IProgress{Int32}"/> implementation to report progress back</param>
        public void Connect(EspConnectMode mode=EspConnectMode.Default, int attempts=3, bool detecting = false, int timeout = -1, IProgress<int>? progress = null)
		{
			ConnectAsync(mode, attempts, detecting, timeout, progress, CancellationToken.None).Wait();
		}
		/// <summary>
		/// Asynchronously connects to an Espressif MCU device
		/// </summary>
		/// <param name="mode">The <see cref="EspConnectMode"/> to use</param>
		/// <param name="attempts">The number of attempts to make</param>
		/// <param name="detecting">True if only detecting, and no actual connection should be made</param>
		/// <param name="timeout">The timeout for each suboperation (not the total)</param>
		/// <param name="progress">A <see cref="IProgress{Int32}"/> implementation to report progress back</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> to use which allows for cancelling the operation</param>
		public async Task ConnectAsync(EspConnectMode mode, int attempts, bool detecting, int timeout = -1, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
			var strategy = BuildConnectStrategyAsync(mode);
			int strategyIndex = 0;
			if(attempts<strategy.Length)
			{
				attempts = strategy.Length;
			}
			int prog = int.MinValue;
			progress?.Report(prog++);
			Exception? lastErr = null;
			var connected = false;
			for (var i = 0; i < attempts; ++i)
			{
				try
				{
					progress?.Report(prog++);
					await ConnectAttemptAsync(strategy[strategyIndex], mode, prog, timeout == -1 ? 5000 : timeout, progress, cancellationToken);
					++strategyIndex;
					if(strategyIndex == strategy.Length)
					{
						strategyIndex = 0;
					}
					connected = true;
					break;
				}
				catch (Exception ex)
				{
					lastErr = ex;
				}
			}
			if (!connected)
			{
				if(lastErr != null)
					throw lastErr;
				else
				{
					throw new Exception("Unknown error attempting to connect");
				}
			}
			DiscardInput();
			if (!detecting)
			{
				var magic = await ReadRegAsync(0x40001000, timeout, cancellationToken);
				CreateDevice(magic);
                if (Device == null) throw new InvalidOperationException("No device created");
                _inBootloader = true;
				await Device.ConnectAsync(DefaultTimeout, cancellationToken);
				if (_baudRate != 115200)
				{
					await SetBaudRateAsync(_baudRate, timeout, cancellationToken);
				}
			}
		}
	}
}
