using ECSLang.Core;

namespace ECSLang.Toolchain;

public interface ILinker
{
    bool Link(string objFilePath, string outputExePath, CompilerOptions? options = null);
}
