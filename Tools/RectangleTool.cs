using System;
using LaserOne.Models;

namespace LaserOne.Tools;

public class RectangleTool
{
    private enum State
    {
        WaitingForFirstCorner,
        WaitingForSecondCorner
    }

    private State _currentState = State.WaitingForFirstCorner;
    private Point2D _firstCorner;

    /// <summary>
    /// Indica se lo strumento è in fase di disegno (dopo il primo clic).
    /// </summary>
    public bool IsDrawing => _currentState == State.WaitingForSecondCorner;

    /// <summary>
    /// Ritorna il primo angolo fissato, utile per l'anteprima temporanea.
    /// </summary>
    public Point2D FirstCorner => _firstCorner;

    public void OnMouseDown(Point2D clickPosition, Action<RectangleShape> onRectangleCreated)
    {
        if (_currentState == State.WaitingForFirstCorner)
        {
            _firstCorner = clickPosition;
            _currentState = State.WaitingForSecondCorner;
        }
        else
        {
            var rect = CreateRectangle(_firstCorner, clickPosition);
            onRectangleCreated?.Invoke(rect);
            _currentState = State.WaitingForFirstCorner;
        }
    }

    public void Reset()
    {
        _currentState = State.WaitingForFirstCorner;
    }

    public static RectangleShape CreateRectangle(Point2D p1, Point2D p2)
    {
        double xMin = Math.Min(p1.X, p2.X);
        double xMax = Math.Max(p1.X, p2.X);
        double yMin = Math.Min(p1.Y, p2.Y);
        double yMax = Math.Max(p1.Y, p2.Y);

        return new RectangleShape(xMin, yMin, xMax - xMin, yMax - yMin);
    }
}