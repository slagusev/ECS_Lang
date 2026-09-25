using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;

namespace ECSLang.Codegen.LLVM;

public sealed class EcsRuntimeEmitter
{
    private readonly LLVMContextRef _context;
    private readonly LLVMModuleRef _module;
    private readonly LLVMBuilderRef _builder;
    private readonly TypeChecker _typeChecker;
    private readonly DiagnosticsBag _diagnostics;

    private readonly Dictionary<string, LLVMTypeRef> _compStructTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LLVMValueRef> _resourceGlobals = new(StringComparer.Ordinal);

    // Archetype storage info: for now, archetype of all declared components
    private LLVMValueRef _archCountGlobal;
    private LLVMValueRef _archCapGlobal;
    private readonly Dictionary<string, LLVMValueRef> _archColumnGlobals = new(StringComparer.Ordinal);

    public EcsRuntimeEmitter(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        TypeChecker typeChecker,
        DiagnosticsBag diagnostics)
    {
        _context = context;
        _module = module;
        _builder = builder;
        _typeChecker = typeChecker;
        _diagnostics = diagnostics;
    }

    public void EmitEcsDeclarations()
    {
        // 1. Emit struct types for all components
        foreach (var (compName, compSym) in _typeChecker.Components)
        {
            var fieldTypes = compSym.Fields.Select(f => MapType(f.Type.Name)).ToArray();
            var structType = _context.CreateNamedStruct($"struct.{compName}");
            structType.StructSetBody(fieldTypes, false);
            _compStructTypes[compName] = structType;
        }

        // 2. Emit global variables for all resources
        foreach (var (resName, resSym) in _typeChecker.Resources)
        {
            var fieldTypes = resSym.Fields.Select(f => MapType(f.Type.Name)).ToArray();
            var structType = _context.CreateNamedStruct($"struct.res.{resName}");
            structType.StructSetBody(fieldTypes, false);
            _compStructTypes[resName] = structType;

            var globalVar = _module.AddGlobal(structType, $"res_{resName}");
            globalVar.Initializer = LLVMValueRef.CreateConstNull(structType);
            _resourceGlobals[resName] = globalVar;
        }

        // 3. Emit Archetype Table storage globals (SoA)
        // Global count: i32
        _archCountGlobal = _module.AddGlobal(_context.Int32Type, "arch_count");
        _archCountGlobal.Initializer = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);

        // Global capacity: i32
        _archCapGlobal = _module.AddGlobal(_context.Int32Type, "arch_cap");
        _archCapGlobal.Initializer = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);

        // Global column pointers for each component: struct.Comp*
        foreach (var (compName, structType) in _compStructTypes)
        {
            if (_typeChecker.Components.ContainsKey(compName))
            {
                var ptrType = LLVMTypeRef.CreatePointer(structType, 0);
                var colGlobal = _module.AddGlobal(ptrType, $"arch_col_{compName}");
                colGlobal.Initializer = LLVMValueRef.CreateConstPointerNull(ptrType);
                _archColumnGlobals[compName] = colGlobal;
            }
        }
    }

    public LLVMTypeRef GetComponentStructType(string compName) =>
        _compStructTypes.TryGetValue(compName, out var t) ? t : _context.Int32Type;

    public LLVMValueRef GetResourceGlobal(string resName) =>
        _resourceGlobals.TryGetValue(resName, out var g) ? g : default;

    public LLVMValueRef GetArchetypeCountGlobal() => _archCountGlobal;
    public LLVMValueRef GetArchetypeCapGlobal() => _archCapGlobal;
    public LLVMValueRef GetArchetypeColGlobal(string compName) =>
        _archColumnGlobals.TryGetValue(compName, out var g) ? g : default;

    public int GetFieldOffset(string compOrResName, string fieldName)
    {
        if (_typeChecker.Components.TryGetValue(compOrResName, out var comp))
        {
            for (int i = 0; i < comp.Fields.Count; i++)
            {
                if (comp.Fields[i].Name == fieldName)
                    return i;
            }
        }

        if (_typeChecker.Resources.TryGetValue(compOrResName, out var res))
        {
            for (int i = 0; i < res.Fields.Count; i++)
            {
                if (res.Fields[i].Name == fieldName)
                    return i;
            }
        }

        return 0;
    }

    private LLVMTypeRef MapType(string typeName) => typeName switch
    {
        "f32" or "float" => _context.FloatType,
        "f64" or "double" => _context.DoubleType,
        "i64" or "u64" => _context.Int64Type,
        "i32" or "u32" or "int" => _context.Int32Type,
        "bool" => _context.Int1Type,
        "string" or "str" => LLVMTypeRef.CreatePointer(_context.Int8Type, 0),
        _ => _context.Int32Type
    };
}
