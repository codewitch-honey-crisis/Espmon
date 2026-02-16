using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace EL
{
	partial class EspLink
	{
		/// <summary>
		/// A reset strategy
		/// </summary>
		/// <param name="port">The target serial port</param>
		/// <param name="cancellationToken">The token that can be used to cancel the request</param>
		/// <returns>True if the reset was successful, otherwise false</returns>
		public delegate Task<bool> ResetStrategyAsync(SerialPort port,CancellationToken cancellationToken);
        /// <summary>
        /// A reset strategy
        /// </summary>
        /// <param name="port">The target serial port</param>
        /// <returns>True if the reset was successful, otherwise false</returns>
        public delegate bool ResetStrategy(SerialPort port);
        /// <summary>
        /// Do not reset
        /// </summary>
        public static readonly ResetStrategyAsync NoResetStrategyAsync = new ResetStrategyAsync(NoResetImplAsync);
		/// <summary>
		/// Hard reset the device (doesn't enter bootloader/will exit bootloader)
		/// </summary>
		public static readonly ResetStrategyAsync HardResetStrategyAsync = new ResetStrategyAsync(HardResetImplAsync);
		/// <summary>
		/// Hard reset the device (USB)
		/// </summary>
		public static readonly ResetStrategyAsync HardResetUsbStrategyAsync = new ResetStrategyAsync(HardResetUsbImplAsync);
		/// <summary>
		/// Reset the device using Dtr/Rts to force the MCU into bootloader mode
		/// </summary>
		public static readonly ResetStrategyAsync ClassicResetStrategyAsync = new ResetStrategyAsync(ClassicResetImplAsync);
        /// <summary>
        /// Reset the device using Dtr/Rts to force the MCU into bootloader mode (USB Serial JTAG)
        /// </summary>
        public static readonly ResetStrategyAsync SerialJtagResetStrategyAsync = new ResetStrategyAsync(SerialJtagResetImplAsync);
        /// <summary>
        /// Do not reset
        /// </summary>
        public static readonly ResetStrategy NoResetStrategy = new ResetStrategy(NoResetImpl);
        /// <summary>
        /// Hard reset the device (doesn't enter bootloader/will exit bootloader)
        /// </summary>
        public static readonly ResetStrategy HardResetStrategy = new ResetStrategy(HardResetImpl);
        /// <summary>
        /// Hard reset the device (USB)
        /// </summary>
        public static readonly ResetStrategy HardResetUsbStrategy = new ResetStrategy(HardResetUsbImpl);
        /// <summary>
        /// Reset the device using Dtr/Rts to force the MCU into bootloader mode
        /// </summary>
        public static readonly ResetStrategy ClassicResetStrategy = new ResetStrategy(ClassicResetImpl);
        /// <summary>
        /// Reset the device using Dtr/Rts to force the MCU into bootloader mode (USB Serial JTAG)
        /// </summary>
        public static readonly ResetStrategy SerialJtagResetStrategy = new ResetStrategy(SerialJtagResetImpl);
		static async Task<bool> SerialJtagResetImplAsync(SerialPort port, CancellationToken cancellationToken)
		{
			if (port == null || !port.IsOpen) { return false; }
			port.RtsEnable = false;
			port.DtrEnable = port.DtrEnable;
			port.DtrEnable = false;
			await Task.Delay(100, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			port.DtrEnable = true;
			port.RtsEnable = false;
			port.DtrEnable = port.DtrEnable;
			await Task.Delay(100, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			port.RtsEnable = true;
			port.DtrEnable = port.DtrEnable;
			port.DtrEnable = false;
			port.RtsEnable = true;
			port.DtrEnable = port.DtrEnable;
			await Task.Delay(100, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			port.DtrEnable = false;
			port.RtsEnable = false;
			port.DtrEnable = port.DtrEnable;

			return true;
		}
		async static Task<bool> HardResetImplIntAsync(SerialPort port, bool isUsb, CancellationToken cancellationToken)
		{
			if (port == null || !port.IsOpen) { return false; }
			port.RtsEnable = true;
			port.DtrEnable = port.DtrEnable;
			if (isUsb)
			{
				await Task.Delay(200,cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				port.RtsEnable = false;
				port.DtrEnable = port.DtrEnable;
				await Task.Delay(200,cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
			}
			else
			{
				await Task.Delay(100,cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				port.RtsEnable = false;
				port.DtrEnable = port.DtrEnable;

			}

			return true;
		}
		static async Task<bool> NoResetImplAsync(SerialPort port, CancellationToken cancellationToken)
		{
			await Task.CompletedTask;
			return true;
		}
		static async Task<bool> HardResetImplAsync(SerialPort port, CancellationToken cancellationToken)
		{
			return await HardResetImplIntAsync(port, false,cancellationToken);
		}
		static async Task<bool> HardResetUsbImplAsync(SerialPort port, CancellationToken cancellationToken)
		{
			return await HardResetImplIntAsync(port, true,cancellationToken);
		}
		static async Task<bool> ClassicResetImplAsync(SerialPort port, CancellationToken cancellationToken)
		{
			if (port == null || !port.IsOpen) { return false; }
			port.DtrEnable = false;
			port.RtsEnable = true;
			port.DtrEnable = port.DtrEnable;
			await Task.Delay(50,cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			port.DtrEnable = true;
			port.RtsEnable = false;
			port.DtrEnable = port.DtrEnable;
			await Task.Delay(350,cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			port.DtrEnable = false;
			return true;
		}
        static bool SerialJtagResetImpl(SerialPort port)
        {
            if (port == null || !port.IsOpen) { return false; }
            port.RtsEnable = false;
            port.DtrEnable = port.DtrEnable;
            port.DtrEnable = false;
            Task.Delay(100).Wait();
            port.DtrEnable = true;
            port.RtsEnable = false;
            port.DtrEnable = port.DtrEnable;
            Task.Delay(100).Wait();
            port.RtsEnable = true;
            port.DtrEnable = port.DtrEnable;
            port.DtrEnable = false;
            port.RtsEnable = true;
            port.DtrEnable = port.DtrEnable;
            Task.Delay(100).Wait();
            port.DtrEnable = false;
            port.RtsEnable = false;
            port.DtrEnable = port.DtrEnable;
            return true;
        }
        static bool HardResetImplInt(SerialPort port, bool isUsb)
        {
            if (port == null || !port.IsOpen) { return false; }
            port.RtsEnable = true;
            port.DtrEnable = port.DtrEnable;
            if (isUsb)
            {
                Task.Delay(200).Wait();
                port.RtsEnable = false;
                port.DtrEnable = port.DtrEnable;
                Task.Delay(200).Wait();
            }
            else
            {
                Task.Delay(100).Wait();
                port.RtsEnable = false;
                port.DtrEnable = port.DtrEnable;

            }

            return true;
        }
        static bool NoResetImpl(SerialPort port)
        {
            return true;
        }
        static bool HardResetImpl(SerialPort port)
        {
            return HardResetImplInt(port, false);
        }
        static bool HardResetUsbImpl(SerialPort port)
        {
            return HardResetImplInt(port, true);
        }
		static bool ClassicResetImpl(SerialPort port)
		{
			if (port == null || !port.IsOpen) { return false; }
			port.DtrEnable = false;
			port.RtsEnable = true;
			port.DtrEnable = port.DtrEnable;
			Task.Delay(50).Wait();
			port.DtrEnable = true;
			port.RtsEnable = false;
			port.DtrEnable = port.DtrEnable;
			Task.Delay(550).Wait();
			port.DtrEnable = false;
			return true;
		}

        /// <summary>
        /// Terminates any connection and asynchronously reset the device.
        /// </summary>
        /// <param name="strategy">The reset strategy to use, or null to hard reset</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to cancel the operation</param>
        /// <exception cref="IOException">Unable to communicate with the device</exception>
        public async Task ResetAsync(ResetStrategyAsync? strategy = null, CancellationToken cancellationToken = default)
        {
			Close();
			try
			{
				if (strategy == null)
				{
					strategy = HardResetStrategyAsync;
				}
				SerialPort? port = GetOrOpenPort(true);
				if (port == null) throw new IOException("Could not open serial port");
				port.Handshake = Handshake.None;
				DiscardInput();

				// On targets with USB modes, the reset process can cause the port to
				// disconnect / reconnect during reset.
				// This will retry reconnections on ports that
				// drop out during the reset sequence.
				for (var i = 2; i >= 0 && !cancellationToken.IsCancellationRequested; --i)
				{
					{
                        if (port == null) throw new IOException("Could not open serial port");
						if (strategy != null)
						{
							var b = await strategy.Invoke(port, cancellationToken);
							if (b)
							{
								return;
							}
						}
					}
				}
				cancellationToken.ThrowIfCancellationRequested();
				throw new IOException("Unable to reset device");
				
			}
			finally
			{
				Close();
			}
		}
		/// <summary>
		/// Terminates any connection and reset the device.
		/// </summary>
		/// <param name="strategy">The reset strategy to use, or null to hard reset</param>
		/// <exception cref="IOException">Unable to communicate with the device</exception>
		public void Reset(ResetStrategy? strategy = null)
		{
            Close();
            try
            {
                if (strategy == null)
                {
                    strategy = HardResetStrategy;
                }
                SerialPort? port = GetOrOpenPort(true);
                if (port == null) throw new IOException("Could not open serial port");
                port.Handshake = Handshake.None;
                DiscardInput();

                // On targets with USB modes, the reset process can cause the port to
                // disconnect / reconnect during reset.
                // This will retry reconnections on ports that
                // drop out during the reset sequence.
                for (var i = 2; i >= 0; --i)
                {
                    {
                        if (port == null) throw new IOException("Could not open serial port");
                        if (strategy != null)
                        {
                            var b = strategy.Invoke(port);
                            if (b)
                            {
                                return;
                            }
                        }
                    }
                }
                throw new IOException("Unable to reset device");

            }
            finally
            {
                Close();
            }
        }
	}
}
