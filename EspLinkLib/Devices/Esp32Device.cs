using System;
using System.Collections.Generic;

namespace EL
{
	[EspDevice("ESP32", 0x00F01D83)]
    internal class Esp32Device : EspDevice
    {
        
		internal override int FlashSize
        {
            get
            {
                var fid = FLASH_ID;
                byte sizeId = (byte)(fid >> 16);
                int result;
                if (FLASH_SIZES.TryGetValue(sizeId, out result))
                {
                    return result;
                }
                return -1;
            }
        }
        internal override uint FLASH_ID
        {
            get
            {
                const byte SPIFLASH_RDID = 0x9F;
                if (Parent == null) throw new InvalidOperationException("Could not connect to EspLink");
                return Parent.SpiFlashCommand(SPIFLASH_RDID, Array.Empty<byte>(), 24, 0, 0, 0, Parent.DefaultTimeout);
            }
        }
        public Esp32Device(EspLink parent) : base(parent) { }
        internal override uint IROM_MAP_START { get; } = 0x400D0000;
        internal override uint IROM_MAP_END { get; } = 0x40400000;

        internal virtual uint DROM_MAP_START { get; } = 0x3F400000;
        internal virtual uint DROM_MAP_END { get; } = 0x3F800000;

        // ESP32 uses a 4 byte status reply
        internal override ushort STATUS_BYTES_LENGTH { get { if (Parent == null) throw new InvalidOperationException("Could not connect to EspLink"); return Parent.IsStub ? (ushort)2 : (ushort)4; } } 

        internal override uint SPI_REG_BASE { get; } = 0x3FF42000;
        internal override byte SPI_USR_OFFS { get; } = 0x1C;
        internal override byte SPI_USR1_OFFS { get; } = 0x20;
        internal override byte SPI_USR2_OFFS { get; } = 0x24;
        internal override short SPI_MOSI_DLEN_OFFS { get; } = 0x28;
        internal override short SPI_MISO_DLEN_OFFS { get; } = 0x2C;

		internal override byte SPI_W0_OFFS { get; } = 0x80;

		internal override uint EFUSE_RD_REG_BASE { get; } = 0x3FF5A000;

        internal override uint EFUSE_BLK0_RDATA3_REG_OFFS { get => EFUSE_RD_REG_BASE + 0x00C; }
        internal override uint EFUSE_BLK0_RDATA5_REG_OFFS { get => EFUSE_RD_REG_BASE + 0x014; }
        internal virtual uint EFUSE_DIS_DOWNLOAD_MANUAL_ENCRYPT_REG { get => EFUSE_RD_REG_BASE + 0x18; }

        internal virtual byte EFUSE_DIS_DOWNLOAD_MANUAL_ENCRYPT { get; } = 1 << 7; // EFUSE_RD_DISABLE_DL_ENCRYPT

        internal virtual uint EFUSE_SPI_BOOT_CRYPT_CNT_REG { get => EFUSE_RD_REG_BASE; } // EFUSE_BLK0_WDATA0_REG

        internal virtual uint EFUSE_SPI_BOOT_CRYPT_CNT_MASK { get; } = 0x7F << 20;  // EFUSE_FLASH_CRYPT_CNT
        internal virtual uint EFUSE_RD_ABS_DONE_REG { get => EFUSE_RD_REG_BASE + 0x018; }

        internal virtual byte EFUSE_RD_ABS_DONE_0_MASK { get; } = 1 << 4;
        internal virtual byte EFUSE_RD_ABS_DONE_1_MASK { get; } = 1 << 5;

        internal virtual uint EFUSE_VDD_SPI_REG { get => EFUSE_RD_REG_BASE + 0x10; }

        internal virtual uint VDD_SPI_XPD { get; } = (uint)(1 << 14);  // XPD_SDIO_REG


        internal virtual uint VDD_SPI_TIEH { get; } = (uint)(1 << 15);  // XPD_SDIO_TIEH

		internal virtual uint VDD_SPI_FORCE { get; } = (uint)(1 << 16); // XPD_SDIO_FORCE


        internal virtual uint DR_REG_SYSCON_BASE { get; } = 0x3FF66000;

		internal virtual uint APB_CTL_DATE_ADDR { get => DR_REG_SYSCON_BASE + 0x7C; }

