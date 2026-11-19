using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class SceneElementFootprint
{
    private readonly int width;
    private readonly int height;
    private readonly int stride;
    private readonly int[] alphaSum;

    // Alpha starts at the top-left, matching the grid's downward Y axis.
    internal SceneElementFootprint(byte[] alpha, int width, int height)
    {
        this.width = width;
        this.height = height;
        stride = width + 1;
        alphaSum = new int[stride * (height + 1)];

        for (int y = 0; y < height; y++)
        {
            int rowSum = 0;
            for (int x = 0; x < width; x++)
            {
                rowSum += alpha[y * width + x];
                alphaSum[(y + 1) * stride + x + 1] = alphaSum[y * stride + x + 1] + rowSum;
            }
        }
    }

    internal Vector2Int[] GetOccupiedCells(float scale, float aspect)
    {
        double size = GameDataConfig.SceneElementDefaultSizeInCells * scale;
        double drawnWidth = size * Math.Min(1d, aspect);
        double drawnHeight = size * Math.Min(1d, 1d / aspect);
        double left = (1d - drawnWidth) * 0.5d;
        double top = (1d - drawnHeight) * 0.5d;
        double pixelsPerCellX = width / drawnWidth;
        double pixelsPerCellY = height / drawnHeight;
        double cellAreaPerAlpha = 1d / (255d * pixelsPerCellX * pixelsPerCellY);
        int right = (int)Math.Ceiling(left + drawnWidth);
        int bottom = (int)Math.Ceiling(top + drawnHeight);
        var occupied = new List<Vector2Int>();

        for (int y = (int)Math.Floor(top); y < bottom; y++)
        {
            double y0 = (y - top) * pixelsPerCellY;
            double y1 = (y + 1d - top) * pixelsPerCellY;
            for (int x = (int)Math.Floor(left); x < right; x++)
            {
                double x0 = (x - left) * pixelsPerCellX;
                double x1 = (x + 1d - left) * pixelsPerCellX;
                double alpha = IntegralAt(x1, y1) - IntegralAt(x0, y1) - IntegralAt(x1, y0) + IntegralAt(x0, y0);
                if (alpha * cellAreaPerAlpha + 0.000001d >= GameDataConfig.SceneElementOccupancyThreshold)
                    occupied.Add(new Vector2Int(x, y));
            }
        }

        return occupied.ToArray();
    }

    // Interpolate the integral at fractional pixel boundaries for exact area within each source pixel.
    private double IntegralAt(double x, double y)
    {
        x = Math.Max(0d, Math.Min(x, width));
        y = Math.Max(0d, Math.Min(y, height));
        int x0 = (int)x;
        int y0 = (int)y;
        int x1 = Math.Min(x0 + 1, width);
        int y1 = Math.Min(y0 + 1, height);
        double dx = x - x0;
        double dy = y - y0;
        double top = alphaSum[y0 * stride + x0] * (1d - dx) + alphaSum[y0 * stride + x1] * dx;
        double bottom = alphaSum[y1 * stride + x0] * (1d - dx) + alphaSum[y1 * stride + x1] * dx;
        return top * (1d - dy) + bottom * dy;
    }
}
