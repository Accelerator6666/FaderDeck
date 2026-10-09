# ESP32-S3 M0 firmware

ESP-IDF 5.5 project. Uses GPIO8=SDA, GPIO9=SCL, 100 kHz I2C and default FaderBuddy 0x20. Read [protocol](../../docs/PROTOCOL.md) before running motor commands.

```bash
idf.py set-target esp32s3
idf.py build
idf.py -p COM5 flash
```

Do not assume the first attached USB-C port is the USB Serial/JTAG port; inspect your ESP32-S3 development board documentation. The device need not have a FaderBuddy attached to respond to PING, but STATE/MOVE/LAYER require I2C hardware.

This is a developer/diagnostics build, not production motor-control safety certification. Do not test around fingers or obstruction; check the common ground and separate 5 V motor power first.
