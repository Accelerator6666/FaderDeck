# FaderDeck

**FaderDeck** is an open-source modular motorized control surface for creative applications.

[简体中文](README.zh-CN.md) · [Architecture](docs/ARCHITECTURE.md) · [Protocol](docs/PROTOCOL.md) · [Roadmap](docs/ROADMAP.md) · [Hardware bench checklist (中文)](docs/HARDWARE_TEST.zh-CN.md) · [Simulator (中文)](docs/SIMULATOR.zh-CN.md)

## Status

**M0 bootstrap / hardware validation prototype — NOT a finished controller.**

The initial proof of concept pairs **one ESP32-S3** with **one FaderBuddy** motorized fader and a **Windows .NET 10 serial diagnostic CLI**. The **Studio preview** adds four on-screen faders, eight parameter banks and an initial OBS WebSocket v5 volume adapter. Live OBS validation is still pending; DaVinci Resolve, REAPER, Ableton and USB MIDI integrations are planned.

| Part | Status |
| --- | --- |
| ESP32-S3 single-fader USB Serial/JTAG bridge | Initial source, awaiting real hardware validation |
| I2C FaderBuddy protocol-v5 integration | Initial source, awaiting real hardware validation |
| Windows command-line diagnostics | Initial source |
| VirtualFader software-only emulator | Implemented with unit tests; no hardware required |
| Parameter synchronization safety gate | Implemented in core library with source-level tests |
| Avalonia Studio: four onscreen virtual faders | Preview implementation |
| OBS WebSocket v5 volume read/write + event subscription | Initial adapter, live testing pending |
| Physical four-fader hardware, USB MIDI, REAPER/DaVinci integration | Planned |

## Hardware-free simulator

No electronics yet? Run the built-in VirtualFader simulator to test the diagnostic protocol and safety logic. This **does not simulate electrical or mechanical behavior** and uses no serial port:

```powershell
dotnet run --project software/FaderDeck.Cli -- sim --demo
dotnet run --project software/FaderDeck.Cli -- sim
```

Or use `FaderDeck.Cli.exe sim --demo` from the Windows Actions artifact. See [Chinese simulator instructions](docs/SIMULATOR.zh-CN.md).

## FaderDeck Studio desktop preview — NO electronics required

Download **FaderDeck-Studio-Windows-x64** from a successful [CI run](https://github.com/Accelerator6666/FaderDeck/actions), extract the entire ZIP (including DLLs), and launch `FaderDeck.Studio.exe`.

From source with .NET 10 SDK:
```powershell
dotnet run --project software/FaderDeck.Studio -c Release
```

- **Simulation:** four virtual faders, each with eight independent banks; no serial or physical hardware.
- **OBS Studio:** enable the built-in WebSocket server in OBS, enter its address (default `ws://127.0.0.1:4455`) and password, then Connect. The preview discovers audio-capable inputs, reads volume and listens for changes.
- OBS credentials are kept in memory only. Non-local plaintext `ws://` is refused.
- **Limitations:** OBS integration has no live-instance acceptance test yet; inputs default to automatic sequential assignment and can be explicitly mapped or unassigned per bank; GUI does not control real motors.

See [Studio guide (简体中文)](docs/STUDIO.zh-CN.md).

## REAPER OSC preview

Select **“打开 REAPER OSC 控制台”** in the Studio window. A separate four-fader GUI sends normalized OSC track-volume values to local REAPER (default UDP 8000) and listens for feedback (default UDP 9000). The UI distinguishes UDP send from verified feedback. Configure REAPER's OSC surface before use; real REAPER integration remains unverified.

See [REAPER instructions (中文)](docs/REAPER.zh-CN.md).

## Hardware (M0)

- ESP32-S3 development board exposing its **native USB Serial/JTAG** port
- FaderBuddy board with current protocol-v5 firmware and 60 mm motorized fader
- Separate regulated 5 V motor supply; ESP32-S3 3.3 V logic supply
- Common GND, SDA and SCL; default fader I2C address **0x20**
- Example GPIO mapping: **SDA GPIO8 / SCL GPIO9** (edit for your board)

**Do not join 5 V motor power directly to the ESP32-S3 3.3 V rail.** Confirm the actual board pinout, safe current capacity, fader address and touch-compatible cap before powering motors. Never connect/disconnect modules under power during this prototype.

## Firmware build (ESP-IDF 5.5)

```bash
cd firmware/esp32-s3
idf.py set-target esp32s3
idf.py build
idf.py -p <SERIAL_PORT> flash
```

The M0 firmware uses the ESP32-S3 **hardware USB Serial/JTAG** CDC interface, **not TinyUSB** and **not USB MIDI**. Host serial commands are specified in [docs/PROTOCOL.md](docs/PROTOCOL.md). On connection, the fader does not move automatically.

## Windows diagnostic CLI

Install [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet run --project software/FaderDeck.Cli -- ports
dotnet run --project software/FaderDeck.Cli -- COM5 ping
dotnet run --project software/FaderDeck.Cli -- COM5 info
dotnet run --project software/FaderDeck.Cli -- COM5 diagnose
dotnet run --project software/FaderDeck.Cli -- COM5 watch 20
dotnet run --project software/FaderDeck.Cli -- COM5 state
dotnet run --project software/FaderDeck.Cli -- COM5 move 0 128 128
dotnet run --project software/FaderDeck.Cli -- COM5 layer 0
```

You can also download a ready-to-run Windows x64 diagnostic executable and ESP32-S3 build binaries as **Actions artifacts** from a successful [CI run](https://github.com/Accelerator6666/FaderDeck/actions). The firmware bundle's `flasher_args.json` gives the flashing offsets; confirm them before using a separate flashing tool.

**Warning:** `move` powers the physical motor, and `calibrate` sweeps the entire travel. Keep fingers, cables and obstacles clear. Test `ping` and `state` first.

Run dependency-free core tests:

```bash
dotnet run --project tests/FaderDeck.Core.Tests -c Release
```

## Source structure

- `firmware/esp32-s3` — ESP-IDF single-fader M0
- `software/FaderDeck.Core` — device framing and safe state gate
- `software/FaderDeck.Cli` — serial hardware diagnostics
- `software/FaderDeck.Studio` — Avalonia desktop preview
- `software/FaderDeck.Obs` — OBS WebSocket v5 adapter
- `tests` — dependency-free host logic tests
- `profiles` — future adapter configuration examples
- `docs` — protocol, architecture and roadmap

## Attribution and license

Licensed under Apache-2.0. FaderBuddy firmware, PCB design and motor-control work are maintained in the [upstream FaderBuddy](https://github.com/scottbez1/FaderBuddy) project, also Apache-2.0. FaderDeck talks to its documented protocol; it does not copy its firmware into this repository.

The [Accelerator6666/FaderBuddy](https://github.com/Accelerator6666/FaderBuddy) fork remains an independent dependency.
