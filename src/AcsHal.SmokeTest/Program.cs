// Phase 0 smoke test: connects to the controller from a machine config and reports what it finds.
// Against the simulator it also moves every axis and probes true-gantry mode (MFLAGS.#GANTRY).
// Against real hardware (connection mode Ethernet) it only reads; it never moves anything.
//
// Usage: AcsHal.SmokeTest [path\to\machine.json]   (default: config\machine.sim.json)

using AcsHal.Acs;
using AcsHal.Core;
using AcsHal.SmokeTest;

const int GantryBit = 25;
var enableTimeout = TimeSpan.FromSeconds(5);
var motionTimeout = TimeSpan.FromSeconds(15);
var testProfile = new MotionProfile(Velocity: 50, Acceleration: 500, Deceleration: 500, Jerk: 5000, KillDeceleration: 5000);
const double TestDistance = 10;
const double PositionTolerance = 0.1;

var report = new Report();
var configPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "config", "machine.sim.json");

MachineConfig config;
try
{
    config = MachineConfig.Load(configPath);
}
catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"Cannot load machine config '{configPath}': {ex.Message}");
    return 2;
}

var simulator = config.Connection.Mode == ConnectionMode.Simulator;
Console.WriteLine($"Config: {configPath}");
Console.WriteLine($"Target: {(simulator ? "simulator" : $"controller at {config.Connection.Address}:{config.Connection.Port} (read-only)")}");

using var controller = new AcsMotionController();

report.Section("Connection");
try
{
    controller.Connect(config.Connection);
    report.Pass("Connected");
}
catch (MotionControllerException ex)
{
    report.Fail(ex.Message);
    report.Summary();
    return 1;
}

try
{
    report.Section("Controller");
    var info = controller.GetInfo();
    report.Info($"Firmware: {info.FirmwareVersion}");
    report.Info($"C library: {info.LibraryVersion}");
    report.Info($"Axes on controller: {info.AxisCount}");
    var highestIndex = config.Axes.Max(a => a.Index);
    var allAxesExist = info.AxisCount > highestIndex;
    report.Check(allAxesExist,
        $"Controller has enough axes for the config (needs index {highestIndex})",
        $"controller only has {info.AxisCount}");

    report.Section("Axes (read-only)");
    Console.WriteLine($"  {"Name",-7}{"Idx",4}  {"Mechanics",-12}{"EFAC",12}  {"MFLAGS",-10} {"Flags",-14} {"Enabled",-8}{"FPOS",12}  Faults");
    foreach (var axisConfig in config.Axes.OrderBy(a => a.Index))
    {
        if (axisConfig.Index >= info.AxisCount)
        {
            report.Fail($"{axisConfig.Name}: index {axisConfig.Index} does not exist on the controller");
            continue;
        }
        var axis = controller.GetAxis(axisConfig.Index);
        var mflags = (uint)controller.ReadScalar("MFLAGS", axisConfig.Index);
        var status = axis.GetStatus();
        var mechanics = axisConfig.Mechanics?.GetType().Name.Replace("Mechanics", "") ?? "(TBD)";
        Console.WriteLine($"  {axisConfig.Name,-7}{axisConfig.Index,4}  {mechanics,-12}{axis.GetUnitsPerCount(),12:0.#########}  0x{mflags:X8} {DescribeMflags(mflags),-14} {status.Enabled,-8}{status.FeedbackPosition,12:0.####}  {status.Faults}");
    }

    if (allAxesExist)
    {
        report.Section("Scaling (EFAC = mm per encoder count)");
        if (simulator)
        {
            // Hardware gets its EFAC during commissioning; on the simulator we set it from the config.
            controller.DisableAll();
            foreach (var axisConfig in config.Axes)
                await controller.GetAxis(axisConfig.Index).WaitEnabledAsync(false, enableTimeout);
            AxisScaling.Apply(controller, config);
            report.Info("Applied EFAC from config to the simulator");
        }
        foreach (var result in AxisScaling.Verify(controller, config))
        {
            report.Check(result.Matches,
                $"{result.Axis.Name}: EFAC {result.Expected:0.#########} ({result.Axis.Mechanics})",
                $"controller has {result.Actual:0.#########}");
        }
        foreach (var axisConfig in config.Axes.Where(a => a.Mechanics is null))
            report.Warn($"{axisConfig.Name}: mechanics not configured yet; scaling not checked");
    }

    if (!simulator)
    {
        report.Info("Motion checks skipped: they only run against the simulator.");
    }
    else
    {
        var gantryAxes = config.Gantries.SelectMany(g => new[] { g.CogAxis, g.YawAxis }).ToHashSet(StringComparer.OrdinalIgnoreCase);

        report.Section("Single-axis motion (simulator)");
        foreach (var axisConfig in config.Axes.Where(a => a.Index < info.AxisCount).OrderBy(a => a.Index))
        {
            // Gantry members are exercised as independent axes here, before gantry mode is probed.
            await RunSingleAxisTest(controller.GetAxis(axisConfig.Index), axisConfig.Name + (gantryAxes.Contains(axisConfig.Name) ? " (independent)" : ""));
        }

        report.Section("True-gantry probe (simulator)");
        foreach (var gantry in config.Gantries)
            await ProbeGantry(gantry);
    }
}
catch (MotionControllerException ex)
{
    report.Fail($"Unexpected controller error: {ex.Message}");
}
finally
{
    if (simulator && controller.IsConnected)
    {
        try { controller.DisableAll(); }
        catch (MotionControllerException ex) { report.Warn($"DisableAll at exit failed: {ex.Message}"); }
    }
}

