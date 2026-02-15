using System;
using System.Threading.Tasks;
using System.Threading;

namespace EL
{
	partial class EspLink
	{
		internal async Task<uint> ReadRegAsync(uint address, int timeout = -1, CancellationToken cancellationToken = default)
        {
			var data = BitConverter.GetBytes(address);
			if (!BitConverter.IsLittleEndian)
			{
				Array.Reverse(data);
			}
			return await CommandResultAsync(Device != null ? Device.ESP_READ_REG : 0x0A, data, 0, timeout, cancellationToken);
		}

		internal async Task<(uint Value, byte[] Data)> WriteRegAsync(uint address, uint value, uint mask = 0xFFFFFFFF, uint delayUSec = 0, uint delayAfterUSec = 0, int timeout = -1, CancellationToken cancellationToken = default)
        {
            if (Device == null) throw new InvalidOperationException("The device is not connected");
            var data = new byte[delayAfterUSec == 0 ? 16 : 32];
			PackUInts(data, 0, new uint[] { address, value, mask, delayUSec });
			if (delayAfterUSec != 0)
			{
				PackUInts(data, 16, new uint[] { Device.UART_DATE_REG_ADDR, 0, 0, delayAfterUSec });
			}
			return await CheckCommandAsync("write target memory", Device != null ? Device.ESP_WRITE_REG : 0x09, data, 0, timeout, cancellationToken);
		}
	}
}
