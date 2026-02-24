
#include "cuda_runtime.h"
#include "device_launch_parameters.h"
#include <stdio.h>
#include <Windows.h>
#include <stdlib.h>
#include <time.h>
#include <cublas_v2.h>
#include <iostream>




void checkCuda(cudaError_t result, const char* msg) {
	if (result != cudaSuccess) {
		std::cerr << "CUDA Error: " << msg << " (" << cudaGetErrorString(result) << ")\n";
		exit(EXIT_FAILURE);
	}
}

void checkCublas(cublasStatus_t result, const char* msg) {
	if (result != CUBLAS_STATUS_SUCCESS) {
		std::cerr << "cuBLAS Error: " << msg << "\n";
		std::cerr << "Error Code: " << result << "\n";
		exit(EXIT_FAILURE);
	}
}

// Function to get the current time in HH:mm:ss.fff format
void getCurrentTime(char* timeBuffer, size_t bufferSize) {
	SYSTEMTIME time;
	GetLocalTime(&time);
	snprintf(timeBuffer, bufferSize, "%02d:%02d:%02d.%03d",
		time.wHour, time.wMinute, time.wSecond, time.wMilliseconds);
}

void writeResultsToFile(FILE* AnalysisFile, FILE* binFile, int u32LoopCount, double* h_odata, int corrMatrixSize) {
	char timeBuffer[20]; // Buffer to hold time in HH:mm:ss.fff format
	getCurrentTime(timeBuffer, sizeof(timeBuffer)); // Fetch current time

	// Write results to Analysis file
	if (AnalysisFile) {
		fprintf(AnalysisFile, "%d\t%s\t", u32LoopCount, timeBuffer); // Add loop count and time
		for (int i = 0; i < corrMatrixSize; ++i) {
			fprintf(AnalysisFile, "%.10f\t", h_odata[i]);
		}
		fprintf(AnalysisFile, "\n");
	}

	// Write results to binary file for Matlab use
	if (binFile) {
		fwrite(h_odata, sizeof(double), corrMatrixSize, binFile);
	}
}


// One segment per block.
// dataA: board1, ABAB... (A,B,A,B,...)
// dataB: board2, ABAB... (A,B,A,B,...)
__global__ void demodulationCrossCorrelation(
	const short* __restrict__ dataA,
	const short* __restrict__ dataB,
	const long long numElements,              // total *samples* per board (ABAB)
	double* __restrict__ aggregatedCorrMatrix,
	const int sharedSegmentSize,              // unused except for sanity; segmentSize <= sharedSegmentSize
	const int totalThreads,                   // unused
	const int demodulationWindowSize,                              // demodulationWindowSize
	const int corrMatrixSize,                 // W * W
	const int segmentSize                     // in FRAMES, usually 2*W
)
{
	int index = blockDim.x * blockIdx.x + threadIdx.x;


	// Declare shared memory
	extern __shared__ double sharedSegment[];

	// Only the first 32 threads in the block load data into shared memory
	if (threadIdx.x < sharedSegmentSize) {
		if (threadIdx.x % 2 == 0) {
			sharedSegment[threadIdx.x] = static_cast<double>(dataA[blockIdx.x * sharedSegmentSize + threadIdx.x]);
		}
		else {
			sharedSegment[threadIdx.x] = static_cast<double>(dataB[blockIdx.x * sharedSegmentSize + threadIdx.x]);
		}
	}

	__syncthreads();

	if (index < totalThreads) {
		int row = threadIdx.x % corrMatrixSize / demodulationWindowSize;
		int col = threadIdx.x % demodulationWindowSize;

		int segmentStart = threadIdx.x / corrMatrixSize * segmentSize; // Determine the starting index of the segment in shared memory

		double value1 = sharedSegment[segmentStart + row * 2];
		double value2 = sharedSegment[segmentStart + (row + demodulationWindowSize) * 2];
		double value3 = sharedSegment[segmentStart + col * 2 + 1];
		double value4 = sharedSegment[segmentStart + (col + demodulationWindowSize) * 2 + 1];

		//double corrValue = (value1 - value2) * (value3 - value4);
		double corrValue = (value1 + value2) /1; // This is for aligment testing only

		aggregatedCorrMatrix[index] = corrValue; // Correlation matrix, one column is a single correlation matrix, column-major order
	}
}


