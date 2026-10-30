# ACS HAL

C# hardware adapter layer for an 8-axis ACS SPiiPlus machine (CMxa controller + UDMdx drives),
developed against the SPiiPlus controller simulator.

| Axes | Role | Drive |
|---|---|---|
| X1, X2 | X gantry (dual drive, true gantry) | Linear motors, one linear encoder each |
| Y1, Y2 | Y gantry (dual drive, true gantry) | Linear motors, one linear encoder each |
| Z | Z | Rotary servo + ball screw, controlled in mm |
| Theta | Theta | Rotary servo + ball screw, controlled in mm |
| Door1, Door2 | Doors | Servo motors (mechanism TBD) |

## Units and scaling

All positions are in mm. Each axis's `mechanics` in the config gives the encoder scale, which on ACS
is the per-axis `EFAC` (mm per encoder count):

- `LinearMotor`: `EFAC = encoderResolutionMm`
- `BallScrew`: `EFAC = leadMm / (countsPerMotorRev × gearRatio)`

On the real controller, EFAC is set and saved in MMI during commissioning; the HAL only verifies it
(`AxisScaling.Verify`). On the simulator, the smoke test writes it (`AxisScaling.Apply`). EFAC can only
be changed while the motor is disabled. Both motors of a gantry must have identical mechanics.

## Layout

```
config/
  machine.sim.json     Axis map for the simulator
  machine.hw.json      Axis map for the real machine (placeholders, marked TBD)
src/
  AcsHal.Core/         Vendor-neutral interfaces and config (IMotionController, IAxis, MachineConfig)
  AcsHal.Acs/          Implementation on the ACS SPiiPlus .NET library
  AcsHal.SmokeTest/    Console check against the simulator or a controller
  AcsHal.Viewer/       WPF app showing the machine through the HAL
lib/
  ACS/                 ACS.SPiiPlusNET6.dll (the build compiles against this; see VERSION.txt)
docs/
  to-do-after-simulation.md   Steps from simulator to the real machine
```

## Prerequisites

| Needed for | What |
|---|---|
| Building | .NET SDK 10 only. The ACS .NET library is checked in under [lib/ACS](lib/ACS). |
| Running (simulator or hardware) | SPiiPlus ADK Suite installed (see below) |

A fresh clone builds with `dotnet build` and no ACS software. To run anything, the ADK must be
installed: the .NET library calls the native `ACSCL_x64.dll`, which talks to the ACS User Mode Driver
(`ACSCSRV.exe`), and the simulator itself (`sb4.exe`) runs under that driver. These are installed
system components, not files the repo can carry.

