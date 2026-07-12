using System.IO.Pipes;

namespace Espmon.Service
{
    public class PipeFrame
    {
        public static (byte Cmd, byte[] Payload) ReadFrame(PipeStream pipe)
        {
            var header = new byte[5];
            pipe.ReadExactly(header);
            var cmd = header[0];
            var len = BitConverter.ToInt32(header, 1);
            if (len < 0 || len > 64 * 1024 * 1024)
                throw new InvalidDataException($"Invalid frame length {len}");
            var buf = new byte[len];
            if (len > 0)
                pipe.ReadExactly(buf);
            return (cmd, buf);
        }
        public static (byte Cmd, byte[] Payload) ReadFrameOrTimeout(NamedPipeClientStream pipe, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            // On timeout, dispose the handle out from under the blocked read.
            // ReadFrame then throws, which we translate to TimeoutException.
            using var reg = cts.Token.Register(() => { try { pipe.Dispose(); } catch { } });
            try
            {
                return PipeFrame.ReadFrame(pipe);
            }
            catch when (cts.IsCancellationRequested)
            {
                throw new TimeoutException("Espmon.Service did not respond within the timeout.");
            }
        }
        public static void WriteFrame(PipeStream pipe, byte cmd, byte[]? payload)
        {
            var len = payload?.Length ?? 0;
            var header = new byte[5];
            header[0] = cmd;
            BitConverter.GetBytes(len).CopyTo(header, 1);
            pipe.Write(header);
            if (len > 0)
                pipe.Write(payload!);
            pipe.Flush();
        }
        public static async Task<(byte Cmd, byte[] Payload)> ReadFrameAsync(PipeStream pipe, CancellationToken ct)
        {
            var header = new byte[5];
            await pipe.ReadExactlyAsync(header, ct);
            var cmd = header[0];
            var len = BitConverter.ToInt32(header, 1);
            if (len < 0 || len > 64 * 1024 * 1024)
                throw new InvalidDataException($"Invalid frame length {len}");
            var buf = new byte[len];
            if (len > 0)
                await pipe.ReadExactlyAsync(buf, ct);
            return (cmd, buf);
        }

        public static async Task WriteFrameAsync(PipeStream pipe, byte cmd, byte[]? payload, CancellationToken ct)
        {
            var len = payload?.Length ?? 0;
            var header = new byte[5];
            header[0] = cmd;
            BitConverter.GetBytes(len).CopyTo(header, 1);
            await pipe.WriteAsync(header, ct);
            if (len > 0)
                await pipe.WriteAsync(payload!, ct);
            await pipe.FlushAsync(ct);
        }
    }
}
