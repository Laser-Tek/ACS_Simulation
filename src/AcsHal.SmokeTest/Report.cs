namespace AcsHal.SmokeTest;

/// <summary>Console output for the smoke test: one line per check, with a pass/fail summary at the end.</summary>
internal sealed class Report
{
    public int Passed { get; private set; }
    public int Failed { get; private set; }
    public int Warnings { get; private set; }

    public void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title}");
    }

    public void Pass(string message) { Passed++; Write("PASS", ConsoleColor.Green, message); }
    public void Fail(string message) { Failed++; Write("FAIL", ConsoleColor.Red, message); }
    public void Warn(string message) { Warnings++; Write("WARN", ConsoleColor.Yellow, message); }
    public void Info(string message) => Write("INFO", ConsoleColor.Gray, message);

    public void Check(bool ok, string message, string? failDetail = null)
    {
        if (ok) Pass(message);
        else Fail(failDetail is null ? message : $"{message}: {failDetail}");
    }

    public void Summary()
    {
        Console.WriteLine();
        Console.WriteLine($"Summary: {Passed} passed, {Failed} failed, {Warnings} warnings");
    }

    private static void Write(string tag, ConsoleColor color, string message)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write($"[{tag}] ");
        Console.ForegroundColor = previous;
        Console.WriteLine(message);
    }
}
