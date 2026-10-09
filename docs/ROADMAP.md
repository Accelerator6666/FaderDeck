# Roadmap

## M0 — single-fader vertical slice (in progress)

- [x] Repo structure, Chinese/English README, license
- [x] Initial ESP-IDF USB Serial/JTAG and I2C source
- [x] Windows .NET 10 serial diagnostics source
- [x] Host protocol and synchronization gate tests source
- [x] CI build workflows defined
- [x] First GitHub Actions builds verified
- [x] CI downloadable artifacts verified on a successful run
- [x] Hardware-free VirtualFader command emulator and safety tests
- [ ] INFO / DIAGNOSE / WATCH validated with physical FaderBuddy
- [ ] Physical ESP32-S3 + FaderBuddy wiring validated
- [ ] Position readback, touch blocking, manual motor moves tested
- [ ] Disconnect/reconnect and power transient tests completed

## M1 — four-fader controller

- [ ] Four unique I2C addresses, health monitoring and input scan
- [ ] Encoder, button and display modules
- [ ] Safe boot, per-motor power protection, touch handling
- [ ] Device protocol revisions with capability negotiation

## M2 — host service

- [x] Initial OBS WebSocket v5 authentication, volume read/write and input events
- [x] Avalonia Studio preview with 4 virtual faders and 8 banks
- [ ] Verify OBS integration against a live OBS instance
- [ ] Generalized adapter interfaces, reconnect, timestamps and production loop-prevention
- [x] Versioned local JSON mapping profile: eight banks / four slots, Auto / Explicit / Unassigned
- [x] Studio visual OBS input mapping editor with persistent file and safe missing-source behavior
- [x] Adapter-independent v1 JSON profile schema: OBS / REAPER / DaVinci capabilities
- [x] Legacy OBS mapping import helper (original file preserved)
- [ ] Wire shared profile editor into Studio GUI; retire OBS-only storage after migration validation
- [ ] Cross-app YAML import/export (JSON implemented)
- [ ] Device inspector and structured logs
- [ ] USB MIDI (native composite USB device, separately validated)

## M3 — first real two-way integrations

- [ ] OBS WebSocket input volume read/write + change subscriptions
- [x] REAPER localhost UDP OSC volume codec and event adapter (software-only, CI-tested)
- [x] Standalone REAPER OSC Studio preview window, four virtual faders and 8 banks
- [ ] REAPER end-to-end acceptance with an actual REAPER instance
- [ ] Verified feedback-to-motor sync & human touch precedence

## M4 — ecosystem

- [ ] Ableton MIDI mapping
- [ ] DaVinci Resolve control experiment: capability-limited
- [ ] GUI profile editor and diagnostics, Chinese UI

## M5 — pre-release

- [ ] Enclosure / PCB revision, durability and electrical testing
- [ ] Firmware packaging, Windows builds, support documentation
- [ ] v0.1 preview release after hardware validation

Do not advertise full software support until end-to-end integration has been tested on supported versions.
