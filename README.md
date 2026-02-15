# Espmon

A fully configurable ESP32 based PC hardware monitoring dashboard

This project is ESP32 firmware and a PC companion application that allows you to monitor your PCs vital statistics and present them on one of many supported ESP32 development kits connected to it via USB Serial.

It supports a pluggable provider architecture for adding new hardware information capabilities such as currently unsupported Intel dGPU statistics in the future (Someone want to send me a Battlemage to test with? ) 

It allows you to use a programmable query system to collect and transform data, even allowing you to create histories of arbitrary values over a particular time period, so you can for example keep tabs on the hottest your CPU has been over the past hour.

You can have 4 arbitrary values per screen, and the screens are divided into two categories of value. Each device can flip through available screens using a touch screen or attached button (assuming the kit has one or the other)

You can run as many attached ESP32s with independent screens as you like, and each one can have its own set of screens you can flip through by manipulating it as above.

The application allows you to flash the devices straight from the application.

The application also allows your dashboard to be persisted as a service so it's available as soon as windows boots and not dependent on a user running the application explicitly to start the monitoring.

To build it - at least using the instructions I give you,  you'll need a windows machine, and you must go here https://docs.espressif.com/projects/esp-idf/en/stable/esp32/get-started/windows-setup.html and install the ESP-IDF using the Windows Installer. Install at least 5.4.3, exporting all the environment variables. You may need to log off and log on to Windows again to refresh everything.

You'll need Visual Studio (2022 or 2026 should work) installed as well.




# ESP Monitor Multi-Board Build System

## Project Structure

```
espmon/
├── shared/
│   ├── components/          # Shared components (htcw_esp_panel, gfx, uix, etc.)
│   └── main/                # Shared application code
│       ├── CMakeLists.txt
│       ├── app_main.cpp
│       └── ... (your other source files)
├── boards/
│   ├── matouch-parallel-35/     # Matouch ESP32-S3 Parallel 3.5"
│   │   ├── CMakeLists.txt       # Board-specific project file
│   │   └── sdkconfig.defaults   # Board-specific config
│   ├── m5stack-core2/           # Another board
│   │   ├── CMakeLists.txt
│   │   └── sdkconfig.defaults
│   └── ... (one directory per board)
├── firmware/                    # Generated during build
│   ├── matouch-parallel-35/
│   │   ├── bootloader.bin
│   │   ├── partition-table.bin
│   │   └── firmware.bin
│   ├── m5stack-core2/
│   │   └── ...
│   └── boards.json              # Copied here as manifest
├── boards.json                  # Board definitions (source of truth)
├── build_all.cmake              # Build orchestration script
└── build_all.bat                # Windows convenience script
```

## Building

### Build All Boards
```bash
# Windows
build_all.bat

# Or manually
cmake -P build_all.cmake
```

### Build Single Board (for development)
```bash
cd boards/matouch-parallel-35
idf.py set-target esp32s3
idf.py build
idf.py flash
```

### Using VS Code ESP-IDF Extension
1. Open the specific board folder (e.g., `boards/matouch-parallel-35`)
2. Use the extension normally - it will work as a standalone ESP-IDF project

## Adding a New Board

1. **Add entry to boards.json**:
```json
{
  "name": "My New Board",
  "slug": "my-new-board",
  "define": "MY_BOARD_DEFINE",
  "target": "esp32s3",
  "components": [
    "esp_lcd_panel_xxx",
    "esp_lcd_touch_yyy"
  ],
  "flash_mode": "dio",
  "flash_freq": "80m",
  "flash_size": "4MB"
}
```

2. **Create board directory**:
```bash
mkdir boards/my-new-board
```

3. **Copy CMakeLists.txt from another board** and update the define:
```cmake
add_compile_definitions(MY_BOARD_DEFINE)
```

4. **Copy sdkconfig.defaults** and update target if needed

5. **Add required components** to your main project's component dependencies

## boards.json Reference

- **name**: Friendly name shown in C# app dropdown
- **slug**: Filesystem-safe folder name
- **define**: C preprocessor define for htcw_esp_panel profile
- **target**: ESP32 chip variant (esp32, esp32s3, esp32c3, esp32p4, etc.)
- **components**: List of ESP-IDF components needed for this board
- **flash_mode**: DIO/QIO/DOUT/QOUT
- **flash_freq**: Flash frequency (40m/80m)
- **flash_size**: Flash size (2MB/4MB/8MB/16MB)

## Flash Offsets

Standard offsets used across all boards:
- Bootloader: 0x1000 (ESP32/S2/S3) or 0x0 (C3/C6)
- Partition Table: 0x8000
- Application: 0x10000

These are included in the manifest for the C# flasher app.

## Custom Panel Configurations

If a board needs a custom panel config instead of a htcw_esp_panel profile:

1. Create `boards/my-board/custom_panel.h`
2. In the board's CMakeLists.txt:
```cmake
target_include_directories(espmon PRIVATE "${CMAKE_CURRENT_LIST_DIR}")
```
3. The custom_panel.h will be found before the shared components

## Component Dependencies

Components listed in boards.json must be:
1. Available in your ESP-IDF component registry
2. Or added to `shared/components/` 
3. Or referenced via idf_component.yml

The build will fail if a required component is missing.
