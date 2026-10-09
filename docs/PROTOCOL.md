# FaderDeck M0 serial protocol (draft 1)

This document describes **FaderDeck host↔ESP32-S3** diagnostics, *not* the upstream FaderBuddy I2C protocol. ASCII UTF-8 lines terminated by `\n` on native ESP32-S3 USB Serial/JTAG CDC; all decimal numbers are unsigned.

| Host request | Device reply | Notes |
| --- | --- | --- |
| `PING` | `PONG 1` | Does not require an attached fader |
| `INFO` | `INFO protocol fw_major fw_minor serial_hex` | Reads FaderBuddy registers 0x00, 0x11, 0x08; serial is 20 hex characters |
| `STATE` | `STATE mode layer position touch` | Reads FaderBuddy register 0x01; requires v5 |
| `LAYER n` | `OK LAYER` | Layer 0..7; only allowed while idle and untouched |
| `MOVE layer pos speed` | `OK MOVE` | Position and speed 0..255; only allowed while idle and untouched |
| `CALIBRATE` | `OK CALIBRATE` | Entire mechanical travel; only allowed while idle and untouched |
| invalid/unavailable | `ERR reason` | Nonzero CLI exit code |

**Diagnostic-only host commands**: `diagnose` sends PING, INFO, STATE; `watch [count]` issues up to 200 STATE queries approximately 100 ms apart. They are implemented by the Windows CLI, not by the device firmware.

### State fields

- mode: 0 remotely moving; 1 user input; 2 idle; 3 error; 4 calibration
- layer: 0..7
- position: 0..255
- touch: 0 or 1

Firmware refuses v5-only commands if I2C protocol version is not 5. To minimize unexpected movement, manual commands require **mode 2 / idle and touch=0**. `INFO` is diagnostic and may report a protocol other than v5. If the fader is disconnected, INFO/STATE report I2C errors.

The M0 `MOVE` command explicitly updates a FaderBuddy firmware layer target. On an active layer this can physically actuate its motor; on an inactive layer it updates that layer's restore position, and moving to that layer later may actuate the motor. `OK MOVE` means the I2C transaction succeeded, **not** that the motor reached the position. Recheck `STATE` after a motor operation.

**Important:** The standalone M0 CLI does not read parameters from any creative application. Its manual `MOVE` is for deliberate bench testing only. Automatic movement from Cached/Unknown app values is forbidden by host-core `SyncGate` but is not an end-to-end production synchronization system.

The M0 USB port is the ESP32-S3 **hardware USB Serial/JTAG** port; it is neither TinyUSB MIDI nor a generic UART bridge. Only one host program may own the port at once. All commands must terminate with newline. Overlength lines are discarded through their final newline to avoid accidental parsing of a suffix.
