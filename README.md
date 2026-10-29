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
```

## Prerequisites

- .NET SDK 10
- SPiiPlus ADK Suite (installs the .NET library, the simulator, and the User Mode Driver)

The build references `ACS.SPiiPlusNET6.dll` from the SPiiPlus Runtime Kit, which each ADK install
updates. To use a different copy, set `AcsNetLibPath` in [Directory.Build.props](Directory.Build.props).

## Build and run

```
dotnet build
dotnet run --project src/AcsHal.SmokeTest                            # simulator (default)
dotnet run --project src/AcsHal.SmokeTest -- config/machine.hw.json  # real controller, read-only
```

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
