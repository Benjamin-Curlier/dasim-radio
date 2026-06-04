# Technology stack (Phase 2 plan)

Companion to the frozen [architecture](architecture.md). This file records the **library
choices** and their verification status. Versions are intentionally **not pinned by hand**:
add packages with `dotnet add <project> package <id>` so NuGet resolves the real latest, then
run `dotnet list package --outdated` periodically.

## Runtime

**.NET 10 (LTS).** Not .NET 11 (STS: ~2-year life, raises the hardware baseline to
x86-64-v2, no GC pause-latency win — nothing for a real-time audio hot path). Re-evaluate at
.NET 12 LTS (expected Nov 2027). Server publish target: `-r linux-x64`.

## Libraries

| Concern | Choice | Notes |
|---|---|---|
| Messaging | `NATS.Net` (+ JetStream / Services / KV) | Core for voice, JetStream/KV/Services for control plane |
| Codec — client | `Concentus` (managed Opus) | Zero native deps → simple Avalonia packaging |
| Codec — media service | `OpusSharp` + `OpusSharp.Natives` | Native libopus shipped for win-x64 **and** linux-x64 in `runtimes/<rid>/native/` → trivial CI, no hand-built assets |
| Client UI | `Avalonia` (+ `Avalonia.Desktop`) | Native cross-platform desktop |
| Client audio I/O | `OwnAudioSharp` | Lock-free/GC-free, PortAudio + miniaudio fallback, single NuGet |
| Global PTT hotkey | `SharpHook` (libuiohook) | System-wide, works unfocused — **X11 only** (see risks) |
| Logging | `Serilog.Extensions.Hosting` | Structured logs in the hosts |
| Hosting | `Microsoft.Extensions.Hosting` | Generic host for agent / media service |
| Tests | `xunit.v3`, `Microsoft.NET.Test.Sdk`, `coverlet`, `Microsoft.Extensions.TimeProvider.Testing`, `Testcontainers` | Test.Sdk 18.x and coverlet 10.x are **confirmed real** (resolved by NuGet in this repo) |

## Codec abstraction (de-risks "libopus")

Define the seam in `Dasim.Radio.Audio`:

- `IOpusEncoder` / `IOpusEncoderFactory`, `IOpusDecoder` — media service binds to OpusSharp,
  client binds to Concentus.
- **One encoder instance per stream, reused across frames** (Opus encoder state is stateful);
  pool PCM/output buffers; never allocate per 20 ms frame.
- `opus_encode` is synchronous and CPU-bound — fan ~50 encodes across the thread pool.
- Tuning levers: complexity 10 → 5–7 (near-transparent for voice, much cheaper),
  `OPUS_APPLICATION_VOIP`, optional DTX. Default 20 ms / 960 samples @ 48 kHz.

Feasibility: ~50 encodes / 20 ms is comfortably within a few cores; **measure with
BenchmarkDotNet on the real deployment CPU before sizing** (the de-risk PoC).

## Top integration risks (spike before building around them)

1. **Wayland breaks the global PTT.** libuiohook is X11-only; on a Wayland session the
   unfocused hotkey silently fails. Detect `XDG_SESSION_TYPE`, require Xorg on target
   distros, or accept focused-only on Wayland. **Validate first on the real target distro.**
2. **Linux audio latency via PipeWire compat.** PortAudio/miniaudio reach PipeWire through
   ALSA/Pulse emulation; for sub-10 ms tune the PipeWire quantum (64–128 @ 48 kHz) and grant
   RT scheduling. Verify input-device enumeration on Linux.
3. **OwnAudioSharp maturity / full-duplex sync.** Small project — prove synchronized
   full-duplex capture+playback with stable matched latency, and that native binaries ship
   for both RIDs. Fallback: raw miniaudio P/Invoke.
4. **Native libopus glibc baseline.** A `libopus.so` built on a newer Ubuntu may not load on
   older target hosts — match the deployment glibc baseline if hand-building (not needed with
   OpusSharp.Natives, but keep in mind).

## Verification posture

Versions returned by research were treated as **hypotheses**. Confirmed empirically here:
xUnit v3 3.2.2, Microsoft.NET.Test.Sdk 18.6.0, coverlet.msbuild 10.0.1,
Microsoft.Extensions.TimeProvider.Testing 10.6.0 (NuGet resolved them). Everything else
(Avalonia 12.x, OpusSharp/native libopus tag, OwnAudioSharp, SharpHook, NATS.Net,
Serilog.*, Testcontainers) must be confirmed via `dotnet add package` / nuget.org when each
project is created — do not copy version numbers from a report into csproj.
