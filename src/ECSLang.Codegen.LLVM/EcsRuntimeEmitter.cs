using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed partial class EcsRuntimeEmitter
{
    private readonly LLVMContextRef _context;
    private readonly LLVMModuleRef _module;
    private readonly LLVMBuilderRef _builder;
    private readonly TypeChecker _typeChecker;
    private readonly DiagnosticsBag _diagnostics;
    private readonly CompilerOptions _options;

    private readonly Dictionary<string, LLVMTypeRef> _compStructTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _compIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _compSizes = new(StringComparer.Ordinal);
    private readonly List<string> _orderedCompNames = new();
    private readonly Dictionary<string, int> _resourceWorldOffsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _eventWorldOffsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _eventSizes = new(StringComparer.Ordinal);

    // Multi-Archetype Structs
    private LLVMTypeRef _archStructType;
    private LLVMTypeRef _colArrayType;
    private LLVMTypeRef _worldStructType;
    private int _nameIndexWorldOffset = -1;
    private int _stringArenaHeadWorldOffset = -1;
    private int _stringArenaChunksWorldOffset = -1;

    public int NameIndexWorldOffset => _nameIndexWorldOffset;
    public int StringArenaHeadWorldOffset => _stringArenaHeadWorldOffset;
    public int StringArenaChunksWorldOffset => _stringArenaChunksWorldOffset;

    public EcsRuntimeEmitter(
        LLVMContextRef context,
        LLVMModuleRef module,
        LLVMBuilderRef builder,
        TypeChecker typeChecker,
        DiagnosticsBag diagnostics,
        CompilerOptions? options = null)
    {
        _context = context;
        _module = module;
        _builder = builder;
        _typeChecker = typeChecker;
        _diagnostics = diagnostics;
        _options = options ?? new CompilerOptions();
    }

    public LLVMTypeRef GetComponentStructType(string compName) =>
        _compStructTypes.TryGetValue(compName, out var t) ? t : _context.Int32Type;

    public int GetResourceOffset(string resName) =>
        _resourceWorldOffsets.TryGetValue(resName, out var off) ? off : -1;

    public int GetEventWorldOffset(string eventName) =>
        _eventWorldOffsets.TryGetValue(eventName, out var off) ? off : -1;

    public int GetComponentId(string compName) =>
        _compIds.TryGetValue(compName, out var id) ? id : -1;

    public ulong GetComponentMask(string compName)
    {
        int id = GetComponentId(compName);
        return (id >= 0 && id < 64) ? (1UL << id) : 0UL;
    }

    public LLVMTypeRef GetArchetypeStructType() => _archStructType;
    public LLVMTypeRef GetColumnsArrayType() => _colArrayType;
    public LLVMTypeRef GetWorldStructType() => _worldStructType;
    public LLVMTypeRef WorldStructType => _worldStructType;

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

        if (_typeChecker.Events.TryGetValue(compOrResName, out var ev))
        {
            for (int i = 0; i < ev.Fields.Count; i++)
            {
                if (ev.Fields[i].Name == fieldName)
                    return i;
            }
        }

        if (_typeChecker.Structs.TryGetValue(compOrResName, out var st))
        {
            for (int i = 0; i < st.Fields.Count; i++)
            {
                if (st.Fields[i].Name == fieldName)
                    return i;
            }
        }

        return 0;
    }

    public unsafe void EmitEcsDeclarations(LLVMTargetDataRef dataLayout)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var i32PtrType = LLVMTypeRef.CreatePointer(_context.Int32Type, 0);

        // 1. Assign Component IDs and create Struct Types in deterministic order:
        // "ChildOf" is always first (if present), followed by other components sorted by Ordinal name
        var orderedComps = _typeChecker.Components.Values
            .Distinct()
            .OrderBy(c => c.Name == "ChildOf" ? 0 : 1)
            .ThenBy(c => TypeSymbol.ToMonomorphizedIdentifier(c.Name), StringComparer.Ordinal)
            .ToList();

        int compIndex = 0;
        foreach (var compSym in orderedComps)
        {
            var compName = compSym.Name;
            if (_compStructTypes.ContainsKey(compName)) continue;
            var cleanName = TypeSymbol.ToMonomorphizedIdentifier(compName);
            if (_compStructTypes.TryGetValue(cleanName, out var existingType))
            {
                _compStructTypes[compName] = existingType;
                _compIds[compName] = _compIds[cleanName];
                _compSizes[compName] = _compSizes[cleanName];
                continue;
            }

            if (compIndex >= 64)
            {
                int totalCount = _typeChecker.Components.Values.Distinct().Count();
                _diagnostics.ReportError(
                    $"Maximum component limit of 64 exceeded (project has {totalCount} components, limit is 64). 64-bit archetype bitmask overflow.",
                    compSym.Span);
                return;
            }

            var fieldTypes = compSym.Fields.Select(f => MapType(f.Type.Name)).ToArray();
            var structType = _context.CreateNamedStruct($"struct.{cleanName}");
            structType.StructSetBody(fieldTypes, false);
            _compStructTypes[compName] = structType;
            _compStructTypes[cleanName] = structType;
            _compIds[compName] = compIndex;
            _compIds[cleanName] = compIndex;

            ulong sizeInBytes = LlvmApi.ABISizeOfType(dataLayout, structType);
            _compSizes[compName] = Math.Max(1, sizeInBytes);
            _compSizes[cleanName] = Math.Max(1, sizeInBytes);
            _orderedCompNames.Add(cleanName);
            compIndex++;
        }

        // 2. Struct types for all resources
        foreach (var (resName, resSym) in _typeChecker.Resources)
        {
            var fieldTypes = resSym.Fields.Select(f => MapType(f.Type.Name)).ToArray();
            var structType = _context.CreateNamedStruct($"struct.res.{resName}");
            structType.StructSetBody(fieldTypes, false);
            _compStructTypes[resName] = structType;
        }

        // 2.1 Struct types for all user structs
        foreach (var (stName, stSym) in _typeChecker.Structs)
        {
            var cleanName = TypeSymbol.ToMonomorphizedIdentifier(stName);
            if (!_compStructTypes.ContainsKey(stName))
            {
                var fieldTypes = stSym.Fields.Select(f => MapType(f.Type.Name)).ToArray();
                var structType = _context.CreateNamedStruct($"struct.user.{cleanName}");
                structType.StructSetBody(fieldTypes, false);
                _compStructTypes[stName] = structType;
                _compStructTypes[cleanName] = structType;
            }
        }

        // 2.2 Struct types for all events
        foreach (var (evName, evSym) in _typeChecker.Events)
        {
            var fieldTypes = evSym.Fields.Select(f => MapType(f.Type.Name)).ToArray();
            var structType = _context.CreateNamedStruct($"struct.event.{evName}");
            structType.StructSetBody(fieldTypes, false);
            _compStructTypes[evName] = structType;
            ulong sizeInBytes = LlvmApi.ABISizeOfType(dataLayout, structType);
            _eventSizes[evName] = Math.Max(1, sizeInBytes);
        }

        // 3. Define %struct.Archetype: { i64 mask, i32 count, i32 cap, ptr entities, [N x ptr] columns }
        uint compCount = (uint)Math.Max(1, _orderedCompNames.Count);
        _colArrayType = LLVMTypeRef.CreateArray(i8PtrType, compCount);

        _archStructType = _context.CreateNamedStruct("struct.Archetype");
        _archStructType.StructSetBody(new[]
        {
            _context.Int64Type, // 0: mask
            _context.Int32Type, // 1: count
            _context.Int32Type, // 2: cap
            i8PtrType,          // 3: entities (ptr to i32)
            _colArrayType       // 4: columns ([N x ptr])
        }, false);

        // 4. Define %struct.EcsWorld
        // Fields:
        // 0: arch_count (i32)
        // 1: arch_cap (i32)
        // 2: arch_tables (ptr to %struct.Archetype[])
        // 3: entity_count (i32)
        // 4: entity_cap (i32)
        // 5: entity_arch (ptr to i32[])
        // 6: entity_row (ptr to i32[])
        // 7: cmd_count (i32)
        // 8: cmd_cap (i32)
        // 9: cmd_data (ptr to i8)
        // 10: cmd_lock (ptr to i8, Win32 SRWLOCK)
        // 11+: embedded resource structs
        // followed by event buffers (6 fields per event type):
        // read_count, read_cap, read_data, write_count, write_cap, write_data
        var worldFields = new List<LLVMTypeRef>
        {
            _context.Int32Type,
            _context.Int32Type,
            LLVMTypeRef.CreatePointer(_archStructType, 0),
            _context.Int32Type,
            _context.Int32Type,
            i32PtrType,
            i32PtrType,
            _context.Int32Type,
            _context.Int32Type,
            i8PtrType,
            i8PtrType
        };

        int resOffset = 11;
        foreach (var (resName, _) in _typeChecker.Resources)
        {
            worldFields.Add(_compStructTypes[resName]);
            _resourceWorldOffsets[resName] = resOffset++;
        }

        foreach (var (evName, _) in _typeChecker.Events)
        {
            _eventWorldOffsets[evName] = worldFields.Count;
            worldFields.Add(_context.Int32Type); // read_count
            worldFields.Add(_context.Int32Type); // read_cap
            worldFields.Add(i8PtrType);          // read_data
            worldFields.Add(_context.Int32Type); // write_count
            worldFields.Add(_context.Int32Type); // write_cap
            worldFields.Add(i8PtrType);          // write_data
        }

        // Entity name index: HashMap<string, entity> (entries_ptr, count, cap)
        _nameIndexWorldOffset = worldFields.Count;
        worldFields.Add(i8PtrType);          // name_index_entries (ptr)
        worldFields.Add(_context.Int32Type); // name_index_count (i32)
        worldFields.Add(_context.Int32Type); // name_index_cap (i32)

        // String Arena in World:
        // string_arena_head: ptr to current active chunk (for bump-pointer allocation)
        // string_arena_chunks: ptr to head of all allocated chunks (for reset and free)
        _stringArenaHeadWorldOffset = worldFields.Count;
        worldFields.Add(i8PtrType);
        _stringArenaChunksWorldOffset = worldFields.Count;
        worldFields.Add(i8PtrType);

        _worldStructType = _context.CreateNamedStruct("struct.EcsWorld");
        _worldStructType.StructSetBody(worldFields.ToArray(), false);
    }

    private LLVMTypeRef MapType(string typeName)
    {
        if (typeName.StartsWith("[") && typeName.EndsWith("]"))
        {
            var inner = typeName.Substring(1, typeName.Length - 2);
            var parts = inner.Split(';');
            if (parts.Length == 2 && uint.TryParse(parts[1].Trim(), out uint len))
            {
                var elemType = MapType(parts[0].Trim());
                return LLVMTypeRef.CreateArray(elemType, len);
            }
            else
            {
                var elemType = MapType(inner.Trim());
                var elemPtrType = LLVMTypeRef.CreatePointer(elemType, 0);
                return _context.GetStructType(new[] { elemPtrType, _context.Int32Type, _context.Int32Type }, false);
            }
        }

        if ((typeName.StartsWith("Vec<") || typeName.StartsWith("List<")) && typeName.EndsWith(">"))
        {
            var inner = typeName.Substring(typeName.IndexOf('<') + 1, typeName.Length - typeName.IndexOf('<') - 2);
            var elemType = MapType(inner.Trim());
            var elemPtrType = LLVMTypeRef.CreatePointer(elemType, 0);
            return _context.GetStructType(new[] { elemPtrType, _context.Int32Type, _context.Int32Type }, false);
        }

        if ((typeName.StartsWith("Map<") || typeName.StartsWith("HashMap<")) && typeName.EndsWith(">"))
        {
            var i8Ptr = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
            return _context.GetStructType(new[] { i8Ptr, _context.Int32Type, _context.Int32Type }, false);
        }

        return typeName switch
        {
            "u8" or "i8" or "byte" => _context.Int8Type,
            "u16" or "i16" or "short" => _context.Int16Type,
            "f32" or "float" => _context.FloatType,
            "f64" or "double" => _context.DoubleType,
            "i64" or "u64" => _context.Int64Type,
            "i32" or "u32" or "int" => _context.Int32Type,
            "bool" => _context.Int1Type,
            "string" or "str" => LLVMTypeRef.CreatePointer(_context.Int8Type, 0),
            "str_view" => _context.GetStructType(new[] { LLVMTypeRef.CreatePointer(_context.Int8Type, 0), _context.Int32Type }, false),
            "World" or "world" => LLVMTypeRef.CreatePointer(_worldStructType, 0),
            "Commands" or "commands" => LLVMTypeRef.CreatePointer(_worldStructType, 0),
            "Entity" or "entity" => _context.Int32Type,
            _ => _compStructTypes.TryGetValue(typeName, out var st) ? st : _context.Int32Type
        };
    }
}
