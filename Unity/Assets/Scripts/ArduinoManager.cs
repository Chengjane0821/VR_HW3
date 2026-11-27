using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.IO.Ports;
#endif

[DisallowMultipleComponent]
public class ArduinoManager : MonoBehaviour
{
    public enum TransportMode { TcpClient, WindowsSerial }
    public TransportMode mode = TransportMode.TcpClient;
    public string serverIP = "192.168.1.100";
    public int serverPort = 8888;
    public string portName = "COM5";
    public int baudRate = 9600;
    public int potValue;
    [SerializeField] string connectionStatus = "Disconnected";
    public string ConnectionStatus { get { return connectionStatus; } }
    public string LastError { get { lock (stateLock) return lastError; } }
    readonly object stateLock = new object();
    string workerStatus = "Disconnected", lastError = "";
    int latestPot, pendingLed = -1;
    volatile bool connected;
    Thread worker;
    ManualResetEvent stop;
    float nextLog;
    string lastLog;

    void OnEnable()
    {
        stop = new ManualResetEvent(false);
        latestPot = potValue;
        // Snapshot Inspector fields on the Unity thread.
        string host = serverIP, port = portName;
        int tcpPort = serverPort, baud = baudRate;
        TransportMode selected = mode;
#if UNITY_ANDROID && !UNITY_EDITOR
        selected = TransportMode.TcpClient;
#endif
        worker = new Thread(() => Run(selected, host, tcpPort, port, baud));
        worker.IsBackground = true;
        worker.Name = "Arduino transport";
        worker.Start();
    }
    void Update()
    {
        potValue = Volatile.Read(ref latestPot);
        string error;
        lock (stateLock) { connectionStatus = workerStatus; error = lastError; }
        string message = connectionStatus + (error.Length == 0 ? "" : ": " + error);
        if (message != lastLog && Time.unscaledTime >= nextLog)
        {
            Debug.Log("Arduino " + message, this);
            lastLog = message;
            nextLog = Time.unscaledTime + 5f;
        }
    }
    void State(string status, string error = "")
    {
        lock (stateLock) { workerStatus = status; lastError = error; }
    }
    void Receive(string line)
    {
        int value;
        if (WireProtocol.TryPot(line, out value)) Volatile.Write(ref latestPot, value);
    }
    public void LEDOn() { QueueLed(1); }
    public void LEDOff() { QueueLed(0); }
    void QueueLed(int value)
    {
        // No stale commands replayed on reconnect; rapid clicks coalesce to the latest state.
        if (connected) Interlocked.Exchange(ref pendingLed, value);
    }
    void Run(TransportMode selected, string host, int tcpPort, string port, int baud)
    {
        while (!stop.WaitOne(0))
        {
            State("Connecting");
            try
            {
                if (selected == TransportMode.WindowsSerial)
                {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    using (var serial = new SerialPort(port, baud))
                    {
                        serial.ReadTimeout = 100;
                        serial.WriteTimeout = 500;
                        serial.Open();
                        connected = true;
                        State("Connected");
                        var frames = new WireProtocol();
                        while (!stop.WaitOne(10))
                        {
                            frames.Feed(serial.ReadExisting(), Receive);
                            int led = Interlocked.Exchange(ref pendingLed, -1);
                            if (led >= 0) serial.Write("LED:" + led + "\n");
                        }
                    }
#else
                    throw new PlatformNotSupportedException("Windows Serial requires Windows Editor/player.");
#endif
                }
                else RunTcp(host, tcpPort);
            }
            catch (Exception e) { State("Disconnected", e.Message); }
            finally { connected = false; Interlocked.Exchange(ref pendingLed, -1); }
            if (!stop.WaitOne(0)) stop.WaitOne(2000);
        }
        State("Disconnected");
    }
    void RunTcp(string host, int port)
    {
        IPAddress address;
        if (!IPAddress.TryParse(host, out address)) throw new ArgumentException("Server IP must be the Windows LAN IPv4/IPv6 address.");
        using (var client = new TcpClient(address.AddressFamily))
        {
            client.NoDelay = true;
            client.SendTimeout = 500;
            var result = client.BeginConnect(address, port, null, null);
            using (var ready = result.AsyncWaitHandle)
            {
                int done = WaitHandle.WaitAny(new WaitHandle[] { stop, ready }, 3000);
                if (done == 0) return;
                if (done == WaitHandle.WaitTimeout) throw new TimeoutException("TCP connect timeout");
                client.EndConnect(result);
            }
            connected = true;
            State("Connected");
            var frames = new WireProtocol();
            byte[] buffer = new byte[512];
            DateTime lastData = DateTime.UtcNow;
            using (var stream = client.GetStream())
            {
                while (!stop.WaitOne(10))
                {
                    if (client.Client.Poll(0, SelectMode.SelectRead))
                    {
                        int count = stream.Read(buffer, 0, buffer.Length);
                        if (count == 0) throw new System.IO.IOException("Server closed connection");
                        frames.Feed(Encoding.ASCII.GetString(buffer, 0, count), line =>
                        {
                            int value;
                            if (WireProtocol.TryPot(line, out value))
                            {
                                Volatile.Write(ref latestPot, value);
                                lastData = DateTime.UtcNow;
                            }
                        });
                    }
                    if ((DateTime.UtcNow - lastData).TotalSeconds > 5)
                        throw new TimeoutException("No Arduino data for 5 seconds");
                    int led = Interlocked.Exchange(ref pendingLed, -1);
                    if (led >= 0)
                    {
                        byte[] command = Encoding.ASCII.GetBytes("LED:" + led + "\n");
                        stream.Write(command, 0, command.Length);
                    }
                }
            }
        }
    }
    void OnDisable()
    {
        if (stop == null) return;
        stop.Set();
        // Connect wait is cancellable; writes have a 500 ms timeout.
        if (worker != null) worker.Join();
        stop.Dispose();
        stop = null;
        connectionStatus = "Disconnected";
    }
}
