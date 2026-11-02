using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace BudsTray.UI;

public enum BudsShape
{
    LeftBud,
    RightBud,
    Case,
}

/// <summary>
/// The buds and the case as dots, which also work as a gauge: the charge rises through the shape.
/// Traced from photos of the CMF Buds Pro 2, front view for the buds and top view for the case.
/// </summary>
public sealed class BudsGlyph : FrameworkElement
{
    private const double DotRadiusRatio = 0.42;

    // How much of the wave a single dot takes to grow, as a share of the whole.
    private const double WaveSoftness = 0.22;

    private static readonly string[] BudRows =
    {
        "............######.........",
        "..........#########+.......",
        "........#######..##+++.....",
        ".......######..##..#+++....",
        ".......#####.######.+++....",
        "......#####.###..###.+++...",
        "......#####.##.##.##.++++..",
        ".....######.##.##.##.++++..",
        ".....######.###..###.++++..",
        ".....######.########.+++++.",
        ".....######.########.+++++.",
        "...-.######.########.+++++.",
        "..--.######.########.+++++.",
        "..--.######.########.+++++.",
        ".---.+#####.########.+++++.",
        ".----.++###.########.++++..",
        ".----.+++++.########.++++..",
        ".-----.++++.########.++++..",
        ".-----.++++.########.+++...",
        ".------.+++.########.++....",
        ".-------.++.########.+.....",
        "..-------.+.########.......",
        "...-------..########.......",
        "....-----...########.......",
        "............###..###.......",
        "............##.##.##.......",
        "............##.##.##.......",
        "............###..###.......",
        "............########.......",
        "............###.#.##.......",
        "............##.#.###.......",
        "............###.#.##.......",
        "............########.......",
        "............###..###.......",
        "............##....##.......",
        "............##....##.......",
        "............###..###.......",
        "............########.......",
        ".............######........",
        "..............####.........",
    };

    private static readonly string[] CaseRows =
    {
        "....#####..++++++++++++++++++...",
        "..#########.++++++++++++++++++..",
        ".###########.++++++++++++++++++.",
        ".############.++++++++++++++++++",
        "#########..##.++++++++++++++++++",
        "########.####.++++++++++++++++++",
        "#############.++++++++++++++++++",
        "#############.++++++++++++++++++",
        "###..########.++++++++++++++++++",
        ".##.#########.++++++++++++++++++",
        ".###########..++++++++++++++++++",
        "+.#########..+++++++++++++++++++",
        "++.#######..++++++++++++++++++++",
        "+++........+++++++++++++++++++++",
        "++++++++++++++++++++++++++++++++",
        "++++++++++++++++++++++++++++++++",
        "++++++++++++++++++++++++++++++++",
        "++++++++++++++++++++++++++++++++",
        "++++++++++++++++++++++++++++++++",
        "++++++++++++++++++++++++++++++++",
        "++++++++++++++++++++++++++++++++",
        "+++++++++++++.................++",
        "+++++++++++++..###.......###..++",
        "+++++++++++++.#...#.#.#.#####.++",
        "+++++++++++++.#...#..#..#####.++",
        "+++++++++++++.#...#.#.#.#####.++",
        "+++++++++++++..###.......###..++",
        "+++++++++++++.................++",
        "++++++++++++++++++++++++++++++++",
        ".++++++++++++++++++++++++++++++.",
        "..++++++++++++++++++++++++++++..",
        "...++++++++++++++++++++++++++...",
    };

    private static readonly Dictionary<BudsShape, Dot[]> Shapes = new()
    {
        [BudsShape.LeftBud] = Dots(Rows(BudsShape.LeftBud)),
        [BudsShape.RightBud] = Dots(Rows(BudsShape.RightBud)),
        [BudsShape.Case] = Dots(Rows(BudsShape.Case)),
    };

    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(
        nameof(Shape), typeof(BudsShape), typeof(BudsGlyph),
        new FrameworkPropertyMetadata(BudsShape.LeftBud, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PitchProperty = DependencyProperty.Register(
        nameof(Pitch), typeof(double), typeof(BudsGlyph),
        new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RevealProperty = DependencyProperty.Register(
        nameof(Reveal), typeof(double), typeof(BudsGlyph),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(double), typeof(BudsGlyph),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(BudsGlyph),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyBrushProperty = DependencyProperty.Register(
        nameof(EmptyBrush), typeof(Brush), typeof(BudsGlyph),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public BudsShape Shape { get => (BudsShape)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public double Pitch { get => (double)GetValue(PitchProperty); set => SetValue(PitchProperty, value); }

    /// <summary>0 shows nothing, 1 the whole shape. A wave of dots runs down from the top.</summary>
    public double Reveal { get => (double)GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

    /// <summary>How high the charge reaches, 0 to 1.</summary>
    public double Fill { get => (double)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush DotBrush { get => (Brush)GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }
    public Brush EmptyBrush { get => (Brush)GetValue(EmptyBrushProperty); set => SetValue(EmptyBrushProperty, value); }

    /// <summary>The right bud is the left one mirrored, with the tips pointing away from each other.</summary>
    public static string[] Rows(BudsShape shape) => shape switch
    {
        BudsShape.LeftBud => BudRows,
        BudsShape.RightBud => BudRows.Select(row => new string(row.Reverse().ToArray())).ToArray(),
        _ => CaseRows,
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        string[] rows = Rows(Shape);
        return new Size(rows[0].Length * Pitch, rows.Length * Pitch);
    }

    protected override void OnRender(DrawingContext context)
    {
        double reveal = Reveal * (1 + WaveSoftness);
        double fill = Fill;
        double fullRadius = Pitch * DotRadiusRatio;

        foreach (Dot dot in Shapes[Shape])
        {
            double grown = Math.Clamp((reveal - dot.Wave) / WaveSoftness, 0, 1);
            if (grown <= 0)
                continue;

            // The front of the wave is lit whatever the charge, like a scan passing over the shape.
            Brush brush = grown < 1 || dot.Height <= fill ? DotBrush : EmptyBrush;
            double radius = fullRadius * dot.Size * grown;
            context.DrawEllipse(brush, null, new Point((dot.Column + 0.5) * Pitch, (dot.Row + 0.5) * Pitch), radius, radius);
        }
    }

    private static Dot[] Dots(string[] rows)
    {
        var dots = new List<Dot>();
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < rows[row].Length; column++)
            {
                char tone = rows[row][column];
                if (tone == '.')
                    continue;

                double down = (double)row / (rows.Length - 1);
                double across = (double)column / (rows[row].Length - 1);
                dots.Add(new Dot(
                    column,
                    row,
                    tone switch { '#' => 1.0, '+' => 0.78, _ => 0.62 },
                    down * 0.85 + across * 0.15,
                    (rows.Length - row - 0.5) / rows.Length));
            }
        }
        return dots.ToArray();
    }

    private readonly record struct Dot(int Column, int Row, double Size, double Wave, double Height);
}
