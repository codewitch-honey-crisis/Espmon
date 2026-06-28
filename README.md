# Espmon 4

This is the alpha release.

A fully configurable ESP32 based PC hardware monitoring dashboard

This project is ESP32 firmware and a PC companion application that allows you to monitor your PCs vital statistics and present them on one of many supported ESP32 development kits connected to it via USB Serial.

It allows you to use a programmable query system to collect and transform data, even allowing you to create histories of arbitrary values over a particular time period, so you can for example keep tabs on the hottest your CPU has been over the past hour.

You can have 4 arbitrary values per screen, and the screens are divided into two categories of value. Each device can flip through available screens using a touch screen or attached button (assuming the kit has one or the other)

You can run as many attached ESP32s with independent screens as you like, and each one can have its own set of screens you can flip through by manipulating it as above.

The application allows you to flash the devices straight from the application.

The application also allows your dashboard to be persisted as a service so it's available as soon as windows boots and not dependent on a user running the application explicitly to start the monitoring.

To run, use Espmon-Install.exe to extract to a folder. You can execute Espmon.exe from the extract location

To build:
- you need Visual Studio 2026
- you need a recent copy of python installed and in your path.
- You need the ESP-IDF 5.x Installed
- You need 7-zip installed


![Espmon 4 Firmware](espmon4.jpg)