        internal virtual byte APB_CTL_DATE_V { get; } = 0x1;
        internal virtual byte APB_CTL_DATE_S { get; } = 31;
		internal virtual uint UART_CLKDIV_REG { get; } = 0x3FF40014;

        internal virtual uint XTAL_CLK_DIVIDER { get; } = 1;

        internal virtual uint RTCCALICFG1 { get; } = 0x3FF5F06C;
        internal virtual uint TIMERS_RTC_CALI_VALUE { get; } = 0x01FFFFFF;
        internal virtual uint TIMERS_RTC_CALI_VALUE_S { get; } = 7;

        internal virtual uint GPIO_STRAP_REG { get; } = 0x3FF44038;
        internal virtual uint GPIO_STRAP_VDDSPI_MASK { get; } = 1 << 5;  // GPIO_STRAP_VDDSDIO

        internal virtual uint RTC_CNTL_SDIO_CONF_REG { get; } = 0x3FF48074;
        internal virtual uint RTC_CNTL_XPD_SDIO_REG { get; } = (uint)(1L << 31);
        internal virtual uint RTC_CNTL_DREFH_SDIO_M { get; } = (uint)(3L << 29);
        internal virtual uint RTC_CNTL_DREFM_SDIO_M { get; } = (uint)(3L << 27);
        internal virtual uint RTC_CNTL_DREFL_SDIO_M { get; } = (uint)(3L << 25);
        internal virtual uint RTC_CNTL_SDIO_FORCE { get; } = (uint)(1L << 22);
        internal virtual uint RTC_CNTL_SDIO_PD_EN { get; } = (uint)(1L << 21);


        internal virtual IReadOnlyDictionary<byte, int> FLASH_SIZES { get; } = new Dictionary<byte, int>() {
            { 0x00, 1*1024 },
            { 0x10, 2*1024 },
            { 0x20, 4*1024 },
            { 0x30, 8*1024 },
            { 0x40, 16*1024 },
            { 0x50, 32*1024 },
            { 0x60, 64*1024 },
            { 0x70, 128*1024 }
		};
        internal virtual IReadOnlyDictionary<byte, int> FLASH_FREQUENCY { get; } = new Dictionary<byte, int>() {
            { 0x0F, 80 },
			{ 0x00, 40 },
            { 0x01, 26 },
            { 0x02, 20 }
        };
        internal override uint BOOTLOADER_FLASH_OFFSET { get; } = 0x1000;

        internal virtual IReadOnlyList<string> OVERRIDE_VDDSDIO_CHOICES { get; } = new string[] { "1.8V", "1.9V", "OFF" };
        internal virtual IReadOnlyList<EspPartitionEntry> MEMORY_MAP { get; } = new EspPartitionEntry[] {
            new EspPartitionEntry( 0x00000000, 0x00010000, "PADDING"),
            new EspPartitionEntry( 0x3F400000, 0x3F800000, "DROM"),
            new EspPartitionEntry( 0x3F800000, 0x3FC00000, "EXTRAM_DATA"),
            new EspPartitionEntry( 0x3FF80000, 0x3FF82000, "RTC_DRAM"),
            new EspPartitionEntry( 0x3FF90000, 0x40000000, "BYTE_ACCESSIBLE"),
            new EspPartitionEntry( 0x3FFAE000, 0x40000000, "DRAM"),
            new EspPartitionEntry( 0x3FFE0000, 0x3FFFFFFC, "DIRAM_DRAM"),
            new EspPartitionEntry( 0x40000000, 0x40070000, "IROM"),
            new EspPartitionEntry( 0x40070000, 0x40078000, "CACHE_PRO"),
            new EspPartitionEntry( 0x40078000, 0x40080000, "CACHE_APP"),
            new EspPartitionEntry( 0x40080000, 0x400A0000, "IRAM"),
            new EspPartitionEntry( 0x400A0000, 0x400BFFFC, "DIRAM_IRAM"),
            new EspPartitionEntry( 0x400C0000, 0x400C2000, "RTC_IRAM"),
            new EspPartitionEntry( 0x400D0000, 0x40400000, "IROM"),
            new EspPartitionEntry( 0x50000000, 0x50002000, "RTC_DATA")
        };

        internal virtual byte FLASH_ENCRYPTED_WRITE_ALIGN { get; } = 32;

        internal virtual uint UF2_FAMILY_ID { get; } = 0x1C5F21B0;

    

	}
}
