# To do after simulation

What comes after the HAL works on the simulator: getting the real CMxa + UDMdx machine moving safely,
then extending the HAL to drive it. Most of steps 1–3 happen in MMI Application Studio and on the
controller, not in this repo.

## 1. Electrical bring-up (before any software)

- Wire the motors, encoders, Hall sensors (if the linear motors have them), limit and home switches,
  door sensors, and the Z brake.
- Wire the safety chain: E-stop to the UDMdx STO (safe torque off) input, plus the door interlocks.
- Connect the PC to the controller by Ethernet and set its IP. Put that IP in
  [machine.hw.json](../config/machine.hw.json).
- Collect the datasheets: motors, encoder resolution, screw lead, gear ratio. These replace the
  placeholder `mechanics` values in the config.

## 2. Commissioning in MMI Application Studio, one axis at a time

Usually done by the electrical or controls engineer, often with an ACS field engineer.

1. **Axis setup:** motor type, feedback type and EFAC for each axis.
2. **Commutation** for the linear motors and servos, so the drive knows the rotor or magnet position.
   The motor must be free to move a little.
3. **Tuning:** current loop, then velocity and position loops, with MMI's frequency-response tools.
4. **Check direction and scale:** command a 10 mm move and measure that the axis really moved 10 mm
   in the expected direction.
5. **Protection limits:**
   - Software travel limits: `SLLIMIT` / `SRLIMIT`
   - Hardware limit switches
   - Position error limits: `CERRI` / `CERRV` / `CERRA`
   - Velocity, acceleration and current limits
6. **Gantry:** commutate X1/X2 and Y1/Y2 carefully. One side cannot travel far on its own because the
   beam is rigid. Then turn on gantry mode and tune the COG and yaw loops. Home both sides to their
   own index or switch so the beam starts square.
7. **Z brake and gravity:** set the brake timing and gravity compensation so Z does not drop when it
   is disabled.
8. **Save to the controller's flash.** The HAL only verifies these values (`AxisScaling.Verify`); it
   never sets them on hardware.

## 3. Controller-side programs (ACSPL+ buffers)

Anything that must still work if the PC or Windows hangs belongs on the controller:

- **Homing routines** for each axis and gantry: switch plus index, then set the zero.
- **Fault autoroutines:** for example, kill motion if a door opens; define what happens on a limit or
  a position error.
- Possibly door open/close sequences with their interlocks.

The HAL then starts and watches these programs rather than doing the work itself.

## 4. HAL work (much of it can start now on the simulator)

| Add | Why |
|---|---|
| Homing (`IAxis.HomeAsync`, or start a homing buffer) | Positions mean nothing until homed |
| Digital I/O interface | Door sensors, interlocks, vacuum, other machine I/O |
| Limit and safety state in `AxisStatus` | Show *why* an axis stopped |
| Reconnect and fault handling | Cable pulls, controller reboot |
| Program-buffer support (load, start, stop, status) | For step 3 |
| A controlled hardware-motion unlock | Hardware is read-only today. Add an explicit commissioning mode with low speed limits, and remove the limits once the machine is verified |

## 5. First motion on the real machine through the HAL

At low speed, with a hand on the E-stop:

1. Enable and disable each axis.
2. Jog each axis.
3. Home.
4. Move to the travel limits and confirm the controller stops the axis.
5. Test the E-stop and the door interlock while moving.

Only then raise the speeds.

## 6. Application layer

Machine sequences, recipes, alarms and the operator UI, built on the HAL.

## Can be done now, without hardware

- Write and test the homing and fault ACSPL+ programs on the simulator.
- Add I/O, homing and buffer support to the HAL.
- Get the real mechanics, axis numbering and controller IP from the electrical team (see
  [Open questions](../README.md#open-questions)).
