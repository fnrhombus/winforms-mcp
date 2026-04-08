using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KitchenSink;

/// <summary>
/// A custom control that renders a gradient "mood ring" circle with a label.
/// </summary>
public class MoodRing : Control
{
    private Color _innerColor = Color.MediumPurple;
    private Color _outerColor = Color.DarkSlateBlue;
    private string _moodText = "Meh";

    public Color InnerColor
    {
        get => _innerColor;
        set { _innerColor = value; Invalidate(); }
    }

    public Color OuterColor
    {
        get => _outerColor;
        set { _outerColor = value; Invalidate(); }
    }

    public string MoodText
    {
        get => _moodText;
        set { _moodText = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(4, 4, Math.Min(Width, Height) - 8, Math.Min(Width, Height) - 8);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var path = new GraphicsPath();
        path.AddEllipse(rect);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = _innerColor,
            SurroundColors = new[] { _outerColor }
        };
        g.FillEllipse(brush, rect);

        using var pen = new Pen(Color.FromArgb(180, Color.Black), 2f);
        g.DrawEllipse(pen, rect);

        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        var textSize = g.MeasureString(_moodText, font);
        var textX = rect.X + (rect.Width - textSize.Width) / 2;
        var textY = rect.Bottom + 4;
        using var textBrush = new SolidBrush(ForeColor);
        g.DrawString(_moodText, font, textBrush, textX, textY);
    }
}
