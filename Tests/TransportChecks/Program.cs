using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void Invoke(ArduinoManager manager, string method) =>
    typeof(ArduinoManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null);
static async Task Until(ArduinoManager manager, Func<bool> condition, int timeout = 7000)
{
    var end = DateTime.UtcNow.AddMilliseconds(timeout);
    do
    {
        Invoke(manager, "Update");
        if (condition()) return;
        await Task.Delay(10);
    } while (DateTime.UtcNow < end);
    throw new Exception("Timed out waiting for transport state");
}
static async Task Send(NetworkStream stream, string data) =>
    await stream.WriteAsync(Encoding.ASCII.GetBytes(data));
static async Task<string> Line(NetworkStream stream)
{
    using var stop = new CancellationTokenSource(3000);
    var text = new StringBuilder();
    byte[] one = new byte[1];
    while (await stream.ReadAsync(one.AsMemory(), stop.Token) > 0)
    {
        if (one[0] == 10) return text.ToString();
        text.Append((char)one[0]);
    }
    throw new Exception("Unexpected EOF");
}
var frames = new WireProtocol();
var lines = new List<string>();
frames.Feed("PO", lines.Add);
frames.Feed("T:512\r\nLED:1\nLED:", lines.Add);
frames.Feed("0\n" + new string('x', 65) + "LED:1\nPOT:1023\n", lines.Add);
Assert(lines.SequenceEqual(new[] { "POT:512", "LED:1", "LED:0", "POT:1023" }), "Framing");
foreach (string invalid in new[] { "POT:-1", "POT:1024", "POT:abc", "POT: 512", "POT:999999999999" })
    Assert(!WireProtocol.TryPot(invalid, out _), "Invalid POT accepted");
Assert(WireProtocol.TryPot("POT:0", out _) && WireProtocol.TryPot("POT:1023", out _), "Endpoints");
Assert(!WireProtocol.IsLed("LED:2") && !WireProtocol.IsLed("LED:1junk"), "Invalid LED accepted");
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var manager = new ArduinoManager { serverIP = "127.0.0.1", serverPort = ((IPEndPoint)listener.LocalEndpoint).Port };
Invoke(manager, "OnEnable");
try
{
    using (var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5)))
    {
        using var stream = peer.GetStream();
        await Until(manager, () => manager.ConnectionStatus == "Connected");
        await Send(stream, "PO");
        await Task.Delay(30);
        await Send(stream, "T:512\r\nPOT:1023\n");
        await Until(manager, () => manager.potValue == 1023);
        manager.LEDOn();
        Assert(await Line(stream) == "LED:1", "LED ON");
        manager.LEDOff();
        Assert(await Line(stream) == "LED:0", "LED OFF");
    }
    await Until(manager, () => manager.ConnectionStatus == "Disconnected");
    manager.LEDOn(); // Must not replay while disconnected.
    using (var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(7)))
    {
        using var stream = peer.GetStream();
        await Send(stream, "POT:0\n");
        await Until(manager, () => manager.ConnectionStatus == "Connected" && manager.potValue == 0);
        await Task.Delay(100);
        Assert(!stream.DataAvailable, "Stale LED command replayed after reconnect");
        // No further POT: client must detect an otherwise silent connection.
        await Until(manager, () => manager.ConnectionStatus == "Disconnected", 6500);
    }
}
finally
{
    var clock = System.Diagnostics.Stopwatch.StartNew();
    Invoke(manager, "OnDisable");
    listener.Stop();
    Assert(clock.ElapsedMilliseconds < 2000, "Slow shutdown");
}
Console.WriteLine("PASS: framing, input validation, actual ArduinoManager TCP RX/TX, reconnect, stale command suppression, idle timeout and shutdown.");
