using System.Text.Json.Serialization;

namespace AcsHal.Core;

/// <summary>
/// How an axis's encoder counts map to machine motion. <see cref="UnitsPerCount"/> is the scale the
/// controller must use (EFAC on ACS) so that positions are in <see cref="Unit"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LinearMotorMechanics), "LinearMotor")]
[JsonDerivedType(typeof(BallScrewMechanics), "BallScrew")]
public abstract record AxisMechanics
{
    public abstract double UnitsPerCount { get; }
    public abstract string Unit { get; }

    internal abstract void Validate(string axisName);

    private protected static void RequirePositive(double value, string field, string axisName)
    {
        if (!(value > 0) || double.IsInfinity(value))
            throw new InvalidDataException($"Axis '{axisName}': mechanics.{field} must be a positive number.");
    }
}

/// <summary>Direct-drive linear motor read by a linear encoder.</summary>
/// <param name="EncoderResolutionMm">Distance per encoder count, in mm.</param>
public sealed record LinearMotorMechanics(double EncoderResolutionMm) : AxisMechanics
{
    public override double UnitsPerCount => EncoderResolutionMm;
    public override string Unit => "mm";

    internal override void Validate(string axisName) =>
        RequirePositive(EncoderResolutionMm, "encoderResolutionMm", axisName);

    public override string ToString() => $"linear motor, {EncoderResolutionMm} mm/count";
}

/// <summary>Rotary motor turning a ball/lead screw, with the encoder on the motor.</summary>
/// <param name="LeadMm">Travel per screw revolution, in mm.</param>
/// <param name="CountsPerMotorRev">Motor encoder counts per motor revolution.</param>
/// <param name="GearRatio">Motor revolutions per screw revolution (1 for direct coupling).</param>
public sealed record BallScrewMechanics(double LeadMm, double CountsPerMotorRev, double GearRatio = 1) : AxisMechanics
{
    public override double UnitsPerCount => LeadMm / (CountsPerMotorRev * GearRatio);
    public override string Unit => "mm";

    internal override void Validate(string axisName)
    {
        RequirePositive(LeadMm, "leadMm", axisName);
        RequirePositive(CountsPerMotorRev, "countsPerMotorRev", axisName);
        RequirePositive(GearRatio, "gearRatio", axisName);
    }

    public override string ToString() => $"ball screw, lead {LeadMm} mm, {CountsPerMotorRev} counts/rev, gear {GearRatio}:1";
}
