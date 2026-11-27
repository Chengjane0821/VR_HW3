using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;

// One active Quest. Bind only the explicitly chosen LAN interface (loopback by default).
internal static class Program
{
    static readonly object gate = new();
    static TcpClient? active;
    static volatile bool serialFailed;
    static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest();
        string com = args.Length > 0 ? args[0] : "COM5";
        if (!int.TryParse(args.Length > 1 ? args[1] : "9600", out int baud) || baud <= 0 ||
            !int.TryParse(args.Length > 2 ? args[2] : "8888", out int port) || port < 1 || port > 65535 ||
            !IPAddress.TryParse(args.Length > 3 ? args[3] : "127.0.0.1", out var address))
        {
            Console.Error.WriteLine("Usage: ArduinoBridge [COM5] [9600] [8888] [PC-LAN-IP] | --self-test");
            return 2;
        }
        using var stop = new CancellationTokenSource();
        using var serial = new SerialPort(com, baud) { ReadTimeout = 200, WriteTimeout = 500, NewLine = "\n" };
        var listener = new TcpListener(address, port);
        Thread? serialWorker = null;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        try
        {
            serial.Open();
            Console.WriteLine($"Serial opened: {com} / {baud}. Close Arduino Serial Monitor first.");
            listener.Start();
            Console.WriteLine($"TCP listening: {address}:{port}; trusted LAN only. Ctrl+C to stop.");
            serialWorker = new Thread(() => ReadSerial(serial, stop)) { IsBackground = true };
            serialWorker.Start();
            while (!stop.IsCancellationRequested)
            {
                using var client = listener.AcceptTcpClientAsync(stop.Token).AsTask().GetAwaiter().GetResult();
                client.NoDelay = true;
                client.SendTimeout = 500;
                lock (gate) active = client;
                Console.WriteLine($"Connected: {client.Client.RemoteEndPoint}");
                try
                {
                    var frames = new WireProtocol();
                    byte[] bytes = new byte[256];
                    using var stream = client.GetStream();
                    while (!stop.IsCancellationRequested)
                    {
                        int count = stream.ReadAsync(bytes.AsMemory(), stop.Token).AsTask().GetAwaiter().GetResult();
                        if (count == 0) break;
                        frames.Feed(Encoding.ASCII.GetString(bytes, 0, count), line =>
                        {
                            if (WireProtocol.IsLed(line))
                            {
                                serial.Write(line + "\n");
                                Console.WriteLine("Serial TX " + line);
                            }
                        });
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (Exception e) { Console.Error.WriteLine("Client: " + e.Message); }
                finally
                {
                    lock (gate) active = null;
                    Console.WriteLine("Disconnected; waiting for another client.");
                }
            }
            return serialFailed ? 1 : 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return serialFailed ? 1 : 0; }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        finally
        {
            stop.Cancel();
            listener.Stop();
            lock (gate) { active?.Dispose(); active = null; }
            serialWorker?.Join();
        }
    }
    static void ReadSerial(SerialPort serial, CancellationTokenSource stop)
    {
        var frames = new WireProtocol();
        var nextLog = DateTime.MinValue;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                string data = serial.ReadExisting();
                frames.Feed(data, line =>
                {
                    if (!WireProtocol.TryPot(line, out int value)) return;
                    if (DateTime.UtcNow >= nextLog)
                    {
                        Console.WriteLine("Serial RX POT:" + value);
                        nextLog = DateTime.UtcNow.AddSeconds(2);
                    }
                    byte[] bytes = Encoding.ASCII.GetBytes("POT:" + value + "\n");
                    lock (gate)
                    {
                        if (active == null) return;
                        try { active.GetStream().Write(bytes, 0, bytes.Length); }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine("TCP TX: " + e.Message);
                            active.Dispose(); active = null;
                        }
                    }
                });
                stop.Token.WaitHandle.WaitOne(10);
            }
            catch (Exception e)
            {
                serialFailed = true;
                Console.Error.WriteLine("Serial failed; reconnect Arduino and restart bridge: " + e.Message);
                stop.Cancel();
            }
        }
    }
    static int SelfTest()
    {
        var received = new List<string>();
        var frames = new WireProtocol();
        frames.Feed("PO", received.Add);
        frames.Feed("T:512\r\nLED:1\nLED:", received.Add);
        frames.Feed("0\n" + new string('x', 100) + "LED:1\nPOT:1023\n", received.Add);
        bool ok = received.SequenceEqual(new[] { "POT:512", "LED:1", "LED:0", "POT:1023" }) &&
            WireProtocol.TryPot("POT:0", out _) && WireProtocol.TryPot("POT:1023", out _) &&
            !WireProtocol.TryPot("POT:1024", out _) && !WireProtocol.TryPot("POT:-1", out _) &&
            !WireProtocol.TryPot("POT:abc", out _) && WireProtocol.IsLed("LED:0") &&
            !WireProtocol.IsLed("LED:2") && !WireProtocol.IsLed(" LED:1");
        Console.WriteLine(ok ? "PASS: split/coalesced/CRLF/oversized frames and protocol validation" : "FAIL");
        return ok ? 0 : 1;
    }
}
