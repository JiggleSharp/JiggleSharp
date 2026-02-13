namespace JiggleSharp.Core.Input;

public interface IPathGenerator
{
    IReadOnlyList<PathPoint> GeneratePath(double targetX, double targetY, Random rng, Engine.JiggleOptions options);
}