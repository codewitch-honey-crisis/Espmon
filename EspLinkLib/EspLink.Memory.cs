using System;
using System.Threading;
using System.Threading.Tasks;

namespace EL
{
	partial class EspLink
    {
        async Task<(uint Value, byte[] Data)> BeginWriteMemoryAsync(uint size, uint blocks, uint blocksize, uint offset, int timeout = -1, CancellationToken cancellationToken = default)
        {
            if (Device == null) throw new InvalidOperationException("The device is not connected");
            //"""Start downloading an application image to RAM"""
            // TODO: check we're not going to overwrite a running stub with this data
            var pck = new byte[16];
            PackUInts(pck, 0, new uint[] { size, blocks, blocksize, offset });
            return await CheckCommandAsync(
                "enter RAM download mode",
                Device.ESP_MEM_BEGIN,
                pck, 0, timeout
, cancellationToken);
        }
        async Task<(uint Value, byte[] Data)> WriteMemoryBlockAsync(byte[] data, int index, int length, uint seq, int timeout = -1, CancellationToken cancellationToken = default)
        {
            if (Device == null) throw new InvalidOperationException("The device is not connected");
            // """Send a block of an image to RAM"""
            var pck = new byte[16 + length];
            PackUInts(pck, 0, new uint[] { (uint)length, seq, 0, 0 });
            Array.Copy(data, index, pck, 16, length);

            return await CheckCommandAsync(
            "write to target RAM",
            Device.ESP_MEM_DATA,
            pck, Checksum(pck, 16, length), timeout, cancellationToken);
        }
        async Task FinishWriteMemoryAsync(uint entrypoint = 0, CancellationToken cancellationToken = default)
        {
            if (Device == null) throw new InvalidOperationException("The device is not connected");
            // """Leave download mode and run the application"""
            //# Sending ESP_MEM_END usually sends a correct response back, however sometimes
            //# (with ROM loader) the executed code may reset the UART or change the baud rate
            //# before the transmit FIFO is empty. So in these cases we set a short timeout
            //# and ignore errors.
            var timeout2 = IsStub ? 3000 : 200;
            var data = new byte[8];
            PackUInts(data, 0, new uint[] { entrypoint == 0 ? (uint)1 : 0, entrypoint });
            try
            {
                await CheckCommandAsync("leave RAM download mode", Device.ESP_MEM_END, data, 0, timeout2, cancellationToken);
            }
            catch
            {
                if (IsStub) throw;
            }
        }
	}
}
