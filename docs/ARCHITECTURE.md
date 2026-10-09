# FaderDeck architecture

## Boundary and responsibilities

```text
Creative apps (future)
   ↕ App Adapters (OBS WebSocket / REAPER OSC / MIDI etc.)
Windows FaderDeck Studio (preview: local virtual faders and OBS adapter)
   ↕ Future profile engine / generalized application bridge
   ↕ USB MIDI + USB CDC (future composite device)
ESP32-S3 firmware — USB, input events, I2C coordinator
   ↕ I2C @ 3.3 V logic level
FaderBuddy ATtiny1616 — position, touch, closed-loop motor control
```

**M0 uses USB Serial/JTAG CDC only**, communicating with one physical FaderBuddy over I2C. USB MIDI, device-configuration transfer, firmware upgrades and Windows UI are intentionally deferred.

## Design boundaries

- Hardware board owns motor control. Do not duplicate PID logic on host.
- Main controller owns physical-control addressing and serial-device framing.
- Windows core owns parameter states and motor-update safety.
- Future adapters own app-specific APIs. Never claim full parameter readback from apps that cannot provide it.
- Raw hardware position is 8-bit (0..255) in the FaderBuddy protocol v5.
- Only motorize on externally verified data, not stale local caches.
- Handle disconnects, page switching and user touch without uncontrolled movement.

## Status model

`Unknown` — no trustworthy external value; do not reposition.

`Cached` — local historical value; do not reposition automatically.

`Verified` — value freshly observed via an app's supported feedback API; eligible for reposition if the user is not touching the fader.

`SyncGate` in the host core is an initial pure-logic implementation. Transport freshness, touch handoff, timeouts, loop prevention and adapter capability negotiation remain planned work.

## Hardware expansion

FaderBuddy default address range is 0x20–0x27 (set uniquely by jumpers). A single unmodified bus supports eight distinct fader addresses. Other I2C peripherals must avoid collisions. No hot-plug support is assumed.

Encoder/display/button daughterboards, 5V power budget, protection circuitry and a 4-fader backplane are hardware milestones after M0.
