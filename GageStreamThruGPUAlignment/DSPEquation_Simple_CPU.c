#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

// Function to compute the correlation matrix for a segment
void computeCorrelationMatrix(short* segment, double* correlationMatrix) {
    for (int i = 0; i < 8; i++) {
        for (int j = 0; j < 8; j++) {
            double value1 = (double)segment[i * 2];
            double value2 = (double)segment[(i + 8) * 2];
            double value3 = (double)segment[j * 2 + 1];
            double value4 = (double)segment[(j + 8) * 2 + 1];
            double result = (value1 - value2) * (value3 - value4);
            correlationMatrix[i * 8 + j] = result;
        }
    }
}

// Function to average all correlation matrices
void averageCorrelationMatrices(double* correlationMatrices, double* averageMatrix, int numSegments) {
	memset(averageMatrix, 0, 64 * sizeof(double));      // Initialize average matrix to zero
  
	// Sum all correlation matrices
    for (int k = 0; k < numSegments; k++) {
        for (int i = 0; i < 64; i++) {
            averageMatrix[i] +=  correlationMatrices[k * 64 + i];
        }
    }

	// Average the correlation matrices
    for (int i = 0; i < 64; i++) {
        averageMatrix[i] /= numSegments;
    }
}


// Function to compare two matrices
int compareMatrices(double* matrix1, double* matrix2, int size, double tolerance) {
    for (int i = 0; i < size; ++i) {
        if (fabs(matrix1[i] - matrix2[i]) > tolerance) {
            return 0; // Matrices are not equal
        }
    }
    return 1; // Matrices are equal
}


// Function to compute the average correlation matrix
int CPU_Equation_PlusOne(void* buffer, __int64 length, double* gpu_average_matrix) {
    // Number of elements in the buffer
    __int64 numElements = length;
    int numSegments = numElements / 32;
    short* inputArray = (short*)buffer;


    // Allocate memory for correlation matrices
    double* correlationMatrices = (double*)malloc(numSegments * 64 * sizeof(double));
    double* averageMatrix = (double*)malloc(64 * sizeof(double));

	// Check if memory allocation was successful
    if (!correlationMatrices || !averageMatrix) {
        printf("Memory allocation failed\n");
        return -1;
    }

    // Compute correlation matrices for each segment
    for (int i = 0; i < numSegments; i++) {
        computeCorrelationMatrix(&inputArray[i * 32], &correlationMatrices[i * 64]);
    }

  
    // Average the correlation matrices
    averageCorrelationMatrices(correlationMatrices, averageMatrix, numSegments);

    // Compare GPU correlation matrix with CPU computed matrices
    int gpu_correct = compareMatrices(averageMatrix, gpu_average_matrix, 64, 0);


    // Write the average correlation matrix to a file
    FILE* fptr = fopen("Analysis.txt", "a");
    if (fptr == NULL) {
        printf("Failed to open file\n");
        free(correlationMatrices);
        free(averageMatrix);
        return -1;
    }


	// Write the average correlation matrix to the file
	fprintf(fptr, "CPU Average Correlation Matrix:\n");
    for (int i = 0; i < 8; i++) {
        for (int j = 0; j < 8; j++) {
            fprintf(fptr, "%.10f ", averageMatrix[i * 8 + j]);
        }
    }
    fprintf(fptr, "\n");


	// Write the result of the comparison to the file
    if (gpu_correct) {
        fprintf(fptr, "GPU Compution is correct.\n");
    }
    else {
        fprintf(fptr, "GPU Compution is not correct.\n");
    }


    fclose(fptr);

    // Free allocated memory
    free(correlationMatrices);
    free(averageMatrix);

    return 0;
}



