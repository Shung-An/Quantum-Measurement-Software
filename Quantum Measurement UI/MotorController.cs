using System;
using NewFocus.Picomotor;

namespace Quantum_measurement_UI
{
    public class MotorController
    {
        private CmdLib8742 cmdLib; // Library for communicating with the motor controller
        public string deviceKey;  // Unique key identifying the motor controller device

        public MotorController()
        {
            InitializeDevice();
        }

        // Initialize the motor controller device
        private void InitializeDevice()
        {
            Console.WriteLine("Waiting for device discovery...");
            deviceKey = string.Empty;
            cmdLib = new CmdLib8742(false, 5000, ref deviceKey);

            if (string.IsNullOrEmpty(deviceKey))
            {
                Console.WriteLine("No devices discovered.");
                throw new Exception("No devices discovered.");
            }

            Console.WriteLine($"First Device Key = {deviceKey}");
        }

        // Set the current position of the specified motor to zero
        public bool SetZeroPosition(int motorNumber)
        {
            bool status = cmdLib.SetZeroPosition(deviceKey, motorNumber);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not set the current position.");
            }
            return status;
        }

        // Get the current position of the specified motor
        public bool GetCurrentPosition(int motorNumber, out int position)
        {
            position = 0;
            bool status = cmdLib.GetPosition(deviceKey, motorNumber, ref position);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not get the current position.");
            }
            return status;
        }

        public bool StopMotion(string deviceKey, int motorNumber)
        {

            bool status = cmdLib.AbortMotion(deviceKey);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not stop the motor.");
            }
            return status;
        }

        // Move the specified motor by a relative number of steps
        public bool MoveRelative(int motorNumber, int relativeSteps)
        {
            bool status = cmdLib.RelativeMove(deviceKey, motorNumber, relativeSteps);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not perform relative move.");
            }
            return status;
        }

        // Move the specified motor by a relative number of steps
        public bool MovePlus10(int motorNumber)
        {
            bool status = cmdLib.RelativeMove(deviceKey, motorNumber, 10);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not perform relative move.");
            }
            return status;
        }
        // Move the specified motor by a relative number of steps
        public bool MoveMinus10(int motorNumber)
        {
            bool status = cmdLib.RelativeMove(deviceKey, motorNumber, -10);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not perform relative move.");
            }
            return status;
        }
        // Move the specified motor by a relative number of steps
        public bool MovePlus1(int motorNumber)
        {
            bool status = cmdLib.RelativeMove(deviceKey, motorNumber, 1);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not perform relative move.");
            }
            return status;
        }
        // Move the specified motor by a relative number of steps
        public bool MoveMinus1(int motorNumber)
        {
            bool status = cmdLib.RelativeMove(deviceKey, motorNumber, -1);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not perform relative move.");
            }
            return status;
        }

        // Move the specified motor to an absolute target position
        public bool MoveToPosition(int motorNumber, int targetPosition)
        {
            bool status = cmdLib.AbsoluteMove(deviceKey, motorNumber, targetPosition);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not perform absolute move.");
            }
            return status;
        }

        // Check if the motion of the specified motor is complete
        public bool IsMotionDone(int motorNumber, out bool isMotionDone)
        {
            isMotionDone = false;
            bool status = cmdLib.GetMotionDone(deviceKey, motorNumber, ref isMotionDone);
            if (!status)
            {
                Console.WriteLine("I/O Error: Could not get motion done status.");
            }
            return status;
        }

        // Shutdown the motor controller and release resources
        public void Shutdown()
        {
            Console.WriteLine("Shutting down motor controller.");
            cmdLib.Shutdown();
        }

        // Check the motor controller for any error messages
        // Check the motor controller for any error messages
        public void CheckForErrors()
        {
            string errorMsg = string.Empty;
            bool status = cmdLib.GetErrorMsg(deviceKey, ref errorMsg);

            if (!status)
            {
                Console.WriteLine("I/O Error: Could not get error status.");
            }
            else if (!string.IsNullOrEmpty(errorMsg))
            {
                var parts = errorMsg.Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && parts[0] != "0")
                {
                    string errorCode = parts[0];

                    // Special case: bypass "MOTION IN PROGRESS"
                    if (errorCode == "314" || errorCode == "214" || errorCode == "114")
                    {
                        // Derive motor to stop from the error code (e.g. 114 → motor 1, 214 → motor 2, 314 → motor 3)
                        int motorToStop=4; // default fallback

                        if (errorCode.Length > 0 && char.IsDigit(errorCode[0]))
                        {
                            motorToStop = errorCode[0] - '0';  // first digit as motor index
                        }

                        Console.WriteLine($"Non-fatal: {errorMsg} (stopping motor {motorToStop} and skipping exception)");
                        StopMotion(deviceKey, motorToStop);

                        return; // skip throwing
                    }


                    // Otherwise throw for all other errors
                    Console.WriteLine($"Device Error: {errorMsg}");
                    throw new Exception($"Device Error: {errorMsg}");
                }
            }
        }

    }
}