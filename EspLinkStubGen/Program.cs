using System.Text.Json;
namespace EspLinkStubGen
{
	internal class Program
	{
		static uint SwapBytes(uint x)
		{
			// swap adjacent 16-bit blocks
			x = (x >> 16) | (x << 16);
			// swap adjacent 8-bit blocks
			return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);

		}
		static void Main(string[] args)
		{
			Console.WriteLine(Environment.CommandLine);
			var outpath = args[1];
			foreach (var file in Directory.GetFiles(args[0], "*.json"))
			{
				using (var stm = File.OpenRead(file))
				{
					JsonDocument doc = JsonDocument.Parse(stm);
					using (var output = File.OpenWrite(Path.Combine(outpath, Path.GetFileNameWithoutExtension(file) + ".idx")))
					{
						uint entryPoint = doc.RootElement.GetProperty("entry").GetUInt32();
						uint textStart = doc.RootElement.GetProperty("text_start").GetUInt32();
						uint dataStart = doc.RootElement.GetProperty("data_start").GetUInt32();
						if (!BitConverter.IsLittleEndian)
						{
							entryPoint = SwapBytes(entryPoint);
							textStart = SwapBytes(textStart);
							dataStart = SwapBytes(dataStart);
						}
						var ba = BitConverter.GetBytes(entryPoint);
						output.Write(ba, 0, ba.Length);
						ba = BitConverter.GetBytes(textStart);
						output.Write(ba, 0, ba.Length);
						ba = BitConverter.GetBytes(dataStart);
						output.Write(ba, 0, ba.Length);
					}
					var text = doc.RootElement.GetProperty("text").GetBytesFromBase64();
					using (var output = File.OpenWrite(Path.Combine(outpath, Path.GetFileNameWithoutExtension(file) + ".text")))
					{
						output.Write(text, 0, text.Length);
					}
					var data = doc.RootElement.GetProperty("data").GetBytesFromBase64();
					using (var output = File.OpenWrite(Path.Combine(outpath, Path.GetFileNameWithoutExtension(file) + ".data")))
					{
						output.Write(data, 0, data.Length);
					}

				}
			}
		}

	}
}