__global__ void demodulationAutoCorrelation(short* data,
	short* dataB,
	__int64 numElements,
	double* autoCorrelationMatrixA,
	double* autoCorrelationMatrixB,
	const int sharedSegmentSize,
	const int totalThreads,
	const int demodulationWindowSize,
	const int corrMatrixSize,
	const int segmentSize)
{

	int index = blockDim.x * blockIdx.x + threadIdx.x;


	// Declare shared memory
	extern __shared__ double sharedSegment[];

	// Only the first 32 threads in the block load data into shared memory
	if (threadIdx.x < sharedSegmentSize) {
		sharedSegment[threadIdx.x] = static_cast<double>(data[blockIdx.x * sharedSegmentSize + threadIdx.x]);
	}

	__syncthreads(); // Ensure all threads have loaded their data into shared memory

	if (index < totalThreads) {
		int row = threadIdx.x % corrMatrixSize / demodulationWindowSize;
		int col = threadIdx.x % demodulationWindowSize;

		int segmentStart = threadIdx.x / corrMatrixSize * segmentSize; // Determine the starting index of the segment in shared memory


		double A_value_1 = sharedSegment[segmentStart + row * 2];
		double A_value_2 = sharedSegment[segmentStart + (row + 8) * 2];
		double A_value_3 = sharedSegment[segmentStart + col * 2];
		double A_value_4 = sharedSegment[segmentStart + (col + 8) * 2];

		double B_value_1 = sharedSegment[segmentStart + row * 2 + 1];
		double B_value_2 = sharedSegment[segmentStart + (row + 8) * 2 + 1];
		double B_value_3 = sharedSegment[segmentStart + col * 2 + 1];
		double B_value_4 = sharedSegment[segmentStart + (col + 8) * 2 + 1];

		double corrValueA = (A_value_1 - A_value_2) * (A_value_3 - A_value_4);
		double corrValueB = (B_value_1 - B_value_2) * (B_value_3 - B_value_4);

		// Store the correlation matrix in column-major order
		autoCorrelationMatrixA[index] = corrValueA; // Correlation matrix, one column is a single correlation matrix
		autoCorrelationMatrixB[index] = corrValueB; // Correlation matrix, one column is a single correlation matrix

	}
}


// CUDA kernel to initialize the array
__global__ void initializeArrayKernel(double* array, int size, double value) {
	int idx = blockIdx.x * blockDim.x + threadIdx.x;
	if (idx < size) {
		array[idx] = value;
	}
}


// Function to initialize the array with 1/N
extern "C" void initializeArrayWithCuda(double* dev_array, int size, double value) {
	int blockSize = 256;
	int numBlocks = (size + blockSize - 1) / blockSize;
	initializeArrayKernel << <numBlocks, blockSize >> > (dev_array, size, value);
	cudaDeviceSynchronize();
}

// CUDA kernel to divide the matrix by N
__global__ void averageMatrixKernel(double* averageMatrix, int N) {
	int idx = blockIdx.x * blockDim.x + threadIdx.x;
	if (idx < 64) {
		averageMatrix[idx] /= N;
	}
}

__global__ void divideG2Matrix(double* g2Matrix, double* d_reducedCorrMatrixA, double* d_reducedCorrMatrixB, int size, int totalSegNum) {
	int ij = threadIdx.x; // ij index 
	int mn = blockIdx.x; // mn index
	int idx = blockIdx.x * blockDim.x + threadIdx.x; // get the index of the thread in global space 

	if (idx < size) {
		g2Matrix[idx] /= d_reducedCorrMatrixA[ij] * d_reducedCorrMatrixB[mn] / totalSegNum;
	}
}