controller.Disconnect();
report.Summary();
return report.Failed == 0 ? 0 : 1;

async Task RunSingleAxisTest(IAxis axis, string name)
{
    try
    {
        axis.ClearFaults();
        axis.SetMotionProfile(testProfile);
        axis.Enable();
        await axis.WaitEnabledAsync(true, enableTimeout);

        var start = axis.GetStatus().FeedbackPosition;
        axis.MoveRelative(TestDistance);
        await axis.WaitMotionDoneAsync(motionTimeout);
        var moved = axis.GetStatus().FeedbackPosition - start;

        axis.MoveAbsolute(start);
        await axis.WaitMotionDoneAsync(motionTimeout);
        var back = axis.GetStatus().FeedbackPosition;

        report.Check(Math.Abs(moved - TestDistance) < PositionTolerance && Math.Abs(back - start) < PositionTolerance,
            $"{name}: enable, move +{TestDistance}, return",
            $"moved {moved:0.####}, returned to {back:0.####} (start {start:0.####})");
    }
    catch (Exception ex) when (ex is MotionControllerException or TimeoutException)
    {
        report.Fail($"{name}: {ex.Message}");
    }
    finally
    {
        TryDisable(axis);
    }
}

async Task ProbeGantry(GantryConfig gantry)
{
    var cogIndex = config.Axis(gantry.CogAxis).Index;
    var yawIndex = config.Axis(gantry.YawAxis).Index;
    var cog = controller.GetAxis(cogIndex);
    var yaw = controller.GetAxis(yawIndex);
    var label = $"Gantry {gantry.Name} (COG axis {cogIndex}, yaw axis {yawIndex})";

    // #GANTRY may only be changed while both motors are disabled.
    TryDisable(cog);
    TryDisable(yaw);
    var originalCog = (uint)controller.ReadScalar("MFLAGS", cogIndex);
    var originalYaw = (uint)controller.ReadScalar("MFLAGS", yawIndex);

    try
    {
        await cog.WaitEnabledAsync(false, enableTimeout);
        await yaw.WaitEnabledAsync(false, enableTimeout);

        controller.Transaction($"MFLAGS({cogIndex}).{GantryBit}=1");
        controller.Transaction($"MFLAGS({yawIndex}).{GantryBit}=1");
        var gantryOn = HasGantryBit((uint)controller.ReadScalar("MFLAGS", cogIndex))
                       && HasGantryBit((uint)controller.ReadScalar("MFLAGS", yawIndex));
        if (!gantryOn)
        {
            report.Warn($"{label}: simulator did not keep MFLAGS.#GANTRY set; true gantry cannot be tested here");
            return;
        }
        report.Pass($"{label}: MFLAGS.#GANTRY accepted on both axes");

        // In true-gantry mode the pair is enabled as one motor. If the two axes are not a valid pair on
        // their servo processor, the enable either fails (error 3065) or enables the COG axis alone.
        // Simulator 3.13.01 quirk: the first ENABLE of a gantry pair often enables only the yaw motor and
        // reports "motion in progress"; another ENABLE ~100 ms later completes it. ACS's own gantry
        // example uses a single ENABLE, so real hardware is expected to need no retry.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                cog.Enable();
                break;
            }
            catch (MotionControllerException ex) when (ex.ErrorCode == 3065 && yaw.GetStatus().Enabled && attempt < 3)
            {
                report.Warn($"{label}: enable attempt {attempt} only enabled yaw (error 3065); retrying");
                await Task.Delay(100);
            }
            catch (MotionControllerException ex) when (ex.ErrorCode == 3065)
            {
                report.Fail($"{label}: enable rejected ({ex.Message}). The axes are probably not a gantry pair on their servo processor");
                return;
            }
        }
        await cog.WaitEnabledAsync(true, enableTimeout);
        await Task.Delay(300);
        if (!yaw.GetStatus().Enabled)
        {
            report.Fail($"{label}: yaw stayed disabled when COG was enabled, so gantry mode is not active. The axes are probably not a gantry pair on their servo processor");
            return;
        }
        report.Pass($"{label}: enabling COG also enabled yaw (gantry enable coupling)");

        var cogStart = cog.GetStatus().FeedbackPosition;
        var yawStart = yaw.GetStatus().FeedbackPosition;
        cog.SetMotionProfile(testProfile);
        cog.MoveRelative(TestDistance);
        await cog.WaitMotionDoneAsync(motionTimeout);
        var cogMoved = cog.GetStatus().FeedbackPosition - cogStart;
        var yawDrift = yaw.GetStatus().FeedbackPosition - yawStart;
        report.Check(Math.Abs(cogMoved - TestDistance) < PositionTolerance && Math.Abs(yawDrift) < PositionTolerance,
            $"{label}: COG move +{TestDistance} with yaw held",
            $"COG moved {cogMoved:0.####}, yaw drifted {yawDrift:0.####}");
        report.Info($"{label}: after move, FPOS({cogIndex})={cog.GetStatus().FeedbackPosition:0.####}, FPOS({yawIndex})={yaw.GetStatus().FeedbackPosition:0.####}");

        cog.MoveRelative(-TestDistance);
        await cog.WaitMotionDoneAsync(motionTimeout);
    }
    catch (Exception ex) when (ex is MotionControllerException or TimeoutException)
    {
        report.Fail($"{label}: {ex.Message}");
    }
    finally
    {
        // Leave the simulator as we found it.
        TryDisable(cog);
        TryDisable(yaw);
        try
        {
            await cog.WaitEnabledAsync(false, enableTimeout);
            await yaw.WaitEnabledAsync(false, enableTimeout);
            controller.Transaction($"MFLAGS({cogIndex}).{GantryBit}={(HasGantryBit(originalCog) ? 1 : 0)}");
            controller.Transaction($"MFLAGS({yawIndex}).{GantryBit}={(HasGantryBit(originalYaw) ? 1 : 0)}");
        }
        catch (Exception ex) when (ex is MotionControllerException or TimeoutException)
        {
            report.Warn($"{label}: could not restore MFLAGS.#GANTRY: {ex.Message}");
        }
    }
}

void TryDisable(IAxis axis)
{
    try { axis.Disable(); }
    catch (MotionControllerException ex) { report.Warn($"Axis {axis.Index}: disable failed: {ex.Message}"); }
}

static bool HasGantryBit(uint mflags) => (mflags & (1u << GantryBit)) != 0;

static string DescribeMflags(uint mflags)
{
    var names = new List<string>();
    if ((mflags & (1u << 0)) != 0) names.Add("DUMMY");
    if ((mflags & (1u << 1)) != 0) names.Add("OPEN");
    if ((mflags & (1u << 3)) != 0) names.Add("HOME");
    if ((mflags & (1u << 4)) != 0) names.Add("STEPPER");
    if ((mflags & (1u << GantryBit)) != 0) names.Add("GANTRY");
    return names.Count == 0 ? "-" : string.Join(",", names);
}
