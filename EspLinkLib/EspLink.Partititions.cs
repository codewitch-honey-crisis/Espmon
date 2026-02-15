using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EL
{
	partial class EspLink
	{
		
		static readonly byte[] _md5PartMagic = new byte[] { 0xEB,0xEB,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF };
		private enum PartitionType : byte
		{
			App = 0x00,
			Data = 0x01
		}
		[Flags]
		private enum PartitionEntryFlags : uint
		{
			None = 0x00,
			Encrypted = 0x01,
			ReadOnly = 0x10
		}
		private enum PartitionSubType : byte
		{
			AppTypeFactory = 0x00,
			AppTypeTest = 0x20,
			Ota = 0x00,
			Phy = 0x01,
			Nvs = 0x02,
			CoreDump = 0x03,
			NvsKeys = 0x05,
			EFuse = 0x05,
			Undefined = 0x06,
			EspHttpd = 0x80,
			Fat = 0x81,
			Spiffs = 0x82,
			LittleFs = 0x83
		}
		private struct PartitionEntry
		{
			public readonly string? Name;
			public readonly uint Type;
			public readonly uint SubType;
			public readonly uint Offset;
			public readonly uint Length;
			public readonly PartitionEntryFlags Flags;
			public PartitionEntry(string? name,uint type, uint subType, uint offset, uint length, PartitionEntryFlags flags)
			{
				Name = name;
				Type = type;
				SubType = subType;
				Offset = offset;
				Length = length;
				Flags = flags;
			}
			public static readonly PartitionEntry Empty = new PartitionEntry(null, (uint)PartitionType.App, (uint)PartitionSubType.Ota, 0, 0,PartitionEntryFlags.None);
		}

		static readonly byte[] _partitionMagicBytes = new byte[] { 0xAA, 0x50 };
		static uint GetAlignmentOffsetForPartType(uint partType)
		{
			if (partType == (uint)PartitionType.App)
			{
				return 0x10000;
			}
			return 0x1000;
		}
		static uint GetAlignmentSizeForType(uint partType)
		{
			if (partType == (uint)PartitionType.App)
			{
				return 0x1000;
			}
			return 0x1;
		}
		static uint ParsePartSubtype(string subtype)
		{
			uint result;
			switch (subtype)
			{
				case "factory": result = (uint)PartitionSubType.AppTypeFactory; break;
				case "test": result = (uint)PartitionSubType.AppTypeTest; break;
				case "ota": result = (uint)PartitionSubType.Ota; break;
				case "nvs": result = (uint)PartitionSubType.Nvs; break;
				case "codedump": result = (uint)PartitionSubType.CoreDump; break;
				case "nvs_keys": result = (uint)PartitionSubType.NvsKeys; break;
				case "efuse": result = (uint)PartitionSubType.EFuse; break;
				case "undefined": result = (uint)PartitionSubType.Undefined; break;
				case "esphttpd": result = (uint)PartitionSubType.EspHttpd; break;
				case "fat": result = (uint)PartitionSubType.Fat; break;
				case "spiffs": result = (uint)PartitionSubType.Spiffs; break;
				case "littlefs": result = (uint)PartitionSubType.LittleFs; break;
				default:
					if(subtype.StartsWith("ota_"))
					{
						var val = uint.Parse(subtype.Substring(4),NumberStyles.Integer,CultureInfo.InvariantCulture.NumberFormat);
						return 0x10 + val;
					}
					if(subtype.Length==0)
					{
						return (uint)PartitionSubType.AppTypeFactory;
					}
					result = ParsePartNum(subtype);
					break;
			}
			return result;
		}
		static uint AlignValueUp(uint value,uint alignment)
		{
			if(alignment<2)
			{
				return value;
			}
			if (value % alignment != 0)
				value += (uint)(alignment - value % alignment);
			return value;
		}
		static async Task<PartitionEntry[]> ParsePartEntriesAsync(TextReader reader)
		{
			string? line;
			var result = new List<PartitionEntry>();
			uint lastOffset = 0,lastLength=0;
			while ((line = await reader.ReadLineAsync()) != null)
			{
				line = line.Trim();
				if (line.Length==0 || line.StartsWith("#")) { continue; }
				var fields = line.Split(',');
				string name="";
				uint type=(uint)PartitionType.App;
				uint subtype=(uint)PartitionSubType.AppTypeFactory;
				uint offset=AlignValueUp(lastOffset+lastLength,GetAlignmentOffsetForPartType(type));
				uint length=0;
				PartitionEntryFlags flags=PartitionEntryFlags.None;
				if (fields.Length > 0)
				{
					name = fields[0].Trim();
					if (fields.Length > 1)
					{
						type = ParsePartType(fields[1].Trim());
						if (fields.Length > 2)
						{
							subtype = ParsePartSubtype(fields[2].Trim());
							if(fields.Length > 3)
							{
								offset = AlignValueUp(ParsePartSize(fields[3].Trim()),GetAlignmentOffsetForPartType(type));
								lastOffset = offset;
								if (fields.Length > 4)
								{
									length = AlignValueUp(ParsePartSize(fields[4].Trim()),GetAlignmentSizeForType(type));
									lastLength = length;
									if (fields.Length > 5)
									{
										flags = ParsePartFlags(fields[5].Trim());
									}
								}
							}
						}
					}
				}
				result.Add(new PartitionEntry(name,type,subtype, offset, length, flags));
			}
			return result.ToArray();
		}
		internal async Task PartitionToBinaryAsync(TextReader input, Stream output)
		{
			var entries = await ParsePartEntriesAsync(input);
			for (var i = 0; i < entries.Length; i++)
			{
				PartitionEntry entry = entries[i];
				await WritePartEntryAsync(entry, output);
			}
			const int MAX_PART_LEN = 0xC00;
			var hash = await MD5HashAsync(output);
			await output.WriteAsync(_md5PartMagic, 0, _md5PartMagic.Length);
			await output.WriteAsync(hash, 0, hash.Length);
			for (int i = (int)output.Length; i < MAX_PART_LEN;++i)
			{
				output.WriteByte(0xFF);
			}
			await output.FlushAsync();
			output.Position = 0;
		}
		static uint ParsePartType(string type)
		{
			uint result;
			switch (type)
			{
				case "app": result = (uint)PartitionType.App; break;
				case "data": result = (uint)PartitionType.Data; break;
				default:
					if(type.Length==0)
					{
						return (uint)PartitionType.App;
					}
					result = ParsePartNum(type);
					break;
			}
			return result;
		}
		static PartitionEntryFlags ParsePartFlags(string flags)
		{
			PartitionEntryFlags result=PartitionEntryFlags.None;
			if (!string.IsNullOrEmpty(flags))
			{
				var fa = flags.Split(':');
				for (var i = 0; i < fa.Length; ++i)
				{
					var f = fa[i].Trim();
					if (f.Length == 0) { continue; }
					switch(f)
					{
						case "encrypted": result|=PartitionEntryFlags.Encrypted; break;
						case "readonly": result=PartitionEntryFlags.ReadOnly; break;
						default:
							result |= (PartitionEntryFlags)ParsePartNum(f);
							break;
					}
				}
			}
			return result;
		}
		
		static uint ParsePartSize(string value)
		{
			if(string.IsNullOrEmpty(value))
			{
				return 0;
			}
			uint mult = 1;
			if (value.EndsWith("K", StringComparison.OrdinalIgnoreCase))
			{
				mult = 1024 ;
				value = value.Substring(0, value.Length - 1);
			} else if (value.EndsWith("M",StringComparison.OrdinalIgnoreCase))
			{
				mult = 1024 * 1024;
				value = value.Substring(0, value.Length - 1);
			}
			uint rawResult = 0;
			if (value.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
			{
				value = value.Substring(2, value.Length - 2);
				if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture.NumberFormat, out rawResult))
				{
					throw new ArgumentException("The number was not in the correct format",nameof(value));
				}
			} else
			{
				if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture.NumberFormat, out rawResult))
				{
					throw new ArgumentException("The number was not in the correct format", nameof(value));
				}
			}
			return rawResult*mult;
		}
		static uint ParsePartNum(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return 0;
			}
			
			uint result = 0;
			if (value.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
			{
				value = value.Substring(2, value.Length - 2);
				if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture.NumberFormat, out result))
				{
					throw new ArgumentException("The number was not in the correct format", nameof(value));
				}
			}
			else
			{
				if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture.NumberFormat, out result))
				{
					throw new ArgumentException("The number was not in the correct format", nameof(value));
				}
			}
			return result;
		}
		static async Task WritePartEntryAsync(PartitionEntry entry, Stream stream)
		{
			//b'<2sBBLL16sL'
			await stream.WriteAsync(_partitionMagicBytes,0, _partitionMagicBytes.Length);
			var ba = new byte[] { (byte)entry.Type, (byte)entry.SubType };
			await stream.WriteAsync(ba, 0, ba.Length);
			uint offset = entry.Offset;
			uint size = entry.Length;
			var flags = (uint)entry.Flags;
			if (!BitConverter.IsLittleEndian)
			{
				offset = SwapBytes(offset);
				size = SwapBytes(size);
				flags = SwapBytes(flags);
			}
			ba = new byte[8];
			PackUInts(ba, 0, new uint[] { offset,size});
			await stream.WriteAsync(ba, 0, ba.Length);
			ba = new byte[16];
			Encoding.ASCII.GetBytes(entry.Name??"", 0, Math.Min(16, entry.Name!=null?entry.Name.Length:0), ba, 0);
			await stream.WriteAsync(ba, 0, ba.Length);
			ba = BitConverter.GetBytes(flags);
			await stream.WriteAsync(ba, 0, ba.Length);
		}
		/// <summary>
		/// Flashes a partition table to the device
		/// </summary>
		/// <param name="partitionTable">A CSV file following this form: https://docs.espressif.com/projects/esp-idf/en/latest/esp32/api-guides/partition-tables.html</param>
		/// <param name="compress">True to compress the image to save traffic, otherwise false</param>
		/// <param name="blockSize">The size of the flash blocks to use</param>
		/// <param name="offset">The offset to begin writing at</param>
		/// <param name="writeAttempts">The number of write attempts to make per block</param>
		/// <param name="finalize">Finalize the flash write and exit the bootloader (not necessary)</param>
		/// <param name="timeout">The timeout for the suboperations</param>
		/// <param name="progress">A <see cref="IProgress{Int32}"/> that can be used to report the progress</param>
		public void FlashPartition(TextReader partitionTable, bool compress, uint blockSize = 0, uint offset = 0x08000, int writeAttempts = 3, bool finalize = false, int timeout = -1, IProgress<int>? progress = null)
		{
			FlashPartitionAsync(partitionTable, compress, blockSize, offset, writeAttempts, finalize, timeout, progress, CancellationToken.None).Wait();
		}
		/// <summary>
		/// Asynchronously flashes a partition table to the device
		/// </summary>
		/// <param name="partitionTable">A CSV file following this form: https://docs.espressif.com/projects/esp-idf/en/latest/esp32/api-guides/partition-tables.html</param>
		/// <param name="compress">True to compress the image to save traffic, otherwise false</param>
		/// <param name="blockSize">The size of the flash blocks to use</param>
		/// <param name="offset">The offset to begin writing at</param>
		/// <param name="writeAttempts">The number of write attempts to make per block</param>
		/// <param name="finalize">Finalize the flash write and exit the bootloader (not necessary)</param>
		/// <param name="timeout">The timeout for the suboperations</param>
		/// <param name="progress">A <see cref="IProgress{Int32}"/> that can be used to report the progress</param>
		/// <param name="cancellationToken">A token that can be used to cancel this operation</param>
		public async Task FlashPartitionAsync(TextReader partitionTable, bool compress, uint blockSize = 0, uint offset = 0x08000, int writeAttempts = 3, bool finalize = false, int timeout = -1, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
			using (var stm = new MemoryStream())
			{
				await PartitionToBinaryAsync(partitionTable, stm);
				stm.Seek(0,SeekOrigin.Begin);
				await FlashAsync(stm, compress, blockSize, offset, writeAttempts, finalize, timeout, progress, cancellationToken);
			}
		}
		private static async Task<byte[]> MD5HashAsync(Stream stream)
		{
			var ba = new byte[stream.Length];
			stream.Position = 0;
			await stream.ReadAsync(ba, 0, ba.Length);
			using (MD5 md5 = MD5.Create())
			{	
				return  md5.ComputeHash(ba);
			}
		}
	}
}