Keep [lib/ACS](lib/ACS) at the same version as the installed ADK. After upgrading the ADK, copy
`ACS.SPiiPlusNET6.dll` from `C:\Program Files (x86)\ACS Motion Control\SPiiPlus Runtime Kit\CommonFiles\`
into `lib/ACS` and update `lib/ACS/VERSION.txt`. To build against the installed copy instead, set
`AcsNetLibPath` in [Directory.Build.props](Directory.Build.props).

## Getting the ACS software (SPiiPlus ADK Suite)

The simulator is not a separate download. It comes with the **SPiiPlus ADK Suite**, which also
installs MMI Application Studio, the User Mode Driver, the C/.NET libraries, and the documentation
(`C:\Program Files (x86)\ACS Motion Control\SPiiPlus Documentation Kit`).

| Version | Where it came from |
|---|---|
| 4.20.01 (target, matches the controller) | `http://203.74.0.111:82/data/ACS/control/SPiiPlus-ADK-Suite-v.4.20.01.zip` (company file server). 1,935,188,116 bytes, SHA-256 `5a8415feb26994f2a580de4889fb9a4304602c746a1948d8c65154c71b5566e9`. `setup.exe` inside is code-signed by ACS Motion Control Ltd. |
| 3.13.01 (currently used for development) | Was already installed on the development PC. |
| Any version | ACS website ([acsmotioncontrol.com](https://acsmotioncontrol.com/capabilities/application-development/mmi-motion-controller-simulator/)); downloads require an ACS account. |

To install:

1. Close MMI Application Studio and anything else connected to the controller or simulator.
2. Unzip the download and run `setup.exe` as administrator. If it does not offer to upgrade an
   existing ADK, uninstall the old "SPiiPlus ADK Suite" from Settings > Apps first.
3. Choose the full install (MMI Application Studio, simulator, libraries, User Mode Driver), and
   reboot if asked.
4. Check: connect MMI to **Simulator**, then `dotnet run --project src/AcsHal.SmokeTest`; the
   firmware line shows the installed version.

## Build and run

```
dotnet build
dotnet run --project src/AcsHal.SmokeTest                            # simulator (default)
dotnet run --project src/AcsHal.SmokeTest -- config/machine.hw.json  # real controller, read-only
```

Or open `AcsHal.sln` in Visual Studio and pick a startup project.

### Viewer

```
dotnet run --project src/AcsHal.Viewer
```

Pick a config and press Connect. The viewer shows the machine as the HAL sees it: the XY table from
above (click it to move there), Z/Theta/door stroke bars, and a card per machine axis (X and Y as
gantries) with enable, jog (hold), move-to, halt and gantry engage/release. The bottom panel has the
log, an ACSPL+ terminal, and the raw controller axes.

- Simulator: on connect, all axes are disabled, EFAC is applied from the config, and both gantries
  are engaged. Motion is allowed only on engaged gantries, so one side is never moved alone.
- Hardware: read-only. Motion buttons are disabled and the terminal only sends `?` queries;
  Disable all and KILL ALL always work.
- Status is polled every 50 ms with one vector read per variable. All library calls go through one
  lock per connection, because ACS does not document the library as thread-safe.

### Watching the simulator in MMI

Several programs can connect to the simulator at once and all see the same axes. (Tested with the
viewer plus a second program: moves from either side showed up in the other.) To watch the
simulator in MMI Application Studio while you use the viewer:

1. Start MMI Application Studio
   (`C:\Program Files (x86)\ACS Motion Control\SPiiPlus ADK Suite v3.13.01\SPiiPlus MMI Application Studio\ACS.Framework.exe`,
   or from the Start menu).
2. Connect to a controller and choose **Simulator** as the connection type.
3. Open **Motion Manager** for axis positions and states, or **Scope** to plot `FPOS` over time.
4. Move axes in the viewer and watch MMI follow. It works the other way too: jog in MMI and the
   viewer's drawing moves.

Things to know:

- MMI shows raw controller axes, not X/Y/Z. In `machine.sim.json`: X = axes 0 + 2, Y = 1 + 3,
  Z = 4, Theta = 5, Door1 = 6, Door2 = 7.
- Both programs control the same simulator. Disabling an axis or turning off gantry mode in MMI shows
  up in the viewer (gantry mode within about a second, which greys out X/Y motion).
- The simulator keeps its state until it is restarted; positions are not reset when you reconnect.
- Disconnecting the viewer does not stop the simulator, so MMI stays connected.

Against the simulator, the smoke test moves every axis and checks true-gantry mode. Against a real
controller it only reads; it never enables or moves anything. The simulator is started
automatically on connect.

## Simulator findings (ADK 3.13.01)

- The simulator has 128 axes (`SYSINFO(13)`); all are ordinary closed-loop axes with no flags set
  beyond the defaults (`MFLAGS = 0x000A0000`: #DEFCON, #ENMOD).
- **True gantry works, but only on fixed pairs 0↔2 and 1↔3** (4-axis servo processor layout).
  `SETCONF(267,…)` is accepted but does not change the pairing, and leaves the simulator in an
  inconsistent state until it is restarted. `machine.sim.json` therefore puts X on 0/2 and Y on 1/3.
- In gantry mode, enabling the COG axis enables both motors; FPOS(COG) is the average position and
  FPOS(yaw) the difference, as described in *AN-True-Gantry-System-Support*.
- **Quirk:** the first `ENABLE` of a gantry pair usually enables only the yaw motor and fails with
  error 3065 ("motion in progress"). A second `ENABLE` about 100 ms later succeeds. ACS's own gantry
  example uses a single `ENABLE`, so this is expected to be simulator-only; verify on hardware.
- `Api.Transaction` returns `null` (not an empty string) for commands with no reply.
- EFAC defaults to 1 and is stored, but the simulator does not rescale positions with it, so unit
  conversion can only really be checked on hardware.

On the real CMxa + UDMdx system, the gantry pairs depend on the drives' servo processors: both
motors of a gantry must be on the same one (e.g. the same UDMdx). Check the axis numbering in
MMI > System Setup before filling in `machine.hw.json`.

## Open questions

- Mechanics values: linear encoder resolution, screw leads, motor encoder counts/rev, gearing
  (the configs hold placeholders)
- Door mechanism (screw/belt), open/closed positions, and interlocks; Z brake and safe height
- Real axis numbering and controller IP
- Firmware version on the controller (target ADK: 4.20.01)

For the steps from the simulator to the real machine, see
[docs/to-do-after-simulation.md](docs/to-do-after-simulation.md).
