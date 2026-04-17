#include <cuda_runtime.h>
#include <cublas_v2.h>

#include <algorithm>
#include <iomanip>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

extern "C" cudaError_t ComputeCrossCorrelationGPU(
    const __int64 u32LoopCount,
    short* data,
    short* dataB,
    const __int64 size,
    const int totalThreads,
    const int gridSize,
    const int blockSize,
    const int sharedSegmentSize,
    const int demodulationWindowSize,
    const int totalSegNum,
    const int corrMatrixSize,
    const int segmentSize,
    double* h_odata,
    cublasHandle_t handle,
    double* d_aggregatedCorrMatrix,
    double* d_reducedCorrMatrix,
    double* d_scaling_factors,
    FILE* binFile,
    FILE* analysisFile);

extern "C" void initializeArrayWithCuda(double* dev_array, int size, double value);

namespace
{
    void RequireCuda(cudaError_t status, const char* context)
    {
        if (status != cudaSuccess) {
            throw std::runtime_error(std::string(context) + ": " + cudaGetErrorString(status));
        }
    }

    void RequireCublas(cublasStatus_t status, const char* context)
    {
        if (status != CUBLAS_STATUS_SUCCESS) {
            throw std::runtime_error(std::string(context) + " failed with cuBLAS status " + std::to_string(status));
        }
    }

    void PrintMatrixPreview(const std::vector<double>& matrix, int width, int rowsToShow)
    {
        const int shownRows = std::min(width, rowsToShow);
        for (int row = 0; row < shownRows; ++row) {
            for (int col = 0; col < width; ++col) {
                std::cout << std::setw(10) << std::fixed << std::setprecision(2)
                    << matrix[row * width + col] << ' ';
            }
            std::cout << "\n";
        }
    }
}

int main()
{
    try {
        constexpr int demodulationWindowSize = 8;
        constexpr int segmentSize = 2 * demodulationWindowSize;
        constexpr int corrMatrixSize = demodulationWindowSize * demodulationWindowSize;
        constexpr int totalSegNum = 2;
        constexpr int blockSize = corrMatrixSize;
        constexpr int gridSize = totalSegNum;
        constexpr int totalThreads = totalSegNum * corrMatrixSize;
        constexpr int sharedSegmentSize = segmentSize;
        constexpr __int64 size = static_cast<__int64>(totalSegNum * segmentSize);

        std::vector<short> hostBoardA(size);
        std::vector<short> hostBoardB(size);

        for (int seg = 0; seg < totalSegNum; ++seg) {
            const int base = seg * segmentSize;
            for (int i = 0; i < segmentSize; ++i) {
                hostBoardA[base + i] = static_cast<short>(100 * (seg + 1) + i);
                hostBoardB[base + i] = static_cast<short>(200 * (seg + 1) + i);
            }
        }

        short* d_boardA = nullptr;
        short* d_boardB = nullptr;
        double* d_aggregated = nullptr;
        double* d_reduced = nullptr;
        double* d_scaling = nullptr;
        std::vector<double> reduced(corrMatrixSize, 0.0);

        RequireCuda(cudaSetDevice(0), "cudaSetDevice");
        RequireCuda(cudaMalloc(reinterpret_cast<void**>(&d_boardA), size * sizeof(short)), "cudaMalloc d_boardA");
        RequireCuda(cudaMalloc(reinterpret_cast<void**>(&d_boardB), size * sizeof(short)), "cudaMalloc d_boardB");
        RequireCuda(cudaMalloc(reinterpret_cast<void**>(&d_aggregated), totalThreads * sizeof(double)), "cudaMalloc d_aggregated");
        RequireCuda(cudaMalloc(reinterpret_cast<void**>(&d_reduced), corrMatrixSize * sizeof(double)), "cudaMalloc d_reduced");
        RequireCuda(cudaMalloc(reinterpret_cast<void**>(&d_scaling), totalSegNum * sizeof(double)), "cudaMalloc d_scaling");

        RequireCuda(cudaMemcpy(d_boardA, hostBoardA.data(), size * sizeof(short), cudaMemcpyHostToDevice), "cudaMemcpy boardA");
        RequireCuda(cudaMemcpy(d_boardB, hostBoardB.data(), size * sizeof(short), cudaMemcpyHostToDevice), "cudaMemcpy boardB");
        initializeArrayWithCuda(d_scaling, totalSegNum, 1.0);

        cublasHandle_t handle = nullptr;
        RequireCublas(cublasCreate(&handle), "cublasCreate");

        std::cout << "Running synthetic cross-correlation demo\n";
        std::cout << "Segments: " << totalSegNum << ", segmentSize: " << segmentSize
            << ", matrix: " << demodulationWindowSize << "x" << demodulationWindowSize << "\n";

        const cudaError_t status = ComputeCrossCorrelationGPU(
            1,
            d_boardA,
            d_boardB,
            size,
            totalThreads,
            gridSize,
            blockSize,
            sharedSegmentSize,
            demodulationWindowSize,
            totalSegNum,
            corrMatrixSize,
            segmentSize,
            reduced.data(),
            handle,
            d_aggregated,
            d_reduced,
            d_scaling,
            nullptr,
            nullptr);

        RequireCuda(status, "ComputeCrossCorrelationGPU");

        std::cout << "First 4 rows of the reduced correlation matrix:\n";
        PrintMatrixPreview(reduced, demodulationWindowSize, 4);

        cublasDestroy(handle);
        cudaFree(d_scaling);
        cudaFree(d_reduced);
        cudaFree(d_aggregated);
        cudaFree(d_boardB);
        cudaFree(d_boardA);

        return 0;
    }
    catch (const std::exception& ex) {
        std::cerr << "Workflow demo failed: " << ex.what() << "\n";
        return 1;
    }
}
