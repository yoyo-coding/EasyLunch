namespace StartPage;

public enum TilePointerState
{
    Entered,
    Exited,
    Pressed
}

public static class TileHoverScale
{
    public static double GetScale(TilePointerState state) => state switch
    {
        TilePointerState.Entered => 1.06,
        TilePointerState.Pressed => 0.96,
        _ => 1.0
    };
}