#define CUDA_CHECK(call)                                                     \
    do {                                                                     \
        cudaError_t _e = (call);                                             \
        if (_e != cudaSuccess) {                                             \
            fprintf(stderr, "CUDA error %s:%d: %s\n",                        \
                    __FILE__, __LINE__, cudaGetErrorString(_e));             \
            exit(1);                                                         \
        }                                                                    \
    } while (0)



// Helper function for using CUDA to compute cross correlation.
extern "C" cudaError_t ComputeCrossCorrelationGPU(const __int64 u32LoopCount,			// Loop count
	short* data,																		// Input data
	short* dataB,																		// Input data B, not used here
	const __int64 size,																	// Size of the input data
	const int totalThreads,																// Total number of threads
	const int gridSize,																	// Thread Grid size
	const int blockSize,																// Thread Block size
	const int sharedSegmentSize,														// Shared Memory segment size
	const int demodulationWindowSize,													// Demodulation window size
	const int totalSegNum,																// Total number of segments of input data
	const int corrMatrixSize,															// Cross Correlation Matrix size
	const int segmentSize,																// Data Segment size
	double* h_odata,																	// Output data
	cublasHandle_t handle,																// cuBLAS handle
	double* d_aggregatedCorrMatrix,														// Aggregated correlation matrix
	double* d_reducedCorrMatrix,														// Reduced correlation matrix, here means mean correlation matrix
	double* d_scaling_factors,															// Scaling factors
	FILE * binFile,																		// Binary file for storing reduced correlation matrix	
	FILE * AnalysisFile)																// Analysis file showing the reduced correlation matrix
{
	cudaError_t cudaStatus = cudaSuccess; // Return status of CUDA functions



	//short* h_dataA;		// Host input data from the GPU
	//short* h_dataB;
	//double* h_dataAB;	// Host output data from the GPU
	//double* h_aggregatedCorrMatrix;
	//h_dataA = (short*)malloc(size / 2 * sizeof(short));		// Output data from the GPU
	//h_dataB = (short*)malloc(size / 2 * sizeof(short));
	//h_dataAB = (double*)malloc(corrMatrixSize * sizeof(double));
	//h_aggregatedCorrMatrix = (double*)malloc(size*2 * sizeof(double)); // Aggregated correlation matrix from GPU

	// Compute the correlation matrix for each segment of data chunked by demodulation window policy
	demodulationCrossCorrelation << <gridSize, blockSize, sharedSegmentSize * sizeof(double) >> > (data, dataB, size, d_aggregatedCorrMatrix, sharedSegmentSize, totalThreads, demodulationWindowSize, corrMatrixSize, segmentSize);
	// Record stop event and synchronize


	// Perform matrix-vector multiplication using cuBLAS for reduding the aggregated correlation matrix
	const int Nrows = corrMatrixSize;
	const int Ncols = totalSegNum;
	const double alpha = 1.0;
	const double beta = 0.0;

	// Wait for the GPU to finish
	checkCuda(cudaDeviceSynchronize(), "Kernel execution failed");

	// d_aggregatedCorrMatrix is a corrMatrixSize x totalSegNum matrix
	// d_scaling_factors is a totalSegNum x 1 vector
	// d_averageMatrix is a corrMatrixSize x 1 vector

	cublasStatus_t cublasStatus = cublasDgemv(handle, CUBLAS_OP_N, Nrows, Ncols, &alpha,
		d_aggregatedCorrMatrix, corrMatrixSize,
		d_scaling_factors, 1,
		&beta, d_reducedCorrMatrix, 1);
	checkCublas(cublasStatus, "cuBLAS Dgemv failed");

	averageMatrixKernel << <1, corrMatrixSize >> > (d_reducedCorrMatrix, totalSegNum);	// Average the reduced matrix

	// Copy the result from device back to the host
	checkCuda(cudaMemcpy(h_odata, d_reducedCorrMatrix, corrMatrixSize * sizeof(double), cudaMemcpyDeviceToHost), "cudaMemcpy failed");


	//checkCuda(cudaMemcpy(h_dataA, data, size / 2 * sizeof(short), cudaMemcpyDeviceToHost), "cudaMemcpy failed");
	//checkCuda(cudaMemcpy(h_dataB, dataB, size / 2 * sizeof(short), cudaMemcpyDeviceToHost), "cudaMemcpy failed");
	//checkCuda(cudaMemcpy(h_aggregatedCorrMatrix, d_aggregatedCorrMatrix,size*2 * sizeof(double), cudaMemcpyDeviceToHost), "cudaMemcpy failed");
	//const int W = 8;
	//const int segmentSizeH = 2 * W;                 // 16 per channel
	//const int perChan = (int)(size / 2);           // 'size' is total across A+B
	//const int numSegments = perChan / segmentSizeH; // safer than size/32
	//const int corrSize = W * W;
	//////// zero the accumulator
	//for (int i = 0; i < corrSize; ++i) h_dataAB[i] = 0.0;
	//for (int seg = 0; seg < numSegments; ++seg) {
	//	const int base = seg * segmentSizeH; // per-channel base
	//	for (int j = 0; j < W; ++j) {
	//		const double da = (double)h_dataA[base + j] - (double)h_dataA[base + W + j]; // A0-A1
	//		for (int k = 0; k < W; ++k) {
	//			const double db = (double)h_dataB[base + k] - (double)h_dataB[base + W + k]; // B0-B1
	//			const int idx = j * W + k;   // row-major to match GPU write
	//			h_dataAB[idx] += da * db;    // accumulate across segments
	//			//printf("%d\t%f\t%f\t%f\t%f\n",idx+ seg*64,da ,db, da * db, h_aggregatedCorrMatrix[idx + seg * 64]);
	//		}
	//	}
	//}
	//for (int i = 0; i < corrMatrixSize; ++i) {
	//	h_dataAB[i] /= (double)size / 32;
	//	if (h_dataAB[i]!= h_odata[i])	printf("CPU: %d\t%.10f\tGPU: %.10f\n", i, h_dataAB[i], h_odata[i]);
	//	else
	//	{
	//	printf("Match %d\tCPU=GPU\n", i);
	//	}
	//}

	// Wait for the GPU to finish
	checkCuda(cudaDeviceSynchronize(), "Kernel execution failed");


	// Write results to Analysis file
	if (AnalysisFile) {
		writeResultsToFile(AnalysisFile, binFile, u32LoopCount, h_odata, corrMatrixSize);
	}


	return cudaStatus;
}



