#include <Windows.h>

#include <atomic>
#include <cstdint>
#include <cstring>
#include <future>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

extern "C" HANDLE createAndConnectPipe(const char* pipeName, DWORD bufferSize);
extern "C" bool CheckForRequest(HANDLE hPipe);
extern "C" int handleClientRequests(
    HANDLE hPipe,
    short* dataA,
    short* dataB,
    double* corrMatrix,
    int segmentIndex,
    DWORD bytesToSend);

namespace
{
    struct PipePair
    {
        HANDLE server = INVALID_HANDLE_VALUE;
        HANDLE client = INVALID_HANDLE_VALUE;
    };

    std::string MakePipeName()
    {
        static std::atomic<unsigned long> counter = 0;
        std::ostringstream builder;
        builder << R"(\\.\pipe\GageStreamThruGPU.Tests.)"
            << GetCurrentProcessId() << "."
            << GetTickCount64() << "."
            << counter.fetch_add(1);
        return builder.str();
    }

    PipePair CreateConnectedPipePair()
    {
        const std::string pipeName = MakePipeName();
        std::promise<HANDLE> serverPromise;
        auto serverFuture = serverPromise.get_future();

        std::thread serverThread([&serverPromise, pipeName]() mutable {
            serverPromise.set_value(createAndConnectPipe(pipeName.c_str(), 64 * 1024));
            });

        HANDLE client = INVALID_HANDLE_VALUE;
        const ULONGLONG deadline = GetTickCount64() + 5000;
        while (GetTickCount64() < deadline) {
            client = CreateFileA(
                pipeName.c_str(),
                GENERIC_READ | GENERIC_WRITE,
                0,
                nullptr,
                OPEN_EXISTING,
                0,
                nullptr);

            if (client != INVALID_HANDLE_VALUE) {
                break;
            }

            const DWORD err = GetLastError();
            if (err != ERROR_FILE_NOT_FOUND && err != ERROR_PIPE_BUSY) {
                break;
            }

            Sleep(20);
        }

        if (client == INVALID_HANDLE_VALUE) {
            serverThread.join();
            throw std::runtime_error("CreateFileA failed for client pipe handle");
        }

        HANDLE server = serverFuture.get();
        serverThread.join();
        if (server == nullptr || server == INVALID_HANDLE_VALUE) {
            CloseHandle(client);
            throw std::runtime_error("createAndConnectPipe returned invalid handle");
        }

        return { server, client };
    }

    void ClosePipePair(PipePair& pair)
    {
        if (pair.client != INVALID_HANDLE_VALUE) {
            CloseHandle(pair.client);
            pair.client = INVALID_HANDLE_VALUE;
        }
        if (pair.server != INVALID_HANDLE_VALUE) {
            CloseHandle(pair.server);
            pair.server = INVALID_HANDLE_VALUE;
        }
    }

    bool WriteShort(HANDLE handle, short value)
    {
        DWORD written = 0;
        return WriteFile(handle, &value, sizeof(value), &written, nullptr) && written == sizeof(value);
    }

    bool ReadExact(HANDLE handle, void* buffer, DWORD totalBytes)
    {
        auto* out = static_cast<std::uint8_t*>(buffer);
        DWORD receivedTotal = 0;
        while (receivedTotal < totalBytes) {
            DWORD received = 0;
            if (!ReadFile(handle, out + receivedTotal, totalBytes - receivedTotal, &received, nullptr) || received == 0) {
                return false;
            }
            receivedTotal += received;
        }
        return true;
    }

    struct TestFailure : std::runtime_error
    {
        using std::runtime_error::runtime_error;
    };

    void Require(bool condition, const std::string& message)
    {
        if (!condition) {
            throw TestFailure(message);
        }
    }

    void TestCreateAndConnectPipe()
    {
        PipePair pair = CreateConnectedPipePair();
        Require(pair.server != INVALID_HANDLE_VALUE, "server pipe handle should be valid");
        Require(pair.client != INVALID_HANDLE_VALUE, "client pipe handle should be valid");
        ClosePipePair(pair);
    }

    void TestHandleClientRequestsReturnsZeroWhenNoRequest()
    {
        PipePair pair = CreateConnectedPipePair();
        short dummyA[4] = { 0 };
        short dummyB[4] = { 0 };
        double matrix[64] = { 0.0 };

        int rc = handleClientRequests(pair.server, dummyA, dummyB, matrix, 0, sizeof(dummyA));
        Require(rc == 0, "handleClientRequests should return 0 when no request is pending");

        ClosePipePair(pair);
    }

