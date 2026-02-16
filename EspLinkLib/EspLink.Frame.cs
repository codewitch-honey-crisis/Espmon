using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EL
{
	partial class EspLink
	{
		const byte _FrameDelimiter = 0xC0;
		const byte _FrameEscape = 0xDB;
		async Task WriteFrameAsync(byte[] data, int index, int length, int timeout = -1, CancellationToken cancellationToken = default)
        {
			int count = 0;
			for(var i = index;i<index+length;++i)
			{
				var b = data[i];
				switch(b)
				{
					case _FrameEscape:
					case _FrameDelimiter:
						count += 2;
						break;
					default:
						++count;
						break;
				}
			}
			var toWrite = new byte[count+2];
			toWrite[0] = _FrameDelimiter;
			var j = 1;
			for(var i = index;i<index+length;++i)
			{
				var src = data[i + index];
				switch(src)
				{
					case _FrameEscape:
						toWrite[j++] = _FrameEscape;
						toWrite[j++] = 0xDD;
						break;
					case _FrameDelimiter:
						toWrite[j++] = _FrameEscape;
						toWrite[j++] = 0xDC;
						break;
					default:
						toWrite[j++] = src;
						break;
				}
			}
			toWrite[j] = _FrameDelimiter;
			var port = GetOrOpenPort();
			if (port == null) throw new IOException("Serial port could not be opened");
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				port.WriteTimeout = timeout;
				await port.BaseStream.WriteAsync(toWrite, 0, toWrite.Length);
				//port.Write(toWrite,0,toWrite.Length);
				cancellationToken.ThrowIfCancellationRequested();
				await port.BaseStream.FlushAsync();
				
			}
			finally
			{
				port.WriteTimeout = -1;
				
			}
		}
		
		async Task<byte[]> ReadFrameAsync(int timeout = -1, CancellationToken cancellationToken = default)
        {
			long _start = DateTimeOffset.UtcNow.Ticks;
			var port = GetOrOpenPort(true)!;
			var bytes = new List<byte>();
			var foundStart = false;
			// start grabbing frame data
			var log = new StringBuilder();
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var i = port.BytesToRead>0? port.ReadByte():-1;
				if (0 > i)
				{
					await Task.Delay(5);
					if (timeout > -1 && TimeSpan.FromTicks( DateTimeOffset.UtcNow.Ticks-_start).Milliseconds >= timeout)
					{
						throw new TimeoutException("The read operation timed out");
					}
					continue;
				} 
				if(!foundStart)
				{
					if(i == _FrameDelimiter)
					{
						foundStart = true;
						continue;
						
					} else
					{
						log.Append((char)i);
						if (i == '\n')
						{
							Debug.Write(log.ToString());
							log.Clear();
						}
					}
				} else
				{
					if (i==_FrameDelimiter)
					{
						break;
					}
					bytes.Add((byte)i);
				}
			}
			if (log.Length > 0)
			{
				Debug.WriteLine(log.ToString());
			}
			int count = bytes.Count;
			for (var i = 0; i < bytes.Count; i++)
			{
				var b = bytes[i];
				if (b == _FrameEscape) { if(i<bytes.Count-1) --count; }
			}
			var result = new byte[count];
			count = 0;
			for (var i = 0; i < result.Length; ++i)
			{
				var b = bytes[i];
				switch (b)
				{
					case _FrameEscape:
						if (count >= result.Length - 1)
						{
							result[count++] = _FrameEscape;
							break;
						}
						if (i < bytes.Count - 1)
						{
							b = bytes[++i];
							switch (b)
							{
								case 0xDD:
									result[count++] = _FrameEscape;
									break;
								case 0xDC:
									result[count++] = _FrameDelimiter;
									break;
								default:
									throw new IOException("Invalid escape content in frame");
							}
						} else
						{
							result[count++] = b;
						}
						break;
					default: result[count++] = b; break;
				}
			}
			return result;
		}
	}
}
