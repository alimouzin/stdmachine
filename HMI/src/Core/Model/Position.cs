namespace Core.Model;

/// <summary>Position : toujours X, Y, Z, C (rotation Rz) et un repère de référence.</summary>
public readonly record struct Position(double X, double Y, double Z, double C, string Frame);
