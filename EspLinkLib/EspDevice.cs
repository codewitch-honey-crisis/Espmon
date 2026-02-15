using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace EL
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    class EspDeviceAttribute : Attribute
    {
        public EspDeviceAttribute(string name, uint magic, uint id = 0)
        {
            Name = name;
            Magic = magic;
			Id = id;

		}
		public string Name { get; set; }
        public uint Magic { get; set; }
		public uint Id { get; set; }

	}
    struct EspPartitionEntry {
        public uint Offset { get; } 
        public uint Size { get;  } 
		public string Name { get; }
        public EspPartitionEntry(uint offset,uint size, string name)
        {
            Offset = offset;
            Size = size;
            Name = name;
        }
    }
    /// <summary>
    /// Represents the base class for an Espressif MCU device
    /// </summary>
    public abstract class EspDevice
    {
        private WeakReference<EspLink> _parent;
        /// <summary>
        /// Constructs a new instance
        /// </summary>
        /// <param name="parent"></param>
        protected EspDevice(EspLink parent)
        {
            _parent = new WeakReference<EspLink>(parent);
        }
        /// <summary>
        /// Retrieves the parent of the device
        /// </summary>
        protected EspLink? Parent {
            get {
                EspLink? result;
                if (_parent.TryGetTarget(out result))
                {
                    return result;
                }
                throw new InvalidOperationException("The parent has been disposed");
            }
        }
        internal virtual int FlashSize { get =>0; } 
        internal abstract uint FLASH_ID
        {
            get;
        }
        internal virtual string? CHIP_NAME { get { return GetType().GetCustomAttribute<EspDeviceAttribute>()?.Name; } }
        internal virtual uint CHIP_DETECT_MAGIC_VALUE { get { return GetType().GetCustomAttribute<EspDeviceAttribute>()!.Magic; } }
        internal virtual bool IS_STUB { get; set; } = false;

        // Commands supported by ESP8266 ROM bootloader
        internal virtual byte ESP_FLASH_BEGIN { get; } = 0x02;
        internal virtual byte ESP_FLASH_DATA { get; } = 0x03;
        internal virtual byte ESP_FLASH_END { get; } = 0x04;
        internal virtual byte ESP_MEM_BEGIN { get; } = 0x05;
        internal virtual byte ESP_MEM_END { get; } = 0x06;
        internal virtual byte ESP_MEM_DATA { get; } = 0x07;
        internal virtual byte ESP_SYNC { get; } = 0x08;
        internal virtual byte ESP_WRITE_REG { get; } = 0x09;
        internal virtual byte ESP_READ_REG { get; } = 0x0A;

        // Some commands supported by ESP32 and later chips ROM bootloader (or -8266 w/ stub)
        internal virtual byte ESP_SPI_SET_PARAMS { get; } = 0x0B;
        internal virtual byte ESP_SPI_ATTACH { get; } = 0x0D;
        internal virtual byte ESP_READ_FLASH_SLOW { get; } = 0x0E;  // ROM only, much slower than the stub flash read
        internal virtual byte ESP_CHANGE_BAUDRATE { get; } = 0x0F;
        internal virtual byte ESP_FLASH_DEFL_BEGIN { get; } = 0x10;
        internal virtual byte ESP_FLASH_DEFL_DATA { get; } = 0x11;
        internal virtual byte ESP_FLASH_DEFL_END { get; } = 0x12;
        internal virtual byte ESP_SPI_FLASH_MD5 { get; } = 0x13;

        // Commands supported by ESP32-S2 and later chips ROM bootloader only
        internal virtual byte ESP_GET_SECURITY_INFO { get; } = 0x14;
		internal virtual uint EFUSE_RD_REG_BASE { get; } = 0;

		internal virtual uint EFUSE_BLK0_RDATA3_REG_OFFS { get => 0; }
		internal virtual uint EFUSE_BLK0_RDATA5_REG_OFFS { get => 0; }
		// Some commands supported by stub only
		internal virtual byte ESP_ERASE_FLASH { get; } = 0xD0;
        internal virtual byte ESP_ERASE_REGION { get; } = 0xD1;
        internal virtual byte ESP_READ_FLASH { get; } = 0xD2;
        internal virtual byte ESP_RUN_USER_CODE { get; } = 0xD3;

        // Flash encryption encrypted data command
        internal virtual byte ESP_FLASH_ENCRYPT_DATA { get; } = 0xD4;

        // Response code(s) sent by ROM
        internal virtual byte ROM_INVALID_RECV_MSG { get; } = 0x05;  // response if an invalid message is received

        // Maximum block sized for RAM and Flash writes, respectively.
        internal virtual uint ESP_RAM_BLOCK { get; set; } = 0x1800;

        internal virtual uint FLASH_WRITE_SIZE { get; } = 0x400;

        // Default baudrate. The ROM auto-bauds, so we can use more or less whatever we want.
        internal virtual uint ESP_ROM_BAUD { get; } = 115200;

        // First byte of the application image
        internal virtual uint ESP_IMAGE_MAGIC { get; } = 0xE9;

        // Initial state for the checksum routine
        internal virtual uint ESP_CHECKSUM_MAGIC { get; } = 0xEF;

        // Flash sector size, minimum unit of erase.
        internal virtual uint FLASH_SECTOR_SIZE { get; } = 0x1000;

        internal virtual uint UART_DATE_REG_ADDR { get; } = 0x60000078;

        // Whether the SPI peripheral sends from MSB of 32-bit register, or the MSB of valid LSB bits.
        internal virtual bool SPI_ADDR_REG_MSB { get; } = true;

        // This ROM address has a different value on each chip model
        internal virtual uint CHIP_DETECT_MAGIC_REG_ADDR { get; } = 0x40001000;

        internal virtual uint UART_CLKDIV_MASK { get; } = 0xFFFFF;

        //  Memory addresses
        internal virtual uint IROM_MAP_START { get; } = 0x40200000;
        internal virtual uint IROM_MAP_END { get; } = 0x40300000;

        // The number of bytes in the UART response that signify command status
        internal virtual ushort STATUS_BYTES_LENGTH { get; } = 2;

        // Bootloader flashing offset
        internal virtual uint BOOTLOADER_FLASH_OFFSET { get; } = 0x0;

        // ROM supports an encrypted flashing mode
        internal virtual bool SUPPORTS_ENCRYPTED_FLASH { get; } = false;

        // # Device PIDs
        internal virtual uint USB_JTAG_SERIAL_PID { get; } = 0x1001;

        //  Chip IDs that are no longer supported by esptool
        internal (int Id, string Name)[] UNSUPPORTED_CHIPS { get; } = { (6, "ESP32-S3(beta 3)") };

        // Whether the SPI peripheral sends from MSB of 32-bit register, or the MSB of valid LSB bits.
        
		internal virtual uint SPI_REG_BASE { get; } = 0;
		internal virtual byte SPI_USR_OFFS { get; } = 0;
		internal virtual byte SPI_USR1_OFFS { get; } = 0;
		internal virtual byte SPI_USR2_OFFS { get; } = 0;
		internal virtual short SPI_MOSI_DLEN_OFFS { get; } = -1;
		internal virtual short SPI_MISO_DLEN_OFFS { get; } = -1;
        internal virtual byte SPI_W0_OFFS { get; } = 0;
        /// <summary>
        /// Called after a connection is made
        /// </summary>
        /// <param name="timeout">The timeout</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        protected virtual Task OnConnectedAsync(int timeout, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
        /// <summary>
        /// Connect to the device
        /// </summary>
        /// <param name="timeout">The timeout</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>An awaitable task</returns>
        internal async Task ConnectAsync(int timeout = -1, CancellationToken cancellationToken = default)
        {
            await OnConnectedAsync(timeout, cancellationToken);
        }
	}
    
}