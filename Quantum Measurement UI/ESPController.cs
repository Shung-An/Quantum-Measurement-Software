using System;
using System.Globalization;
using System.Threading;
using NationalInstruments.Visa;
using Ivi.Visa;

namespace Quantum_measurement_UI
{
    /// <summary>
    /// Class for controlling ESP300 motion controller with customizable motion profiles
    /// </summary>
    class ESP300Controller
    {
        // Configuration parameters
        public int Axis { get; set; } = 1;
        public double Velocity { get; set; } = 10.0;
        public double Acceleration { get; set; } = 5.0;
        public double Deceleration { get; set; } = 5.0;

        public double currentPosition { get; set; } = 0.0;
        // Connection state
        public bool IsConnected { get; private set; }
        public bool IsBypassed { get; private set; }


        // VISA communication objects
        private ResourceManager _resourceManager;
        private IVisaSession _visaSession;
        private IMessageBasedSession _session;
        private IMessageBasedFormattedIO formattedIO;
        private readonly object _ioLock = new object();
        private const int PositionReadRetryCount = 3;

        /// <summary>
        /// Connects to the ESP300 controller
        /// </summary>
        /// <param name="visaAddress">VISA address of the controller (default: GPIB0::1::INSTR)</param>
        /// <returns>True if connection was successful</returns>
        public bool Connect(string visaAddress = "GPIB0::1::INSTR", bool bypassUsbConnection = false)
        {
            if (bypassUsbConnection)
            {
                IsBypassed = true;
                IsConnected = true;
                Console.WriteLine("ESP300 USB/GPIB connection bypassed.");
                return true;
            }

            try
            {
                _resourceManager = new ResourceManager();
                _visaSession = _resourceManager.Open(visaAddress);
                _session = (IMessageBasedSession)_visaSession;

                // --- Important settings for ESP300 ---
                _session.Clear();                        // flush any junk
                _session.TimeoutMilliseconds = 5000;     // allow long replies
                _session.TerminationCharacterEnabled = true;
                _session.TerminationCharacter = 0x0D;    // carriage return '\r'

                formattedIO = _session.FormattedIO;
                IsConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ESP300 GPIB connect failed: {ex.Message}");
                IsConnected = false;
                return false;
            }
        }






        /// <summary>
        /// Executes a previously created cycle motion program
        /// </summary>
        /// <param name="program">The program name to execute (default: 1)</param>
        public void ExecuteProgram(string program)
        {
            SendCommand($"EX {program}");
        }

        /// <summary>
        /// Aborts the currently running program
        /// </summary>
        public void AbortProgram()
        {
            SendCommand("AP");
        }

        /// <summary>
        /// Gets the current position of the axis
        /// </summary>
        /// <returns>Current position</returns>
        public String GetDelayStageInfo()
        {
            string axisPrefix = Axis.ToString();
            string response = Query($"{axisPrefix}ID?");
            return ($"model and serial number: {response}");
        }

        public string ReadResponse()
        {
            lock (_ioLock)
            {
                if (formattedIO != null)
                {
                    return formattedIO.ReadLine();
                }

                throw new Exception("formattedIO session is not initialized.");
            }
        }


