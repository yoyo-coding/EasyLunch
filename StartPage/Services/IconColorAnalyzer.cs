using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace StartPage.Services;

/// <summary>
/// 从图标 PNG 中提取主色，并生成适配深浅色主题的圆角矩形遮罩颜色。
/// 浅色主题使用较亮的淡化色，深色主题使用较深的颜色。
/// </summary>
public static class IconColorAnalyzer
{
    // 只统计足够不透明的像素，避免透明边缘干扰主色。
    private const byte OpaqueAlphaThreshold = 40;

    // 浅色模式：主色向白色靠拢的比例，数值越大颜色越亮。
    private const double LightLightenFactor = 0.68;

    // 浅色模式：遮罩透明度（0~255），数值越小越透明。
    private const byte LightAlpha = 0x40;

    // 深色模式：主色保留比例（乘数），数值越小颜色越深。
    private const double DarkKeepFactor = 0.55;

    // 深色模式：遮罩透明度（0~255），深色背景下需要更实一点才可见。
    private const byte DarkAlpha = 0x66;

    /// <summary>
    /// 返回适配深浅色主题的两个遮罩颜色（"#AARRGGBB"）；分析失败时返回 null。
    /// </summary>
    public static IconMaskColors? GetMaskColors(string iconPath)
    {
        try
        {
            using var bitmap = new Bitmap(iconPath);
            if (!TryGetDominantColor(bitmap, out var r, out var g, out var b))
            {
                return null;
            }

            // 浅色模式：向白色靠拢，颜色更亮。
            var lightR = (byte)Math.Round(r + (255 - r) * LightLightenFactor);
            var lightG = (byte)Math.Round(g + (255 - g) * LightLightenFactor);
            var lightB = (byte)Math.Round(b + (255 - b) * LightLightenFactor);

            // 深色模式：向黑色靠拢，颜色更深。
            var darkR = (byte)Math.Round(r * DarkKeepFactor);
            var darkG = (byte)Math.Round(g * DarkKeepFactor);
            var darkB = (byte)Math.Round(b * DarkKeepFactor);

            return new IconMaskColors(
                $"#{LightAlpha:X2}{lightR:X2}{lightG:X2}{lightB:X2}",
                $"#{DarkAlpha:X2}{darkR:X2}{darkG:X2}{darkB:X2}");
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetDominantColor(Bitmap bitmap, out int r, out int g, out int b)
    {
        r = g = b = 0;

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var buffer = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            // 采样步长：每 4 像素取 1 个，256x256 图标只需处理 4096 个像素。
            const int step = 4;
            // 每通道量化 5 bit，共 32*32*32 个颜色桶。
            const int shift = 5;

            var buckets = new Dictionary<int, ColorBucket>();
            long sumR = 0, sumG = 0, sumB = 0;
            var opaqueCount = 0;

            for (var y = 0; y < bitmap.Height; y += step)
            {
                var row = y * stride;
                for (var x = 0; x < bitmap.Width; x += step)
                {
                    var index = row + x * 4;
                    var alpha = buffer[index + 3];
                    if (alpha < OpaqueAlphaThreshold)
                    {
                        continue;
                    }

                    var pr = buffer[index + 2];
                    var pg = buffer[index + 1];
                    var pb = buffer[index];

                    sumR += pr;
                    sumG += pg;
                    sumB += pb;
                    opaqueCount++;

                    var key = (pr >> shift) << 10 | (pg >> shift) << 5 | (pb >> shift);
                    if (!buckets.TryGetValue(key, out var bucket))
                    {
                        bucket = new ColorBucket();
                        buckets[key] = bucket;
                    }

                    bucket.R += pr;
                    bucket.G += pg;
                    bucket.B += pb;
                    bucket.Count++;
                }
            }

            if (opaqueCount == 0)
            {
                return false;
            }

            // 优先选择“有彩色倾向”的桶（避开纯白/纯黑背景），找不到再退回全局平均。
            ColorBucket? best = null;
            foreach (var bucket in buckets.Values)
            {
                if (bucket.Count <= 1)
                {
                    continue;
                }

                var avgR = bucket.R / (double)bucket.Count;
                var avgG = bucket.G / (double)bucket.Count;
                var avgB = bucket.B / (double)bucket.Count;
                var max = Math.Max(avgR, Math.Max(avgG, avgB));
                var min = Math.Min(avgR, Math.Min(avgG, avgB));
                if (max - min < 24)
                {
                    continue;
                }

                if (best is null || bucket.Count > best.Count)
                {
                    best = bucket;
                }
            }

            if (best is not null)
            {
                r = (int)Math.Round(best.R / (double)best.Count);
                g = (int)Math.Round(best.G / (double)best.Count);
                b = (int)Math.Round(best.B / (double)best.Count);
                return true;
            }

            r = (int)Math.Round(sumR / (double)opaqueCount);
            g = (int)Math.Round(sumG / (double)opaqueCount);
            b = (int)Math.Round(sumB / (double)opaqueCount);
            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private sealed class ColorBucket
    {
        public long R;
        public long G;
        public long B;
        public int Count;
    }
}

/// <summary>一组适配深浅色主题的遮罩颜色。</summary>
public sealed record IconMaskColors(string Light, string Dark);