using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// MouseController:
/// - 盢 (x,y)  screen pixel 虫锣传Θ╰参菲公竚 (Windows)
/// - 硂穦钡北╰参村夹 (猔種)
/// </summary>
public static class MouseController
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    public static void SetMousePositionScreen(Vector2 screenPos)
    {
        // screenPos: (0..screenWidth, 0..screenHeight) with origin at top-left
        int X = Mathf.RoundToInt(screenPos.x);
        int Y = Mathf.RoundToInt(screenPos.y);
        SetCursorPos(X, Y);
    }

    public static Vector2 GetMousePositionScreen()
    {
        POINT p;
        GetCursorPos(out p);
        return new Vector2(p.X, p.Y);
    }
#else
    public static void SetMousePositionScreen(Vector2 screenPos)
    {
        Debug.LogWarning("SetMousePositionScreen not implemented for this platform.");
    }
    public static Vector2 GetMousePositionScreen() { return Vector2.zero; }
#endif
}
