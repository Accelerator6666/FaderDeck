# FaderDeck M0 serial protocol (draft 0)

This document defines a **new host↔ESP32-S3 diagnostics protocol**, not the upstream FaderBuddy I2C register format. ASCII UTF-8 with newline (`\n`) delimiters on the ESP32-S3 **hardware USB Serial/JTAG CDC** port.

All numbers are unsigned base-10 except where indicated.

| Host request | Device reply | Notes |
| --- | --- | --- |
| `PING` | `PONG 1` | Reports diagnostics-protocol version |
| `STATE` | `STATE mode layer position touch` | Parsed from FaderBuddy register 0x01 |
| `LAYER n` | `OK LAYER` | Active layer 0..7; user-touch safety applied by firmware |
| `MOVE layer pos speed` | `OK MOVE` | Explicitly sets FaderBuddy layer target, pos/speed 0..255 |
| `CALIBRATE` | `OK CALIBRATE` | Explicitly requests FaderBuddy motor self-calibration |
| invalid/unavailable | `ERR reason` | Never silently fake success |

`STATE mode layer position touch` fields:
- mode: 0 moving remotely; 1 active user input; 2 idle; 3 error; 4 self calibration (upstream firmware enums)
- layer: 0..7
- position: 0..255
- touch: 0 or 1

`MOVE` is **explicit physical motion** when the target layer is active, not an app parameter write. It refuses user touch/mode-input-active and refuses a failed I2C state read. Writing an inactive layer's target updates its remembered position without moving that layer immediately. The upstream firmware may itself defer a layer change while the user interacts.

For safety, the M0 host CLI never automatically sends `MOVE` based on a Cached or Unknown app value. Full feedback synchronization is future work.

Read and write commands are synchronous in M0. `OK` means an I2C command was accepted, **not** that the physical motor has reached the requested position; poll `STATE` separately.

USB / transport details: native ESP32-S3 USB Serial/JTAG CDC, not a generic UART dongle and not TinyUSB MIDI; baud rate in CLI is nominal only and does not set an actual USB physical bit rate. Keep one serial client at a time.
