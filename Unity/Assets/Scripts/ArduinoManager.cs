
using UnityEngine;
using System;
using System.IO.Ports;

public class ArduinoManager : MonoBehaviour
{
    public string portName = "COM5";
    public int baudRate = 9600;

    public int potValue = 0;

    private SerialPort serialPort;
    private string buffer = "";

    void Start()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.ReadTimeout = 20;
            serialPort.WriteTimeout = 100;
            serialPort.Open();

            Debug.Log("Arduino Connected!");
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
        }
    }

    void Update()
    {
        if (serialPort == null || !serialPort.IsOpen)
            return;

        try
        {
            buffer += serialPort.ReadExisting();

            int index;

            while ((index = buffer.IndexOf('\n')) >= 0)
            {
                string data = buffer.Substring(0, index).Trim();
                buffer = buffer.Substring(index + 1);

                if (data.StartsWith("POT:"))
                {
                    if (int.TryParse(data.Substring(4), out int value))
                    {
                        potValue = Mathf.Clamp(value, 0, 1023);
                        Debug.Log("POT: " + potValue);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning(e.Message);
        }
    }

    public void LEDOn()
    {
        SendCommand("LED:1");
    }

    public void LEDOff()
    {
        SendCommand("LED:0");
    }

    void SendCommand(string command)
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            try
            {
                serialPort.Write(command + "\n");
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
            }
        }
    }

    void OnDestroy()
    {
        if (serialPort != null)
        {
            serialPort.Close();
            serialPort.Dispose();
        }
    }
}
