using System;
using System.Globalization;
using System.Text;

// Bounded, incremental ASCII framing. Oversized lines are discarded through the next LF.
public sealed class WireProtocol
{
    readonly StringBuilder line = new StringBuilder(64);
    bool dropping;
    public void Feed(string chunk, Action<string> receive)
    {
        foreach (char c in chunk)
        {
            if (c == '\n')
            {
                if (!dropping) receive(line.ToString().TrimEnd('\r'));
                line.Length = 0;
                dropping = false;
            }
            else if (!dropping)
            {
                if (line.Length >= 64) { line.Length = 0; dropping = true; }
                else line.Append(c);
            }
        }
    }
    public static bool TryPot(string line, out int value)
    {
        value = 0;
        return line.StartsWith("POT:", StringComparison.Ordinal) &&
            int.TryParse(line.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value >= 0 && value <= 1023;
    }
    public static bool IsLed(string line) { return line == "LED:1" || line == "LED:0"; }
}