        // get the current position of the axis
        public double GetCurrentPosition()
        {
            string axisPrefix = Axis.ToString();
            string command = $"{axisPrefix}TP?";

            for (int attempt = 1; attempt <= PositionReadRetryCount; attempt++)
            {
                try
                {
                    string response = Query(command)?.Trim();
                    if (double.TryParse(response, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double position))
                    {
                        currentPosition = position;
                        return position;
                    }
                }
                catch (Ivi.Visa.IOTimeoutException)
                {
                    ResetIoStateAfterReadFailure();
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch
                {
                    ResetIoStateAfterReadFailure();
                }

                Thread.Sleep(50 * attempt);
            }

            return currentPosition;
        }

        public int getMotionStatus()
        {
            string axisPrefix = Axis.ToString();
            string response = Query($"{axisPrefix}MD?")?.Trim();
            
            if (int.TryParse(response, out int status))
            {
                return status;
            }

            return -1; // Return -1 if parsing failed
        }



        /// <summary>
        /// Reset the system-
        /// </summary>
        public void Reset()
        {
            if (IsBypassed)
            {
                return;
            }

            // Send the reset command to the controller
            SendCommand("RS");
            // Wait for the reset to complete
            Thread.Sleep(20000);
        }


        /// <summary>
        /// Clears all errors from the ESP300 by draining the error queue (ER?) until "0"
        /// and then issuing CL to clear status registers. Returns a multi-line report.
        /// </summary>
        public string ClearAllErrors()
        {
            lock (_ioLock)
            {
                if (IsBypassed)
                    return "No delay stage errors detected (USB bypass enabled).";

                if (_session == null || formattedIO == null)
                    return "ESP300 not connected.";

                var sb = new System.Text.StringBuilder();

                try
                {
                    // Drain error queue
                    for (int i = 0; i < 64; i++) // ESP300 error queue depth is limited
                    {
                        formattedIO.WriteLine("ER?");
                        var resp = formattedIO.ReadLine()?.Trim();

                        if (string.IsNullOrWhiteSpace(resp))
                            break;

                        sb.AppendLine(resp);

                        if (resp.StartsWith("0")) // "0, ..." => no more errors
                            break;
                    }

                    // Clear status registers
                    formattedIO.WriteLine("CL");
                }
                catch (Exception ex)
                {
                    return $"ClearAllErrors failed: {ex.Message}";
                }

                return sb.Length > 0 ? sb.ToString().TrimEnd() : "No errors in queue.";
            }
        }


        /// <summary>
        /// Sends a command (no reply expected)
        /// </summary>
        public void SendCommand(string command)
        {
            lock (_ioLock)
            {
                if (IsBypassed)
                {
                    ApplySimulatedCommand(command);
                    return;
                }

                if (formattedIO == null)
                    throw new InvalidOperationException("VISA session not initialized");

                formattedIO.WriteLine(command);
            }
        }


        /// <summary>
        /// Sends a query (expects reply)
        /// </summary>
        public string Query(string command)
        {
            lock (_ioLock)
            {
                if (IsBypassed)
                {
                    return GetSimulatedQueryResponse(command);
                }

                if (formattedIO == null)
                    throw new InvalidOperationException("VISA session not initialized");

                formattedIO.WriteLine(command);
                return formattedIO.ReadLine();
            }
        }
        /// <summary>
        /// Checks for any errors using TB?. If errors exist, drains ER? until no errors remain,
        /// clears status registers (CL), and returns a multi-line report of everything found.
        /// If no errors, returns "No delay stage errors detected".
        /// </summary>
        public string CheckForErrors()
        {
            lock (_ioLock)
            {
                if (IsBypassed)
                    return "No delay stage errors detected (USB bypass enabled).";

                if (_session == null || formattedIO == null)
                    return "ESP300 not connected.";

                try
                {
                    formattedIO.WriteLine("TB?");
                    string tb = formattedIO.ReadLine()?.Trim();

                    if (string.IsNullOrWhiteSpace(tb))
                        return "TB? returned empty response";

                    if (tb.StartsWith("0,"))
                        return "No delay stage errors detected";

                    // drain ER? queue once
                    formattedIO.WriteLine("ER?");
                    string er = formattedIO.ReadLine()?.Trim();

                    return string.IsNullOrWhiteSpace(er) ? tb : $"{tb}\n{er}";
                }
                catch (Ivi.Visa.IOTimeoutException)
                {
                    return "Timeout while checking ESP300 errors.";
                }
                catch (Exception ex)
                {
                    return $"CheckForErrors failed: {ex.Message}";
                }
            }
        }



        public void setPositionDisplayResolution(double resolution)
        {
            // Set the display resolution for the axis
            string axisPrefix = Axis.ToString();
            SendCommand($"{axisPrefix}FP{resolution}");
        }

        /// <summary>
        /// Safely disconnects from the ESP300 controller.
        /// Optionally attempts to abort any running program and stop motion before closing the session.
        /// </summary>
        /// <param name="abortProgram">Send AB to abort any running program before disconnecting.</param>
        /// <param name="stopMotion">Send ST to stop motion before disconnecting.</param>
        public void Disconnect(bool abortProgram = true, bool stopMotion = true)
        {
            lock (_ioLock)
            {
                if (IsBypassed)
                {
                    IsConnected = false;
                    IsBypassed = false;
                    return;
                }

                // Best-effort commands; swallow errors if the link is already gone.
                try
                {
                    if (abortProgram && IsConnected) formattedIO?.WriteLine("AB"); // Abort program (ESP300)
                }
                catch { /* ignore */ }

                try
                {
                    if (stopMotion && IsConnected) formattedIO?.WriteLine("ST"); // Stop motion
                }
                catch { /* ignore */ }

                // Try to clear I/O buffers (non-fatal if it fails)
                try { _session?.Clear(); } catch { /* ignore */ }

                // Dispose VISA objects in reverse order of creation
                try { formattedIO = null; } catch { /* ignore */ }
                try { _session?.Dispose(); } catch { /* ignore */ } finally { _session = null; }
                try { _visaSession?.Dispose(); } catch { /* ignore */ } finally { _visaSession = null; }
                try { _resourceManager?.Dispose(); } catch { /* ignore */ } finally { _resourceManager = null; }

                IsConnected = false;
            }
        }

        private void ApplySimulatedCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            string trimmed = command.Trim();
            string axisPrefix = Axis.ToString(CultureInfo.InvariantCulture);

            if (trimmed.StartsWith($"{axisPrefix}PA", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(trimmed.Substring($"{axisPrefix}PA".Length), NumberStyles.Float, CultureInfo.InvariantCulture, out double absolutePosition))
            {
                currentPosition = absolutePosition;
            }
            else if (trimmed.StartsWith($"{axisPrefix}PR", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(trimmed.Substring($"{axisPrefix}PR".Length), NumberStyles.Float, CultureInfo.InvariantCulture, out double relativePosition))
            {
                currentPosition += relativePosition;
            }
        }

        private string GetSimulatedQueryResponse(string command)
        {
            string trimmed = command?.Trim() ?? string.Empty;
            string axisPrefix = Axis.ToString(CultureInfo.InvariantCulture);

            if (trimmed.Equals($"{axisPrefix}TP?", StringComparison.OrdinalIgnoreCase))
            {
                return currentPosition.ToString("G17", CultureInfo.InvariantCulture);
            }

            if (trimmed.Equals($"{axisPrefix}MD?", StringComparison.OrdinalIgnoreCase))
            {
                return "1";
            }

            if (trimmed.Equals($"{axisPrefix}ID?", StringComparison.OrdinalIgnoreCase))
            {
                return "ESP300 USB BYPASS";
            }

            if (trimmed.Equals($"{axisPrefix}VA?", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals($"{axisPrefix}VU?", StringComparison.OrdinalIgnoreCase))
            {
                return Velocity.ToString("G17", CultureInfo.InvariantCulture);
            }

            if (trimmed.Equals($"{axisPrefix}AC?", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals($"{axisPrefix}AU?", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals($"{axisPrefix}AG?", StringComparison.OrdinalIgnoreCase))
            {
                return Acceleration.ToString("G17", CultureInfo.InvariantCulture);
            }

            if (trimmed.Equals("TB?", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("ER?", StringComparison.OrdinalIgnoreCase))
            {
                return "0";
            }

            return "0";
        }

        private void ResetIoStateAfterReadFailure()
        {
            lock (_ioLock)
            {
                try
                {
                    _session?.Clear();
                }
                catch
                {
                    // Best effort only: recovering from intermittent controller read glitches.
                }
            }
        }




        /// <summary>
        /// Executes commands from a text file - simple version
        /// </summary>
        /// <param name="filePath">Path to the text file containing commands</param>
        /// <returns>True if execution was successful</returns>
        public bool ExecuteCommandsFromFile(string filePath)
        {
            try
            {
                Console.WriteLine($"Executing commands from file: {filePath}");

                // Read all lines from the file
                string[] lines = System.IO.File.ReadAllLines(filePath);

                // Process each line
                foreach (string line in lines)
                {
                    // Skip empty lines
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // Trim whitespace
                    string command = line.Trim();

                    // Send the command
                    Console.WriteLine($"Sending command: {command}");
                    SendCommand(command);

                    // Small delay between commands
                    Thread.Sleep(100);
                }

                Console.WriteLine("Command file execution completed");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return false;
            }
        }
    }

    class TestMotionController
    {
        static void Test(string[] args)
        {
            // Create controller instance with user-specified parameters
            ESP300Controller controller = new ESP300Controller
            {
                Axis = 1                  // Axis number
            };


            // Connect to controller
            if (controller.Connect())
            {
                try
                {
                    //controller.ExecuteCommandsFromFile("C:\\Users\\jr151\\source\\repos\\motion.txt");
                    controller.setPositionDisplayResolution(5);
                    controller.AbortProgram();
                    controller.GetDelayStageInfo();
                    controller.ExecuteProgram("Motion");

                    controller.CheckForErrors();
                    controller.getMotionStatus();

                    for (int i = 0; i < 800; i++)
                    {
                        double currentPosition = controller.GetCurrentPosition();
                        Console.WriteLine($"Move to position: {currentPosition}");
                        Thread.Sleep(1000); // wait for 1 second
                        controller.CheckForErrors();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }

            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
