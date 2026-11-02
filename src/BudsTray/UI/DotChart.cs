using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace BudsTray.UI;

/// <summary>Charge over time on a dot grid: one lit dot per column for each bud, on rows of 10%.</summary>
public sealed class DotChart : FrameworkElement
{
    public const int Columns = 74;

    private const int Rows = 11;
    private const double Pitch = 4;
    private const double DotRadius = 1.4;

    private (int? Left, int? Right)[] _data = new (int?, int?)[Columns];

    public Brush GridBrush { get; set; } = Brushes.DimGray;
    public Brush LeftBrush { get; set; } = Brushes.White;
    public Brush RightBrush { get; set; } = Brushes.Gray;

    public void SetData((int? Left, int? Right)[] data)
    {
        _data = data;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Columns * Pitch, Rows * Pitch);

    protected override void OnRender(DrawingContext context)
    {
        for (int column = 0; column < Columns; column++)
        {
            for (int row = 0; row < Rows; row++)
                Dot(context, GridBrush, column, row);
        }

        // The left bud last, so that it stays visible where the two agree.
        for (int column = 0; column < _data.Length && column < Columns; column++)
        {
            if (_data[column].Right is { } right)
                Dot(context, RightBrush, column, RowOf(right));
            if (_data[column].Left is { } left)
                Dot(context, LeftBrush, column, RowOf(left));
        }
    }

    private static int RowOf(int level) => (Rows - 1) - (int)System.Math.Round(System.Math.Clamp(level, 0, 100) / 10.0);

    private static void Dot(DrawingContext context, Brush brush, int column, int row) =>
        context.DrawEllipse(brush, null, new Point((column + 0.5) * Pitch, (row + 0.5) * Pitch), DotRadius, DotRadius);
}
