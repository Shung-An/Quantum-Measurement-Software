/*
 * File: NamedPipeServer.cpp
 * Description: This file contains functions to create and manage a named pipe server in Windows.
 *              The server waits for a client to connect, handles incoming requests, and sends
 *              data segments or other responses based on the client's request.
 *
 * Functions:
 * - createAndConnectPipe: Creates a named pipe and waits for a client to connect.
 * - CheckForRequest: Checks if data is available from the client without blocking.
 * - handleClientRequests: Handles requests from the client, including starting the experiment,
 *                         sending data, and stopping the data acquisition.
 *
 * Usage:
 * - Compile this file as part of your application that interacts with named pipes.
 *
 * Author: Frank Ran
 * Created: 2024-11-13
 
 
 * Notes:
 * - Ensure the client application connects to the same named pipe for proper communication.
 * - Customize the buffer size and request handling logic as needed.
 */

#include <stdio.h>
#include <Windows.h>
#include <stdlib.h>
#include <iostream>
#include <vector>


/*
 * Function: createAndConnectPipe
 * Description: Creates a named pipe with read/write access and waits for a client to connect.
 * Parameters:
 *   - pipeName: The name of the pipe (const char*) used to identify the pipe.
 *   - bufferSize: The size of the pipe's buffer in bytes (DWORD).
 * Returns:
 *   - HANDLE: A handle to the created pipe. Returns NULL if the pipe creation or connection fails.
 * Notes:
 *   - The function blocks until a client connects to the pipe.
 *   - The created pipe has duplex (read/write) access with byte-based communication.
 */
extern "C" HANDLE createAndConnectPipe(const char* pipeName, DWORD bufferSize) {
    HANDLE hPipe = CreateNamedPipe(
        pipeName,                 // Pipe name passed as argument
        PIPE_ACCESS_DUPLEX,        // Read/Write access
        PIPE_TYPE_BYTE |           // Byte-type pipe
        PIPE_READMODE_BYTE |       // Byte-read mode
        PIPE_WAIT,                 // Blocking mode
        1,                         // Max number of instances
        bufferSize,                // Output buffer size
        bufferSize,                // Input buffer size
        0,                         // Default timeout
        NULL);                     // Default security attributes

    if (hPipe == INVALID_HANDLE_VALUE) {
        DWORD err = GetLastError();
        std::wcerr << L"[Error] Failed to create named pipe (" << pipeName << L")\n"
            << L"Win32 Error Code: " << err << L"\n";
        return NULL;
    }

    std::cout << "Waiting for client connection...\n";
    BOOL connected = ConnectNamedPipe(hPipe, NULL) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);

    if (!connected) {
        std::cerr << "Failed to connect to the client.\n";
        CloseHandle(hPipe);
        return NULL;
    }

    return hPipe;
}

/*
 * Function: CheckForRequest
 * Description: Checks if the client has sent any data to the server without blocking.
 * Parameters:
 *   - hPipe: A handle to the pipe (HANDLE) to check for available data.
 * Returns:
 *   - bool: Returns true if data is available to read; false otherwise.
 * Notes:
 *   - Uses PeekNamedPipe to check for data without removing it from the pipe.
 */
extern "C" bool CheckForRequest(HANDLE hPipe) {
    DWORD bytesAvailable = 0;
    if (PeekNamedPipe(hPipe, NULL, 0, NULL, &bytesAvailable, NULL) && bytesAvailable > 0) {
        return true; // Data is available to read
    }
    return false; // No data available
}

/*
 * Function: handleClientRequests
 * Description: Handles requests from the client connected to the named pipe. Depending on the request,
 *              the function may start or stop the experiment, or send data back to the client.
 * Parameters:
 *   - hPipe: A handle to the pipe (HANDLE) for communication.
 *   - data: Pointer to an array of short integers (short*) containing the raw signals data to send.
 *   - corrMatrix: Pointer to an array of doubles (double*) representing the correlation matrix to send.
 *   - segmentIndex: The index (int) of the data segment to send.
 *   - bytesToSend: The number of bytes to send (DWORD) from the `data` array, which means the number pf signal points sent.
 * Returns:
 *   - int: Status code indicating the result:
 *          - 0: No data to process.
 *          - 1: Error occurred (e.g., reading or writing data).
 *          - 2: Experiment start requested.
 *          - 3: Data sent successfully.
 *          - 4: Experiment stop requested.
 * Notes:
 *   - This function calls CheckForRequest to verify if the client has sent data.
 *   - The function uses `ReadFile` and `WriteFile` to read from and write to the pipe.
 */
extern "C" int handleClientRequests(HANDLE hPipe, short* data, short* dataB, double* corrMatrix, int segmentIndex, DWORD bytesToSend) {
    if (!CheckForRequest(hPipe)) {
        return 0;  // No data to process
    }

    // Read the client's request
    short request;
    DWORD bytesRead;
    BOOL success = ReadFile(hPipe, &request, sizeof(request), &bytesRead, NULL);
    if (!success || bytesRead != sizeof(request)) {
        std::cerr << "Failed to read request from client.\n";
        return 1;  // Error reading the request
    }

	if (request == 1) { // The client requested to start the experiment
        return 2; // The client requested to start the experiment
    }
	else if (request == 2) { // The client requested to send data including raw signals and correlation matrix
        DWORD bytesWritten1;
        DWORD bytesWritten2;

        // --- Calculate the starting position for this segment ---
        int samplesPerSegment = bytesToSend / sizeof(short);
        short* segmentStartA = data + segmentIndex * samplesPerSegment;
        short* segmentStartB = dataB + segmentIndex * samplesPerSegment;

        // --- Interleave A and B (ABABAB...) ---
        std::vector<short> interleaved;
        interleaved.resize(samplesPerSegment * 2);

        for (int i = 0; i < samplesPerSegment; ++i) {
            interleaved[2 * i] = segmentStartA[i];
            interleaved[2 * i + 1] = segmentStartB[i];
        }

        // --- Send the interleaved segment ---
        DWORD interleavedBytes = static_cast<DWORD>(interleaved.size() * sizeof(short));
        success = WriteFile(hPipe, interleaved.data(), interleavedBytes, &bytesWritten1, NULL);
        if (!success || bytesWritten1 != interleavedBytes) {
            std::cerr << "Failed to send interleaved data segment to client.\n";
            return 1;
		}  // Error sending the data segment

        // Send the correlation matrix to the client
        success = WriteFile(hPipe, corrMatrix, 512, &bytesWritten2, NULL);
        if (!success || bytesWritten2 != 512) {
            std::cerr << "Failed to send correlation matrix to client.\n";
            return 1;  // Error sending the correlation matrix
        }

        return 3;
    }
	else if (request == 3) { // The client requested to abort the experiment
        return 4; // The client requested to stop experiement
    }
    else {
        return 1;  // Invalid request
    }
}
