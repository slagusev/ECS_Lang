namespace ECSLang.Core;

public enum OptimizationLevel
{
    O0,
    O1,
    O2,
    O3,
    Os,
    Oz
}

public sealed class CompilerOptions
{
    public OptimizationLevel OptimizationLevel { get; set; } = OptimizationLevel.O0;
    public bool IsRelease { get; set; } = false;
    public bool GenerateDebugInfo { get; set; } = false;
    public bool NoWaitOnExit { get; set; } = false;

    public string GetPassPipelineString() => OptimizationLevel switch
    {
        OptimizationLevel.O0 => "default<O0>",
        OptimizationLevel.O1 => "default<O1>",
        OptimizationLevel.O2 => "default<O2>",
        OptimizationLevel.O3 => "default<O3>",
        OptimizationLevel.Os => "default<Os>",
        OptimizationLevel.Oz => "default<Oz>",
        _ => "default<O0>"
    };
}
