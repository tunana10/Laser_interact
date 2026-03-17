using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Homography: compute 3x3 homography from at least 4 correspondences using DLT
/// - Inputs: srcPoints[] (pixel coords in image), dstPoints[] (pixel coords in projection)
/// - Returns object that can TransformPoint(src)
/// 
/// Implementation details:
/// - Build 2N x 8 system and solve Ax = b for 8 parameters (h11..h32), with h33 = 1
/// - Solve using simple Gaussian elimination (no external libs)
/// </summary>
public class Homography
{
    public float[,] H = new float[3, 3];

    // Apply to 2D point (pixel)
    public Vector2 TransformPoint(Vector2 p)
    {
        float x = p.x, y = p.y;
        float X = H[0, 0] * x + H[0, 1] * y + H[0, 2];
        float Y = H[1, 0] * x + H[1, 1] * y + H[1, 2];
        float W = H[2, 0] * x + H[2, 1] * y + H[2, 2];
        if (Mathf.Abs(W) < 1e-6f) return new Vector2(X, Y);
        return new Vector2(X / W, Y / W);
    }

    // Factory from correspondences
    public static Homography FromCorrespondences(Vector2[] src, Vector2[] dst)
    {
        if (src == null || dst == null || src.Length != dst.Length || src.Length < 4) return null;
        int N = src.Length;
        // Build matrix A (2N x 8) and b (2N)
        float[,] A = new float[2 * N, 8];
        float[] b = new float[2 * N];
        for (int i = 0; i < N; i++)
        {
            float x = src[i].x, y = src[i].y;
            float X = dst[i].x, Y = dst[i].y;
            // equation: [ x y 1 0 0 0 -xX -yX ] [h] = X
            //           [ 0 0 0 x y 1 -xY -yY ]       = Y
            int r1 = 2 * i;
            int r2 = 2 * i + 1;
            A[r1, 0] = x; A[r1, 1] = y; A[r1, 2] = 1f; A[r1, 3] = 0f; A[r1, 4] = 0f; A[r1, 5] = 0f; A[r1, 6] = -x * X; A[r1, 7] = -y * X;
            A[r2, 0] = 0f; A[r2, 1] = 0f; A[r2, 2] = 0f; A[r2, 3] = x; A[r2, 4] = y; A[r2, 5] = 1f; A[r2, 6] = -x * Y; A[r2, 7] = -y * Y;
            b[r1] = X;
            b[r2] = Y;
        }

        // Solve linear least squares A*x = b for x (8 unknowns) via normal equations (A^T A) x = A^T b
        float[,] ATA = new float[8, 8];
        float[] ATb = new float[8];
        // ATA = A^T * A
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 8; j++)
            {
                float s = 0f;
                for (int k = 0; k < 2 * N; k++) s += A[k, i] * A[k, j];
                ATA[i, j] = s;
            }
        for (int i = 0; i < 8; i++)
        {
            float s = 0f;
            for (int k = 0; k < 2 * N; k++) s += A[k, i] * b[k];
            ATb[i] = s;
        }

        // Solve ATA x = ATb by Gaussian elimination
        float[] xsol = GaussianSolve(ATA, ATb);
        if (xsol == null) return null;

        // fill H with h11..h32 and h33 = 1
        Homography Hm = new Homography();
        Hm.H[0, 0] = xsol[0]; Hm.H[0, 1] = xsol[1]; Hm.H[0, 2] = xsol[2];
        Hm.H[1, 0] = xsol[3]; Hm.H[1, 1] = xsol[4]; Hm.H[1, 2] = xsol[5];
        Hm.H[2, 0] = xsol[6]; Hm.H[2, 1] = xsol[7]; Hm.H[2, 2] = 1f;
        return Hm;
    }

    // simple Gaussian elimination solver for NxN linear system
    private static float[] GaussianSolve(float[,] A, float[] b)
    {
        int n = b.Length;
        float[,] M = new float[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) M[i, j] = A[i, j];
            M[i, n] = b[i];
        }
        // forward elimination
        for (int k = 0; k < n; k++)
        {
            // find pivot
            int piv = k;
            float maxv = Mathf.Abs(M[k, k]);
            for (int i = k + 1; i < n; i++)
            {
                float v = Mathf.Abs(M[i, k]);
                if (v > maxv) { maxv = v; piv = i; }
            }
            if (Mathf.Abs(M[piv, k]) < 1e-12f) return null; // singular
            // swap
            if (piv != k)
                for (int j = k; j <= n; j++) { float t = M[k, j]; M[k, j] = M[piv, j]; M[piv, j] = t; }

            // normalize row k
            float diag = M[k, k];
            for (int j = k; j <= n; j++) M[k, j] /= diag;

            // eliminate below
            for (int i = k + 1; i < n; i++)
            {
                float f = M[i, k];
                if (Mathf.Abs(f) < 1e-12f) continue;
                for (int j = k; j <= n; j++) M[i, j] -= f * M[k, j];
            }
        }
        // back substitution
        float[] x = new float[n];
        for (int i = n - 1; i >= 0; i--)
        {
            float s = M[i, n];
            for (int j = i + 1; j < n; j++) s -= M[i, j] * x[j];
            x[i] = s / M[i, i];
        }
        return x;
    }
}
