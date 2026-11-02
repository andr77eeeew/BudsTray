using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BudsTray.UI;

/// <summary>The tray icon: the charge of the emptier bud in pixel digits, which stay crisp at 16 px.</summary>
public static class TrayGlyph
{
    public const int LowBatteryLevel = 20;
    private const int GlyphHeight = 5;
    private const int WideGlyphColumns = 3;
    private const int UnknownAlpha = 130;

    private static readonly Color Low = Color.FromArgb(0xE5, 0x24, 0x2E);

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = new[] { "###", "#.#", "#.#", "#.#", "###" },
        // Narrow, so that "100" fits the 16 px icon without shrinking the other digits.
        ['1'] = new[] { "#", "#", "#", "#", "#" },
        ['2'] = new[] { "###", "..#", "###", "#..", "###" },
        ['3'] = new[] { "###", "..#", "###", "..#", "###" },
        ['4'] = new[] { "#.#", "#.#", "###", "..#", "..#" },
        ['5'] = new[] { "###", "#..", "###", "..#", "###" },
        ['6'] = new[] { "###", "#..", "###", "#.#", "###" },
        ['7'] = new[] { "###", "..#", "..#", "..#", "..#" },
        ['8'] = new[] { "###", "#.#", "###", "#.#", "###" },
        ['9'] = new[] { "###", "#.#", "###", "..#", "###" },
        ['-'] = new[] { "...", "...", "###", "...", "..." },
    };

    public static Icon Render(int size, int? left, int? right, bool lightTaskbar)
    {
        int? level = Lowest(left, right);
        Color ink = lightTaskbar ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White;
        Color color = level switch
        {
            null => Color.FromArgb(UnknownAlpha, ink),
            <= LowBatteryLevel => Low,
            _ => ink,
        };

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(color))
            DrawText(g, brush, level is { } value ? Math.Clamp(value, 0, 100).ToString() : "--", size);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            // Clone so the icon owns its data and the GDI handle can go now.
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static int? Lowest(int? left, int? right) =>
        left is { } l && right is { } r ? Math.Min(l, r) : left ?? right;

    private static void DrawText(Graphics g, Brush brush, string text, int size)
    {
        // Two wide digits and a gap take 7 cells; that sets the pixel size for every value.
        int scale = Math.Max(1, size / (2 * WideGlyphColumns + 1));
        int gap = text.Length > 2 ? Math.Max(1, scale / 2) : scale;

        int width = (text.Length - 1) * gap;
        foreach (char c in text)
            width += Glyphs[c][0].Length * scale;

        int x = (size - width) / 2;
        int y = (size - GlyphHeight * scale) / 2;
        foreach (char c in text)
        {
            string[] glyph = Glyphs[c];
            for (int row = 0; row < GlyphHeight; row++)
            {
                for (int column = 0; column < glyph[row].Length; column++)
                {
                    if (glyph[row][column] == '#')
                        g.FillRectangle(brush, x + column * scale, y + row * scale, scale, scale);
                }
            }
            x += glyph[0].Length * scale + gap;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
