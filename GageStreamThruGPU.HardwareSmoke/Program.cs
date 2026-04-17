using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

const string pipeName = "DataPipe";
const int dataPointsPerChannel = 100;
const int interleavedBytes = dataPointsPerChannel * 2 * sizeof(short);
const int matrixBytes = 64 * sizeof(double);
const string defaultExePath = @"C:\Quantum Squeezing\Quantum-Measurement-Software\GageStreamThruGPU\x64\Debug\GageStreamThruGPU.exe";
const string defaultWorkingDirectory = @"C:\Quantum Squeezing\Quantum-Measurement-Software\Quantum Measurement UI\bin\Debug\net8.0-windows7.0";
const string defaultResultsBaseDirectory = @"D:\Quantum Squeezing Project\DataFiles";

string exePath = args.Length > 0
    ? args[0]
    : defaultExePath;

string experimentToken = args.Length > 1
    ? args[1]
    : DateTime.Now.ToString("yyMMdd_HHmmss").PadRight(15, '_')[..15];

string workingDirectory = args.Length > 2
    ? args[2]
    : defaultWorkingDirectory;

if (experimentToken.Length != 15)
{
    experimentToken = experimentToken.Length > 15
        ? experimentToken[..15]
        : experimentToken.PadRight(15, '_');
}

if (!File.Exists(exePath))
{
    Console.Error.WriteLine($"Executable not found: {exePath}");
    return 1;
}

if (!Directory.Exists(workingDirectory))
{
    Console.Error.WriteLine($"Working directory not found: {workingDirectory}");
    return 1;
}

string iniPath = Path.Combine(workingDirectory, "StreamThruGPU.ini");
if (!File.Exists(iniPath))
{
    Console.Error.WriteLine($"StreamThruGPU.ini not found in working directory: {iniPath}");
    return 1;
}

string experimentDirectory = Path.Combine(defaultResultsBaseDirectory, experimentToken);
Directory.CreateDirectory(experimentDirectory);

Process? process = null;

try
{
    Console.WriteLine($"Starting server: {exePath}");
    Console.WriteLine($"Using working directory: {workingDirectory}");
    Console.WriteLine($"Using ini file: {iniPath}");
    Console.WriteLine($"Ensured experiment directory exists: {experimentDirectory}");
    process = Process.Start(new ProcessStartInfo
    {
        FileName = exePath,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false
    });

    if (process is null)
    {
        Console.Error.WriteLine("Failed to start GageStreamThruGPU.exe");
        return 1;
    }

    await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    Console.WriteLine("Connecting to DataPipe...");
    await client.ConnectAsync(10000);

    Console.WriteLine($"Sending start handshake with experiment token '{experimentToken}'");
    await WriteShortAsync(client, 1);
    byte[] dirBytes = Encoding.ASCII.GetBytes(experimentToken);
    await client.WriteAsync(dirBytes);
    await client.FlushAsync();

    await Task.Delay(3000);

    Console.WriteLine("Requesting one real hardware frame...");
    await WriteShortAsync(client, 2);

    byte[] interleaved = new byte[interleavedBytes];
    byte[] matrix = new byte[matrixBytes];

    await ReadExactAsync(client, interleaved, interleavedBytes);
    await ReadExactAsync(client, matrix, matrixBytes);

    short[] samples = new short[interleavedBytes / sizeof(short)];
    double[] corr = new double[64];
    Buffer.BlockCopy(interleaved, 0, samples, 0, interleavedBytes);
    Buffer.BlockCopy(matrix, 0, corr, 0, matrixBytes);

    Console.WriteLine("Received data successfully.");
    Console.WriteLine($"First 10 interleaved samples: {string.Join(", ", samples.Take(10))}");
    Console.WriteLine($"First 8 matrix values: {string.Join(", ", corr.Take(8).Select(v => v.ToString("F4")))}");

    Console.WriteLine("Sending stop request...");
    await WriteShortAsync(client, 3);
    await client.FlushAsync();

    if (!process.WaitForExit(10000))
    {
        Console.WriteLine("Server did not exit in time; killing process.");
        process.Kill(entireProcessTree: true);
    }

    Console.WriteLine("Hardware smoke run complete.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Hardware smoke failed: {ex.Message}");
    if (process is { HasExited: false })
    {
        process.Kill(entireProcessTree: true);
    }
    return 1;
}

static async Task WriteShortAsync(Stream stream, short value)
{
    byte[] bytes = BitConverter.GetBytes(value);
    await stream.WriteAsync(bytes);
}

static async Task ReadExactAsync(Stream stream, byte[] buffer, int total)
{
    int offset = 0;
    while (offset < total)
    {
        int read = await stream.ReadAsync(buffer.AsMemory(offset, total - offset));
        if (read == 0)
        {
            throw new IOException($"Pipe closed after {offset} of {total} bytes");
        }

        offset += read;
    }
}