    void TestStartAndStopRequests()
    {
        {
            PipePair pair = CreateConnectedPipePair();
            short dummyA[4] = { 0 };
            short dummyB[4] = { 0 };
            double matrix[64] = { 0.0 };

            Require(WriteShort(pair.client, 1), "failed to send start request");
            Require(CheckForRequest(pair.server), "CheckForRequest should detect start request");
            int rc = handleClientRequests(pair.server, dummyA, dummyB, matrix, 0, sizeof(dummyA));
            Require(rc == 2, "start request should return 2");
            ClosePipePair(pair);
        }

        {
            PipePair pair = CreateConnectedPipePair();
            short dummyA[4] = { 0 };
            short dummyB[4] = { 0 };
            double matrix[64] = { 0.0 };

            Require(WriteShort(pair.client, 3), "failed to send stop request");
            Require(CheckForRequest(pair.server), "CheckForRequest should detect stop request");
            int rc = handleClientRequests(pair.server, dummyA, dummyB, matrix, 0, sizeof(dummyA));
            Require(rc == 4, "stop request should return 4");
            ClosePipePair(pair);
        }
    }

    void TestInvalidRequestReturnsError()
    {
        PipePair pair = CreateConnectedPipePair();
        short dummyA[4] = { 0 };
        short dummyB[4] = { 0 };
        double matrix[64] = { 0.0 };

        Require(WriteShort(pair.client, 99), "failed to send invalid request");
        int rc = handleClientRequests(pair.server, dummyA, dummyB, matrix, 0, sizeof(dummyA));
        Require(rc == 1, "invalid request should return 1");

        ClosePipePair(pair);
    }

    void TestDataRequestSendsInterleavedSamplesAndMatrix()
    {
        PipePair pair = CreateConnectedPipePair();

        short dataA[8] = { 10, 11, 12, 13, 20, 21, 22, 23 };
        short dataB[8] = { 30, 31, 32, 33, 40, 41, 42, 43 };
        double matrix[64] = {};
        for (int i = 0; i < 64; ++i) {
            matrix[i] = 1000.0 + static_cast<double>(i);
        }

        Require(WriteShort(pair.client, 2), "failed to send data request");
        auto sendFuture = std::async(
            std::launch::async,
            [&]() {
                return handleClientRequests(pair.server, dataA, dataB, matrix, 1, static_cast<DWORD>(4 * sizeof(short)));
            });

        short received[8] = {};
        Require(ReadExact(pair.client, received, sizeof(received)), "failed to read interleaved samples");

        const short expected[8] = { 20, 40, 21, 41, 22, 42, 23, 43 };
        for (int i = 0; i < 8; ++i) {
            if (received[i] != expected[i]) {
                std::ostringstream message;
                message << "interleaved sample mismatch at index " << i
                    << ": expected " << expected[i]
                    << ", got " << received[i];
                throw TestFailure(message.str());
            }
        }

        double receivedMatrix[64] = {};
        Require(ReadExact(pair.client, receivedMatrix, sizeof(receivedMatrix)), "failed to read correlation matrix");
        for (int i = 0; i < 64; ++i) {
            if (receivedMatrix[i] != matrix[i]) {
                std::ostringstream message;
                message << "matrix mismatch at index " << i;
                throw TestFailure(message.str());
            }
        }

        int rc = sendFuture.get();
        Require(rc == 3, "data request should return 3 after successful send");

        ClosePipePair(pair);
    }

    void TestDataRequestRejectsInvalidByteCount()
    {
        PipePair pair = CreateConnectedPipePair();
        short dataA[4] = { 1, 2, 3, 4 };
        short dataB[4] = { 5, 6, 7, 8 };
        double matrix[64] = { 0.0 };

        Require(WriteShort(pair.client, 2), "failed to send data request");
        int rc = handleClientRequests(pair.server, dataA, dataB, matrix, 0, 3);
        Require(rc == 1, "invalid bytesToSend should return 1");

        ClosePipePair(pair);
    }
}

int main(int argc, char** argv)
{
    const struct
    {
        const char* name;
        void(*run)();
    } tests[] = {
        { "CreateAndConnectPipe", &TestCreateAndConnectPipe },
        { "HandleClientRequestsReturnsZeroWhenNoRequest", &TestHandleClientRequestsReturnsZeroWhenNoRequest },
        { "StartAndStopRequests", &TestStartAndStopRequests },
        { "InvalidRequestReturnsError", &TestInvalidRequestReturnsError },
        { "DataRequestSendsInterleavedSamplesAndMatrix", &TestDataRequestSendsInterleavedSamplesAndMatrix },
        { "DataRequestRejectsInvalidByteCount", &TestDataRequestRejectsInvalidByteCount },
    };

    int failures = 0;
    const char* requestedTest = argc > 1 ? argv[1] : nullptr;

    for (const auto& test : tests) {
        if (requestedTest != nullptr && std::strcmp(requestedTest, test.name) != 0) {
            continue;
        }

        try {
            std::cout << "[RUN ] " << test.name << std::endl;
            test.run();
            std::cout << "[PASS] " << test.name << std::endl;
        }
        catch (const std::exception& ex) {
            ++failures;
            std::cerr << "[FAIL] " << test.name << ": " << ex.what() << std::endl;
        }
    }

    if (failures != 0) {
        std::cerr << failures << " test(s) failed." << std::endl;
        return 1;
    }

    std::cout << "All GageStreamThruGPU pipe tests passed." << std::endl;
    return 0;
}
