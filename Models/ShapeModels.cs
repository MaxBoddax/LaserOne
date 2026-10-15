using System;

namespace LaserOne.Models;

public abstract class ShapeModel
{
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsSelected { get; set; }

    protected ShapeModel() { }

    protected ShapeModel(double x, double y)
    {
        X = x;
        Y = y;
    }
}

public class RectangleShape : ShapeModel
{
    public double Width { get; set; }
    public double Height { get; set; }

    public RectangleShape() : base() { }

    public RectangleShape(double x, double y, double width, double height) : base(x, y)
    {
        Width = width;
        Height = height;
    }
}

public class LineShape : ShapeModel
{
    public double X2 { get; set; }
    public double Y2 { get; set; }

    public LineShape() : base() { }

    public LineShape(double x, double y, double x2, double y2) : base(x, y)
    {
        X2 = x2;
        Y2 = y2;
    }
}

public class CircleShape : ShapeModel
{
    public double Radius { get; set; }

    public CircleShape() : base() { }

    public CircleShape(double x, double y, double radius) : base(x, y)
    {
        Radius = radius;
    }
}