namespace DotCarbon.Plugins.Positioner;

/// <summary>
/// Move a window to a named position. <c>Position</c> is one of TopLeft, TopRight, BottomLeft,
/// BottomRight, TopCenter, BottomCenter, LeftCenter, RightCenter, Center (matched case- and
/// separator-insensitively). <c>Label</c> targets a specific window; null uses the current one.
/// </summary>
public record PositionerMoveArgs(string Position, string? Label = null);