// Helper function for using CUDA to compute G2 correlation.
extern "C" cudaError_t ComputeG2CorrelationGPU(const __int64 u32LoopCount,           // Loop count
	short* data,                                                                     // Input data
	short* dataB,                                                                     // Input data
	const __int64 size,                                                              // Size of the input data
	const int totalThreads,                                                          // Total number of threads
	const int gridSize,                                                              // Thread Grid size
	const int blockSize,                                                             // Thread Block size
	const int sharedSegmentSize,                                                     // Shared Memory segment size
	const int demodulationWindowSize,													// Demodulation window size
	const int totalSegNum,                                                           // Total number of segments of input data
	const int corrMatrixSize,                                                        // Auto Correlation Matrix size
	const int segmentSize,                                                           // Data Segment size
	double* h_odata,                                                                 // Output data
	cublasHandle_t handle,                                                           // cuBLAS handle
	double* d_correlationMatrixA,                                                    // Correlation matrix A
	double* d_correlationMatrixB,                                                    // Correlation matrix B
	double* d_g2Matrix,                                                              // G2 matrix (final output)
	double* d_reducedCorrMatrixA,                                                     // Reduced Auto correlation matrix A
	double* d_reducedCorrMatrixB,                                                     // Reduced Auto correlation matrix B
	double* d_scaling_factors,                                                       // Scaling factors
	FILE * binFile,                                                                  // Binary file for storing the G2 matrix
	FILE * AnalysisFile)                                                             // Analysis file for showing the G2 matrix
{
	cudaError_t cudaStatus = cudaSuccess; // Return status of CUDA functions

	// Compute correlation matrices A and B using shared memory
	demodulationAutoCorrelation << <gridSize, blockSize, sharedSegmentSize * sizeof(double) >> > (data, dataB, size, d_correlationMatrixA, d_correlationMatrixB, sharedSegmentSize, totalThreads, demodulationWindowSize, corrMatrixSize, segmentSize);



	// Perform matrix-vector multiplication using cuBLAS for reducing the aggregated correlation matrix A
	// 64 x N matrix-vector multiplication
	int Nrows = corrMatrixSize;
	int Ncols = totalSegNum;
	double alpha = 1.0;
	double beta = 0.0;

	// d_correlationMatrix is a corrMatrixSize x totalSegNum matrix
	// d_scaling_factors is a totalSegNum x 1 vector
	// d_reducedCorrMatrixA is a corrMatrixSize x 1 vector
	cublasStatus_t cublasStatus_1 = cublasDgemv(handle, CUBLAS_OP_N, Nrows, Ncols, &alpha,
		d_correlationMatrixA, corrMatrixSize,
		d_scaling_factors, 1,
		&beta, d_reducedCorrMatrixA, 1);
	checkCublas(cublasStatus_1, "cuBLAS Dgemv 0 failed");

	// d_correlationMatrix is a corrMatrixSize x totalSegNum matrix
	// d_scaling_factors is a totalSegNum x 1 vector
	// d_reducedCorrMatrixB is a corrMatrixSize x 1 vector
	cublasStatus_t cublasStatus_2 = cublasDgemv(handle, CUBLAS_OP_N, Nrows, Ncols, &alpha,
		d_correlationMatrixB, corrMatrixSize,
		d_scaling_factors, 1,
		&beta, d_reducedCorrMatrixB, 1);
	checkCublas(cublasStatus_2, "cuBLAS Dgemv 1 failed");


	// Perform matrix-matrix multiplication using cuBLAS for G2 correlation matrix computation
	Nrows = corrMatrixSize;
	Ncols = corrMatrixSize;
	int Kdim = totalSegNum;
	alpha = 1.0;
	beta = 0.0;

	// d_correlationMatrixA and d_correlationMatrixB are both corrMatrixSize x totalSegNum matrices
	// We want to compute G2 matrix which is the result of matrix multiplication: A * B^T
	cublasStatus_t cublasStatus_3 = cublasDgemm(
		handle,
		CUBLAS_OP_N, CUBLAS_OP_N,
		Nrows, Ncols, Kdim,
		&alpha,
		d_correlationMatrixA, Nrows,  // Leading dimension of matrix A is Nrows (corrMatrixSize)
		d_correlationMatrixB, Kdim,  // Leading dimension of matrix B is Kdim (totalSegNum), since B is transposed
		&beta,
		d_g2Matrix, Nrows  // Output G2 matrix has leading dimension Nrows (corrMatrixSize)
	);
	checkCublas(cublasStatus_3, "cuBLAS Dgemm for G2 correlation matrix failed");


	// Divide the g2 matrix by auto correlation matrix A and B
	divideG2Matrix << <Nrows, Ncols >> > (d_g2Matrix, d_reducedCorrMatrixA, d_reducedCorrMatrixB, corrMatrixSize * corrMatrixSize, totalSegNum);

	// Copy the result back to the host
	checkCuda(cudaMemcpy(h_odata, d_g2Matrix, corrMatrixSize * corrMatrixSize * sizeof(double), cudaMemcpyDeviceToHost), "cudaMemcpy failed");

	// Wait for the GPU to finish
	checkCuda(cudaDeviceSynchronize(), "Kernel execution failed");

	// Write results to the Analysis file
	writeResultsToFile(AnalysisFile, binFile, u32LoopCount, h_odata, corrMatrixSize * corrMatrixSize);

	return cudaStatus;
}






