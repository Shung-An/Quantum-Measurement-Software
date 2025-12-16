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


static inline const char* winerr(DWORD e) {
    switch (e) {
    case ERROR_BROKEN_PIPE: return "ERROR_BROKEN_PIPE";
    case ERROR_NO_DATA:     return "ERROR_NO_DATA";
    default: return "";
    }
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
// Non-blocking peek for a pending short request; returns true if it's 3 (abort).
static bool HasAbortRequest(HANDLE hPipe) {
    DWORD avail = 0;
    if (!PeekNamedPipe(hPipe, nullptr, 0, nullptr, &avail, nullptr)) {
        // Peek may fail briefly on disconnect; treat as no abort
        return false;
    }
    if (avail < sizeof(short)) return false;

    short req = 0;
    DWORD br = 0;
    if (!ReadFile(hPipe, &req, sizeof(req), &br, NULL) || br != sizeof(req)) {
        // Could be peer closed or another transient; not a definitive abort
        return false;
    }
    // We consumed one pending short request; only act on 3 (abort).
    return (req == 3);
}


// Write with chunking + abort responsiveness.
// Returns: 0 ok, 4 abort, 1 error.
static int WriteAllWithAbort(HANDLE hPipe, const void* buf, DWORD totalBytes) {
    const BYTE* p = static_cast<const BYTE*>(buf);
    DWORD remaining = totalBytes;

    // 32 KB chunks => good responsiveness without thrashing
    const DWORD CHUNK = 32 * 1024;

    while (remaining > 0) {
        // Soft abort: client sent a '3'
        if (HasAbortRequest(hPipe)) {
            std::cerr << "[pipe] Abort request received mid-write\n";
            return 4;
        }

        DWORD toWrite = remaining < CHUNK ? remaining : CHUNK;
        DWORD sent = 0;
        if (!WriteFile(hPipe, p, toWrite, &sent, NULL) || sent == 0) {
            DWORD err = GetLastError();
            std::cerr << "[pipe] WriteFile failed (sent=" << sent
                << ", need=" << toWrite << ") err=" << err << " " << winerr(err) << "\n";
            // Hard abort: client closed after sending 3, or just disconnected
            if (err == ERROR_BROKEN_PIPE || err == ERROR_NO_DATA) return 4;
            return 1;
        }
        p += sent;
        remaining -= sent;
    }
    return 0;
}

/*
 * Function: handleClientRequests
 * Description: Handles requests from the client connected to the named pipe. Depending on the request,
 *              the function may start or stop the experiment, or send data back to the client.
 * Parameters:
 *   - hPipe: A handle to the pipe (HANDLE) for communication.
 *   - dataA: Pointer to channel-A samples (short*), contiguous by segments.
 *   - dataB: Pointer to channel-B samples (short*), contiguous by segments.
 *   - corrMatrix: Pointer to an array of doubles (double*) representing the correlation matrix to send.
 *   - segmentIndex: The index (int) of the data segment to send.
 *   - bytesToSend: The number of BYTES per-channel to send for this segment (DWORD).
 * Returns:
 *   - 0: No data to process.
 *   - 1: Error occurred.
 *   - 2: Experiment start requested.
 *   - 3: Data sent successfully.
 *   - 4: Experiment stop requested.
 */
extern "C" int handleClientRequests(
    HANDLE hPipe,
    short* dataA,
    short* dataB,
    double* corrMatrix,
    int     segmentIndex,
    DWORD   bytesToSend
) {
    if (!CheckForRequest(hPipe)) {
        return 0;  // No data to process
    }

    // Read client's request (16-bit short)
    short request = 0;
    DWORD bytesRead = 0;
    if (!ReadFile(hPipe, &request, sizeof(request), &bytesRead, NULL) || bytesRead != sizeof(request)) {
        std::cerr << "Failed to read request from client. err=" << GetLastError() << "\n";
        return 1;
    }

    if (request == 1) {
        return 2; // start experiment
    }
    if (request == 3) {
        return 4; // stop experiment
    }
    if (request != 2) {
        return 1; // invalid request
    }

    // --- request == 2: send interleaved A/B (ABAB...) + 512-byte matrix ---
    if (bytesToSend == 0 || (bytesToSend % sizeof(short)) != 0) {
        std::cerr << "bytesToSend invalid: " << bytesToSend << "\n";
        return 1;
    }

    const int samplesPerSegment = static_cast<int>(bytesToSend / sizeof(short));
    short* segA = dataA + static_cast<size_t>(segmentIndex) * samplesPerSegment;
    short* segB = dataB + static_cast<size_t>(segmentIndex) * samplesPerSegment;

    const int CH_S = 16 * 1024; // samples per channel per chunk
    std::vector<short> scratch; scratch.resize(static_cast<size_t>(CH_S) * 2);

    int remainingSamples = samplesPerSegment;
    short* pA = segA;
    short* pB = segB;

    while (remainingSamples > 0) {
        if (HasAbortRequest(hPipe)) return 4;

        int thisS = (remainingSamples < CH_S) ? remainingSamples : CH_S;

        for (int i = 0; i < thisS; ++i) {
            scratch[2 * i] = pA[2 * i];
            scratch[2 * i + 1] = pB[2 * i + 1];
        }

        const DWORD bytesThisChunk = static_cast<DWORD>(thisS * 2 * sizeof(short));
        int rc = WriteAllWithAbort(hPipe, scratch.data(), bytesThisChunk);
        if (rc != 0) return rc; // 4=abort, 1=error

        pA += thisS;
        pB += thisS;
        remainingSamples -= thisS;
    }

    // Matrix (512 bytes == 64 doubles)
    {
        const DWORD matrixBytes = 512;
        int rc = WriteAllWithAbort(hPipe, corrMatrix, matrixBytes);
        if (rc != 0) return rc;
    }

    FlushFileBuffers(hPipe);
    return 3;
}