namespace TenMillionBlocks.Diagnostics;

public partial class StressBenchmarkController
{
    // Godot also exposes a type named Environment. Keep the benchmark call sites concise while making
    // it explicit that working-set memory comes from the .NET process, not the Godot scene environment.
    private static class Environment
    {
        public static long WorkingSet => System.Environment.WorkingSet;
    }
}
