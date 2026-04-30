using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;
using NationalInstruments.DAQmx;

namespace QuantumDAQService
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("QuantumDAQService started.");

            while (true)
            {
                using (var server = new NamedPipeServerStream("QuantumDAQPipe", PipeDirection.InOut, 1, PipeTransmissionMode.Message))
                using (var daqController = new DaqController())
                {
                    Console.WriteLine("Waiting for client connection...");
                    server.WaitForConnection();
                    Console.WriteLine("Client connected.");

                    byte[] lengthBuffer = new byte[4];

                    while (server.IsConnected)
                    {
                        int bytesRead = server.Read(lengthBuffer, 0, 4);
                        if (bytesRead == 0) break;

                        int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                        if (messageLength <= 0 || messageLength > 4096) continue;

                        byte[] messageBytes = new byte[messageLength];
                        int totalRead = 0;

                        while (totalRead < messageLength)
                        {
                            int read = server.Read(messageBytes, totalRead, messageLength - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead == messageLength)
                        {
                            string command = Encoding.UTF8.GetString(messageBytes).Trim();
                            string[] parts = command.Split(' ');
                            string cmd = parts[0];

                            try
                            {
                                switch (cmd)
                                {
                                    case "StartAI":
                                        double sampleRateHz = 10000;
                                        if (parts.Length >= 3)
                                            sampleRateHz = double.Parse(parts[2]);
                                        string[] channels = parts[1].Split(',');
                                        daqController.InitializeAnalogInput(channels, sampleRateHz);
                                        daqController.StartContinuousReading();
                                        SendResponse(server, "Analog Input Initialized\n");
                                        break;

                                    case "ReadAI":
                                        int samplesPerChannel = 150;
                                        if (parts.Length >= 2 && int.TryParse(parts[1], out int requestedSamples))
                                            samplesPerChannel = Math.Max(1, Math.Min(requestedSamples, 1000));
                                        double[] values = daqController.GetBufferedData(samplesPerChannel);
                                        string response = string.Join(",", Array.ConvertAll(values, value => value.ToString("G17", CultureInfo.InvariantCulture))) + "\n";
                                        SendResponse(server, response);
                                        break;

                                    case "StartAO":
                                        daqController.InitializeAnalogOutput(parts[1]);
                                        SendResponse(server, "Analog Output Initialized\n");
                                        break;

                                    case "WriteAO":
                                        double voltage = double.Parse(parts[1]);
                                        daqController.WriteAnalogOutput(voltage);
                                        SendResponse(server, "Analog Output Written\n");
                                        break;

                                    case "StopDAQ":
                                        daqController.Dispose();
                                        SendResponse(server, "DAQ Tasks Disposed\n");
                                        break;

                                    case "Exit":
                                        return;

                                    default:
                                        SendResponse(server, "Unknown Command\n");
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                SendResponse(server, "Error: " + ex.Message + "\n");
                            }
                        }
                    }

                    Console.WriteLine("Client disconnected. Waiting for next client...");
                }
            }
        }

        static void SendResponse(NamedPipeServerStream server, string message)
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            byte[] lengthBytes = BitConverter.GetBytes(messageBytes.Length);
            server.Write(lengthBytes, 0, lengthBytes.Length);
            server.Write(messageBytes, 0, messageBytes.Length);
            server.Flush();
        }
    }

    class DaqController : IDisposable
    {
        private NationalInstruments.DAQmx.Task analogInputTask;
        private AnalogMultiChannelReader analogReader;
        private NationalInstruments.DAQmx.Task analogOutputTask;
        private AnalogSingleChannelWriter analogWriter;
        private Thread aiReaderThread;
        private bool aiRunning = false;

        private List<double>[] channelBuffers;
        private int numChannels = 1;
        public string DeviceName { get; set; } = "Dev1";

        public void InitializeAnalogInput(string[] inputChannels, double sampleRateHz)
        {
            analogInputTask = new NationalInstruments.DAQmx.Task();

            foreach (var channel in inputChannels)
            {
                analogInputTask.AIChannels.CreateVoltageChannel(
                    DeviceName + "/" + channel,
                    "",
                    AITerminalConfiguration.Differential,  // FIX: Use Differential for cleaner input
                    -10.0,
                    10.0,
                    AIVoltageUnits.Volts);
            }

            analogInputTask.Timing.ConfigureSampleClock(
                "",
                sampleRateHz,
                SampleClockActiveEdge.Rising,
                SampleQuantityMode.ContinuousSamples,
                (int)(sampleRateHz));

            analogReader = new AnalogMultiChannelReader(analogInputTask.Stream);
            channelBuffers = new List<double>[inputChannels.Length];
            for (int i = 0; i < inputChannels.Length; i++)
            {
                channelBuffers[i] = new List<double>();
            }
        }

        public void StartContinuousReading()
        {
            aiRunning = true;
            aiReaderThread = new Thread(() =>
            {
                while (aiRunning)
                {
                    try
                    {
                        if (analogReader != null)
                        {
                            double[,] data = analogReader.ReadMultiSample(100);
                            int chCount = data.GetLength(0);
                            int numSamples = data.GetLength(1);

                            if (channelBuffers.Length != chCount)
                            {
                                Console.WriteLine("❗ Channel buffer mismatch. Stopping read.");
                                continue;
                            }

                            lock (channelBuffers)
                            {
                                for (int ch = 0; ch < chCount; ch++)
                                {
                                    for (int s = 0; s < numSamples; s++)
                                    {
                                        channelBuffers[ch].Add(data[ch, s]);
                                    }

                                    if (channelBuffers[ch].Count > 1000)
                                    {
                                        channelBuffers[ch].RemoveRange(0, channelBuffers[ch].Count - 1000);
                                    }
                                }
                            }

                            Console.WriteLine($"[DAQ] Read {numSamples} samples × {chCount} channels. CH0 sample: {data[0, 0]:F3}");
                        }
                        Thread.Sleep(10);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("AI Reader Error: " + ex.Message);
                    }
                }
            });

            aiReaderThread.IsBackground = true;
            aiReaderThread.Start();
        }

        public double[] GetBufferedData(int samplesPerChannel)
        {
            lock (channelBuffers)
            {
                int minCount = int.MaxValue;
                foreach (var buf in channelBuffers)
                    minCount = Math.Min(minCount, buf.Count);

                int count = Math.Min(samplesPerChannel, minCount);
                int start = minCount - count;
                List<double> flat = new List<double>();
                for (int i = start; i < minCount; i++)
                {
                    for (int ch = 0; ch < channelBuffers.Length; ch++)
                    {
                        flat.Add(channelBuffers[ch][i]);
                    }
                }
                return flat.ToArray();
            }
        }

        public void InitializeAnalogOutput(string outputChannel)
        {
            analogOutputTask = new NationalInstruments.DAQmx.Task();
            analogOutputTask.AOChannels.CreateVoltageChannel(
                DeviceName + "/" + outputChannel,
                "",
                -10.0,
                10.0,
                AOVoltageUnits.Volts);
            analogWriter = new AnalogSingleChannelWriter(analogOutputTask.Stream);
        }

        public void WriteAnalogOutput(double voltage)
        {
            if (analogWriter == null)
                throw new InvalidOperationException("Analog output task not initialized.");
            analogWriter.WriteSingleSample(true, voltage);
        }

        public void Dispose()
        {
            aiRunning = false;
            aiReaderThread?.Join();
            analogInputTask?.Dispose();
            analogOutputTask?.Dispose();
        }
    }
}
