using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace BudsTray.UI;

/// <summary>Draws a number on a 5x7 dot grid.</summary>
public sealed class DotMatrixNumber : FrameworkElement
{
    private const int Columns = 5;
    private const double DotRadiusRatio = 0.42;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
        ['3'] = new[] { ".###.", "#...#", "....#", "..##.", "....#", "#...#", ".###." },
        ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
        ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
        ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
        ['7'] = new[] { "#####", "....#", "...#.", "...#.", "..#..", "..#..", "..#.." },
        ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
        ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "#...#", ".###." },
        ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
    };

    private const int Rows = 7;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DotMatrixNumber),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(DotMatrixNumber),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PitchProperty = DependencyProperty.Register(
        nameof(Pitch), typeof(double), typeof(DotMatrixNumber),
        new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Brush DotBrush { get => (Brush)GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }
    public double Pitch { get => (double)GetValue(PitchProperty); set => SetValue(PitchProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = Text.Length;
        double width = count == 0 ? 0 : (count * Columns + (count - 1)) * Pitch;
        return new Size(width, Rows * Pitch);
    }

    protected override void OnRender(DrawingContext context)
    {
        double radius = Pitch * DotRadiusRatio;
        for (int i = 0; i < Text.Length; i++)
        {
            if (!Glyphs.TryGetValue(Text[i], out string[]? glyph))
                continue;

            double originX = i * (Columns + 1) * Pitch;
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    if (glyph[row][column] != '#')
                        continue;
                    var center = new Point(originX + (column + 0.5) * Pitch, (row + 0.5) * Pitch);
                    context.DrawEllipse(DotBrush, null, center, radius, radius);
                }
            }
        }
    }
}
