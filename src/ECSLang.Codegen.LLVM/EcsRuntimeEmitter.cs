using System.Runtime.InteropServices;
using ECSLang.Core;
using ECSLang.Core.AST;
using ECSLang.Semantics;
using LLVMSharp.Interop;
using LlvmApi = LLVMSharp.Interop.LLVM;

namespace ECSLang.Codegen.LLVM;

public sealed class EcsRuntimeEmitter
{
    private readonly LLVMContextRef _context;
    private readonly LLVMModuleRef _module;
    private readonly LLVMBuilderRef _builder;
    private readonly TypeChecker _typeChecker;
    private readonly DiagnosticsBag _diagnostics;

    private readonly Dictionary<string, LLVMTypeRef> _compStructTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _compIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _compSizes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _resourceWorldOffsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _eventWorldOffsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _eventSizes = new(StringComparer.Ordinal);

    // Multi-Archetype Structs
    private LLVMTypeRef _archStructType;
    private LLVMTypeRef _colArrayType;
    private LLVMTypeRef _worldStructType;

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
        return id >= 0 ? 1UL << id : 0UL;
    }

    public LLVMTypeRef GetArchetypeStructType() => _archStructType;
    public LLVMTypeRef GetColumnsArrayType() => _colArrayType;
    public LLVMTypeRef GetWorldStructType() => _worldStructType;

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

        // 1. Assign Component IDs and create Struct Types
        int compIndex = 0;
        foreach (var (compName, compSym) in _typeChecker.Components)
        {
            var fieldTypes = compSym.Fields.Select(f => MapType(f.Type.Name)).ToArray();
            var structType = _context.CreateNamedStruct($"struct.{compName}");
            structType.StructSetBody(fieldTypes, false);
            _compStructTypes[compName] = structType;
            _compIds[compName] = compIndex++;

            ulong sizeInBytes = LlvmApi.ABISizeOfType(dataLayout, structType);
            _compSizes[compName] = Math.Max(1, sizeInBytes);
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
            if (!_compStructTypes.ContainsKey(stName))
            {
                var fieldTypes = stSym.Fields.Select(f => MapType(f.Type.Name)).ToArray();
                var structType = _context.CreateNamedStruct($"struct.user.{stName}");
                structType.StructSetBody(fieldTypes, false);
                _compStructTypes[stName] = structType;
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
        uint compCount = (uint)Math.Max(1, _typeChecker.Components.Count);
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
        // 7+: embedded resource structs
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
            i32PtrType
        };

        int resOffset = 7;
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

        _worldStructType = _context.CreateNamedStruct("struct.EcsWorld");
        _worldStructType.StructSetBody(worldFields.ToArray(), false);
    }

    public unsafe void EmitMultiArchetypeRuntime(
        LLVMTargetDataRef dataLayout,
        LLVMTypeRef reallocType,
        LLVMValueRef reallocFunc,
        LLVMTypeRef memcpyType,
        LLVMValueRef memcpyFunc,
        LLVMTypeRef memsetType,
        LLVMValueRef memsetFunc,
        LLVMTypeRef mallocType,
        LLVMValueRef mallocFunc,
        LLVMTypeRef freeType,
        LLVMValueRef freeFunc)
    {
        var i8PtrType = LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        var i32PtrType = LLVMTypeRef.CreatePointer(_context.Int32Type, 0);
        var archPtrType = LLVMTypeRef.CreatePointer(_archStructType, 0);
        var worldPtrType = LLVMTypeRef.CreatePointer(_worldStructType, 0);

        ulong archStructSize = Math.Max(8, LlvmApi.ABISizeOfType(dataLayout, _archStructType));
        var archStructSizeVal = LLVMValueRef.CreateConstInt(_context.Int64Type, archStructSize);

        ulong worldStructSize = Math.Max(64, LlvmApi.ABISizeOfType(dataLayout, _worldStructType));
        var worldStructSizeVal = LLVMValueRef.CreateConstInt(_context.Int64Type, worldStructSize);

        var compNames = _typeChecker.Components.Keys.ToList();
        int totalComps = compNames.Count;

        // =========================================================================
        // Helper: world_get_or_create_archetype(ptr world, i64 mask) -> i32 arch_idx
        // =========================================================================
        var getArchType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { worldPtrType, _context.Int64Type }, false);
        var getArchFunc = _module.AddFunction("world_get_or_create_archetype", getArchType);
        var gEntryBB = getArchFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(gEntryBB);

        var worldParamG = getArchFunc.GetParam(0);
        var targetMask = getArchFunc.GetParam(1);

        var archCountSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, 0, "arch_count_slot");
        var archCapSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, 1, "arch_cap_slot");
        var archTablesSlot = _builder.BuildStructGEP2(_worldStructType, worldParamG, 2, "arch_tables_slot");

        var curCount = _builder.BuildLoad2(_context.Int32Type, archCountSlot, "cur_count");
        var iAlloca = _builder.BuildAlloca(_context.Int32Type, "i");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), iAlloca);

        var sCondBB = getArchFunc.AppendBasicBlock("search_cond");
        var sBodyBB = getArchFunc.AppendBasicBlock("search_body");
        var sNotFoundBB = getArchFunc.AppendBasicBlock("not_found");
        var sRetBB = getArchFunc.AppendBasicBlock("return_found");

        _builder.BuildBr(sCondBB);

        // Search loop
        _builder.PositionAtEnd(sCondBB);
        var curI = _builder.BuildLoad2(_context.Int32Type, iAlloca, "cur_i");
        var hasMore = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curI, curCount, "has_more");
        _builder.BuildCondBr(hasMore, sBodyBB, sNotFoundBB);

        _builder.PositionAtEnd(sBodyBB);
        var tablesBase = _builder.BuildLoad2(archPtrType, archTablesSlot, "tables_base");
        var existingArchPtr = _builder.BuildInBoundsGEP2(_archStructType, tablesBase, new[] { curI }, "arch_elem");
        var maskGEP = _builder.BuildStructGEP2(_archStructType, existingArchPtr, 0, "mask_gep");
        var existingMask = _builder.BuildLoad2(_context.Int64Type, maskGEP, "existing_mask");
        var isMatch = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, existingMask, targetMask, "is_match");

        var sNextBB = getArchFunc.AppendBasicBlock("search_next");
        _builder.BuildCondBr(isMatch, sRetBB, sNextBB);

        _builder.PositionAtEnd(sNextBB);
        var nextI = _builder.BuildAdd(curI, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_i");
        _builder.BuildStore(nextI, iAlloca);
        _builder.BuildBr(sCondBB);

        _builder.PositionAtEnd(sRetBB);
        _builder.BuildRet(curI);

        // Not found: allocate new archetype
        _builder.PositionAtEnd(sNotFoundBB);
        var curCap = _builder.BuildLoad2(_context.Int32Type, archCapSlot, "cur_cap");
        var needGrow = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curCount, curCap, "need_grow");
        var growBB = getArchFunc.AppendBasicBlock("grow_tables");
        var initArchBB = getArchFunc.AppendBasicBlock("init_arch");

        _builder.BuildCondBr(needGrow, growBB, initArchBB);

        _builder.PositionAtEnd(growBB);
        var capIsZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "cap_zero");
        var doubleCap = _builder.BuildMul(curCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_cap");
        var newCap = _builder.BuildSelect(capIsZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 8), doubleCap, "new_cap");
        _builder.BuildStore(newCap, archCapSlot);

        var newCap64 = _builder.BuildZExt(newCap, _context.Int64Type, "new_cap64");
        var allocBytes = _builder.BuildMul(newCap64, archStructSizeVal, "alloc_bytes");
        var curTablesRaw = _builder.BuildLoad2(archPtrType, archTablesSlot, "cur_tables_raw");
        var curTablesI8 = _builder.BuildBitCast(curTablesRaw, i8PtrType, "cur_tables_i8");
        var reallocCall = _builder.BuildCall2(reallocType, reallocFunc, new[] { curTablesI8, allocBytes }, "new_tables_i8");
        var newTablesTyped = _builder.BuildBitCast(reallocCall, archPtrType, "new_tables_typed");
        _builder.BuildStore(newTablesTyped, archTablesSlot);
        _builder.BuildBr(initArchBB);

        _builder.PositionAtEnd(initArchBB);
        var newIdx = curCount;
        var nextCount = _builder.BuildAdd(curCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_count");
        _builder.BuildStore(nextCount, archCountSlot);

        var latestTables = _builder.BuildLoad2(archPtrType, archTablesSlot, "latest_tables");
        var newArchElem = _builder.BuildInBoundsGEP2(_archStructType, latestTables, new[] { newIdx }, "new_arch_elem");

        // Set mask
        var newMaskGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 0, "m_gep");
        _builder.BuildStore(targetMask, newMaskGEP);

        // Set count = 0
        var newCountGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 1, "cnt_gep");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), newCountGEP);

        // Set cap = 0
        var newCapGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 2, "cap_gep");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), newCapGEP);

        // Set entities = null
        var newEntGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 3, "ent_gep");
        _builder.BuildStore(LLVMValueRef.CreateConstPointerNull(i8PtrType), newEntGEP);

        // Set columns = null
        var colsArrGEP = _builder.BuildStructGEP2(_archStructType, newArchElem, 4, "cols_gep");
        for (int k = 0; k < totalComps; k++)
        {
            var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, colsArrGEP, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k)
            }, $"col_slot_{k}");
            _builder.BuildStore(LLVMValueRef.CreateConstPointerNull(i8PtrType), colSlot);
        }

        _builder.BuildRet(newIdx);

        // =========================================================================
        // Helper: world_grow_archetype(ptr world, i32 arch_idx) -> void
        // =========================================================================
        var growArchType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
        var growArchFunc = _module.AddFunction("world_grow_archetype", growArchType);
        var grEntryBB = growArchFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(grEntryBB);

        var worldParamGr = growArchFunc.GetParam(0);
        var archIdxParam = growArchFunc.GetParam(1);

        var tablesSlotGr = _builder.BuildStructGEP2(_worldStructType, worldParamGr, 2, "tables_slot_gr");
        var tablesBaseGr = _builder.BuildLoad2(archPtrType, tablesSlotGr, "tables_base");
        var archElemGr = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseGr, new[] { archIdxParam }, "arch_elem");

        var capSlotGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 2, "cap_slot");
        var curArchCap = _builder.BuildLoad2(_context.Int32Type, capSlotGr, "cur_cap");
        var archCapZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curArchCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_zero");
        var doubleArchCap = _builder.BuildMul(curArchCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_cap");
        var newArchCap = _builder.BuildSelect(archCapZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 32), doubleArchCap, "new_arch_cap");
        _builder.BuildStore(newArchCap, capSlotGr);

        var newArchCap64 = _builder.BuildZExt(newArchCap, _context.Int64Type, "new_cap64");

        // Reallocate entities array (i32*): newArchCap * 4 bytes
        var entSlotGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 3, "ent_slot");
        var curEntRaw = _builder.BuildLoad2(i8PtrType, entSlotGr, "cur_ent_raw");
        var entBytes = _builder.BuildMul(newArchCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, 4), "ent_bytes");
        var newEntRaw = _builder.BuildCall2(reallocType, reallocFunc, new[] { curEntRaw, entBytes }, "new_ent_raw");
        _builder.BuildStore(newEntRaw, entSlotGr);

        // Reallocate active component columns
        var maskSlotGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 0, "mask_slot");
        var archMaskGr = _builder.BuildLoad2(_context.Int64Type, maskSlotGr, "arch_mask");
        var colsArrGr = _builder.BuildStructGEP2(_archStructType, archElemGr, 4, "cols_arr");

        for (int k = 0; k < totalComps; k++)
        {
            var compName = compNames[k];
            ulong compSize = _compSizes[compName];
            ulong compBit = 1UL << k;

            var bitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, compBit);
            var andRes = _builder.BuildAnd(archMaskGr, bitVal, $"has_{compName}");
            var hasComp = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, andRes, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_has_{compName}");

            var growColBB = growArchFunc.AppendBasicBlock($"grow_col_{compName}");
            var skipColBB = growArchFunc.AppendBasicBlock($"skip_col_{compName}");

            _builder.BuildCondBr(hasComp, growColBB, skipColBB);

            _builder.PositionAtEnd(growColBB);
            var colSlotK = _builder.BuildInBoundsGEP2(_colArrayType, colsArrGr, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k)
            }, $"col_slot_{compName}");
            var curColRaw = _builder.BuildLoad2(i8PtrType, colSlotK, $"cur_col_{compName}");
            var colBytes = _builder.BuildMul(newArchCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, compSize), $"col_bytes_{compName}");
            var newColRaw = _builder.BuildCall2(reallocType, reallocFunc, new[] { curColRaw, colBytes }, $"new_col_{compName}");
            _builder.BuildStore(newColRaw, colSlotK);
            _builder.BuildBr(skipColBB);

            _builder.PositionAtEnd(skipColBB);
        }

        _builder.BuildRetVoid();

        // =========================================================================
        // Factory: ecs_create_world() -> ptr
        // =========================================================================
        var createWorldType = LLVMTypeRef.CreateFunction(worldPtrType, Array.Empty<LLVMTypeRef>(), false);
        var createWorldFunc = _module.AddFunction("ecs_create_world", createWorldType);
        var cwEntryBB = createWorldFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(cwEntryBB);

        var rawWorld = _builder.BuildCall2(mallocType, mallocFunc, new[] { worldStructSizeVal }, "raw_world");
        _builder.BuildCall2(memsetType, memsetFunc, new[] { rawWorld, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), worldStructSizeVal }, "");
        var typedWorld = _builder.BuildBitCast(rawWorld, worldPtrType, "typed_world");

        // Initialize empty Archetype 0 in this world
        _builder.BuildCall2(getArchType, getArchFunc, new[] { typedWorld, LLVMValueRef.CreateConstInt(_context.Int64Type, 0) }, "a0_init");
        _builder.BuildRet(typedWorld);

        // =========================================================================
        // Helper: world_spawn(ptr world) -> i32 entity_id
        // =========================================================================
        var spawnType = LLVMTypeRef.CreateFunction(_context.Int32Type, new[] { worldPtrType }, false);
        var spawnFunc = _module.AddFunction("world_spawn", spawnType);
        var spEntryBB = spawnFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(spEntryBB);

        var worldParamSp = spawnFunc.GetParam(0);
        var entCountSlotSp = _builder.BuildStructGEP2(_worldStructType, worldParamSp, 3, "ent_count_slot");
        var entCapSlotSp = _builder.BuildStructGEP2(_worldStructType, worldParamSp, 4, "ent_cap_slot");
        var entArchSlotSp = _builder.BuildStructGEP2(_worldStructType, worldParamSp, 5, "ent_arch_slot");
        var entRowSlotSp = _builder.BuildStructGEP2(_worldStructType, worldParamSp, 6, "ent_row_slot");

        var curEntCount = _builder.BuildLoad2(_context.Int32Type, entCountSlotSp, "cur_ent_count");
        var curEntCap = _builder.BuildLoad2(_context.Int32Type, entCapSlotSp, "cur_ent_cap");
        var needGrowEnt = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curEntCount, curEntCap, "need_grow_ent");

        var growEntBB = spawnFunc.AppendBasicBlock("grow_ent");
        var assignEntBB = spawnFunc.AppendBasicBlock("assign_ent");

        _builder.BuildCondBr(needGrowEnt, growEntBB, assignEntBB);

        _builder.PositionAtEnd(growEntBB);
        var entCapZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curEntCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "ent_cap_zero");
        var doubleEntCap = _builder.BuildMul(curEntCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "double_ent_cap");
        var newEntCap = _builder.BuildSelect(entCapZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 64), doubleEntCap, "new_ent_cap");
        _builder.BuildStore(newEntCap, entCapSlotSp);

        var newEntCap64 = _builder.BuildZExt(newEntCap, _context.Int64Type, "new_ent_cap64");
        var bytesForEnt = _builder.BuildMul(newEntCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, 4), "bytes_for_ent");

        // Realloc entity_arch
        var curArchArrRaw = _builder.BuildLoad2(i32PtrType, entArchSlotSp, "cur_arch_arr");
        var curArchArrI8 = _builder.BuildBitCast(curArchArrRaw, i8PtrType, "cur_arch_i8");
        var newArchArrI8 = _builder.BuildCall2(reallocType, reallocFunc, new[] { curArchArrI8, bytesForEnt }, "new_arch_i8");
        var newArchArrTyped = _builder.BuildBitCast(newArchArrI8, i32PtrType, "new_arch_typed");
        _builder.BuildStore(newArchArrTyped, entArchSlotSp);

        // Realloc entity_row
        var curRowArrRaw = _builder.BuildLoad2(i32PtrType, entRowSlotSp, "cur_row_arr");
        var curRowArrI8 = _builder.BuildBitCast(curRowArrRaw, i8PtrType, "cur_row_i8");
        var newRowArrI8 = _builder.BuildCall2(reallocType, reallocFunc, new[] { curRowArrI8, bytesForEnt }, "new_row_i8");
        var newRowArrTyped = _builder.BuildBitCast(newRowArrI8, i32PtrType, "new_row_typed");
        _builder.BuildStore(newRowArrTyped, entRowSlotSp);

        _builder.BuildBr(assignEntBB);

        _builder.PositionAtEnd(assignEntBB);
        var e = curEntCount;
        var nextEntCountVal = _builder.BuildAdd(curEntCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_ent_cnt");
        _builder.BuildStore(nextEntCountVal, entCountSlotSp);

        // Add to Archetype 0 (empty archetype, mask 0)
        var a0 = _builder.BuildCall2(getArchType, getArchFunc, new[] { worldParamSp, LLVMValueRef.CreateConstInt(_context.Int64Type, 0) }, "a0");

        var tablesSlotSp = _builder.BuildStructGEP2(_worldStructType, worldParamSp, 2, "tables_slot_sp");
        var tablesBaseSp = _builder.BuildLoad2(archPtrType, tablesSlotSp, "tables_sp");
        var a0Ptr = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseSp, new[] { a0 }, "a0_ptr");
        var cntSlot0 = _builder.BuildStructGEP2(_archStructType, a0Ptr, 1, "cnt_slot0");
        var curCount0 = _builder.BuildLoad2(_context.Int32Type, cntSlot0, "cur_cnt0");
        var capSlot0 = _builder.BuildStructGEP2(_archStructType, a0Ptr, 2, "cap_slot0");
        var curCap0 = _builder.BuildLoad2(_context.Int32Type, capSlot0, "cur_cap0");
        var needGrow0 = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curCount0, curCap0, "need_grow0");

        var grow0BB = spawnFunc.AppendBasicBlock("grow_a0");
        var afterGrow0BB = spawnFunc.AppendBasicBlock("after_grow_a0");
        _builder.BuildCondBr(needGrow0, grow0BB, afterGrow0BB);

        _builder.PositionAtEnd(grow0BB);
        _builder.BuildCall2(growArchType, growArchFunc, new[] { worldParamSp, a0 }, "");
        _builder.BuildBr(afterGrow0BB);

        _builder.PositionAtEnd(afterGrow0BB);
        var tablesBaseSp2 = _builder.BuildLoad2(archPtrType, tablesSlotSp, "tables_sp2");
        var a0Ptr2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseSp2, new[] { a0 }, "a0_ptr2");
        var cntSlot0_2 = _builder.BuildStructGEP2(_archStructType, a0Ptr2, 1, "cnt_slot0_2");
        var row = _builder.BuildLoad2(_context.Int32Type, cntSlot0_2, "row");
        var nextCount0 = _builder.BuildAdd(row, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_cnt0");
        _builder.BuildStore(nextCount0, cntSlot0_2);

        // a0.entities[row] = e
        var entSlot0 = _builder.BuildStructGEP2(_archStructType, a0Ptr2, 3, "ent_slot0");
        var entRaw0 = _builder.BuildLoad2(i8PtrType, entSlot0, "ent_raw0");
        var entTyped0 = _builder.BuildBitCast(entRaw0, i32PtrType, "ent_typed0");
        var entElem0 = _builder.BuildInBoundsGEP2(_context.Int32Type, entTyped0, new[] { row }, "ent_elem0");
        _builder.BuildStore(e, entElem0);

        // world.entity_arch[e] = a0
        var archArrSp = _builder.BuildLoad2(i32PtrType, entArchSlotSp, "arch_arr_sp");
        var eArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrSp, new[] { e }, "e_arch_slot");
        _builder.BuildStore(a0, eArchSlot);

        // world.entity_row[e] = row
        var rowArrSp = _builder.BuildLoad2(i32PtrType, entRowSlotSp, "row_arr_sp");
        var eRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSp, new[] { e }, "e_row_slot");
        _builder.BuildStore(row, eRowSlot);

        _builder.BuildRet(e);

        // =========================================================================
        // Helpers for each component: world_set_Comp / world_add_Comp / world_remove_Comp / world_has_Comp
        // =========================================================================
        for (int k = 0; k < totalComps; k++)
        {
            var compName = compNames[k];
            var compSym = _typeChecker.Components[compName];
            var compStructType = _compStructTypes[compName];
            ulong compSize = _compSizes[compName];
            ulong compBit = 1UL << k;
            var compBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, compBit);

            // 1. world_set_Comp and world_add_Comp
            foreach (var prefix in new[] { "world_set_", "world_add_" })
            {
                var paramTypes = new List<LLVMTypeRef> { worldPtrType, _context.Int32Type };
                foreach (var f in compSym.Fields)
                {
                    paramTypes.Add(MapType(f.Type.Name));
                }

                var setFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, paramTypes.ToArray(), false);
                var setFunc = _module.AddFunction($"{prefix}{compName}", setFuncType);
                var setEntryBB = setFunc.AppendBasicBlock("entry");
                _builder.PositionAtEnd(setEntryBB);

                var targetArchAlloca = _builder.BuildAlloca(_context.Int32Type, "target_arch");
                var targetRowAlloca = _builder.BuildAlloca(_context.Int32Type, "target_row");

                var worldParamSet = setFunc.GetParam(0);
                var eParam = setFunc.GetParam(1);

                var entArchSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 5, "ent_arch_slot_set");
                var entRowSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 6, "ent_row_slot_set");
                var tablesSlotSet = _builder.BuildStructGEP2(_worldStructType, worldParamSet, 2, "tables_slot_set");

                // Load entity current archetype & row
                var archArr = _builder.BuildLoad2(i32PtrType, entArchSlotSet, "arch_arr");
                var entArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArr, new[] { eParam }, "ent_arch_slot");
                var curArchIdx = _builder.BuildLoad2(_context.Int32Type, entArchSlot, "cur_arch_idx");

                var rowArr = _builder.BuildLoad2(i32PtrType, entRowSlotSet, "row_arr");
                var entRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArr, new[] { eParam }, "ent_row_slot");
                var curRow = _builder.BuildLoad2(_context.Int32Type, entRowSlot, "cur_row");

                var tablesBaseSet = _builder.BuildLoad2(archPtrType, tablesSlotSet, "tables_set");
                var curArchPtr = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseSet, new[] { curArchIdx }, "cur_arch_ptr");

                var curMaskSlot = _builder.BuildStructGEP2(_archStructType, curArchPtr, 0, "cur_mask_slot");
                var curMask = _builder.BuildLoad2(_context.Int64Type, curMaskSlot, "cur_mask");

                var hasBit = _builder.BuildAnd(curMask, compBitVal, "has_bit");
                var alreadyHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, hasBit, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "already_has");

                var inPlaceBB = setFunc.AppendBasicBlock("in_place_update");
                var transBB = setFunc.AppendBasicBlock("transition");
                var storeFieldsBB = setFunc.AppendBasicBlock("store_fields");

                _builder.BuildCondBr(alreadyHas, inPlaceBB, transBB);

                // --- In place update ---
                _builder.PositionAtEnd(inPlaceBB);
                _builder.BuildStore(curArchIdx, targetArchAlloca);
                _builder.BuildStore(curRow, targetRowAlloca);
                _builder.BuildBr(storeFieldsBB);

                // --- Transition to new archetype ---
                _builder.PositionAtEnd(transBB);
                var newMask = _builder.BuildOr(curMask, compBitVal, "new_mask");
                var newArchIdx = _builder.BuildCall2(getArchType, getArchFunc, new[] { worldParamSet, newMask }, "new_arch_idx");

                var tablesBaseTr1 = _builder.BuildLoad2(archPtrType, tablesSlotSet, "tables_tr1");
                var newArchPtr1 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseTr1, new[] { newArchIdx }, "new_arch_ptr1");
                var newCntSlot1 = _builder.BuildStructGEP2(_archStructType, newArchPtr1, 1, "new_cnt_slot1");
                var newCnt1 = _builder.BuildLoad2(_context.Int32Type, newCntSlot1, "new_cnt1");
                var newCapSlot1 = _builder.BuildStructGEP2(_archStructType, newArchPtr1, 2, "new_cap_slot1");
                var newCap1 = _builder.BuildLoad2(_context.Int32Type, newCapSlot1, "new_cap1");
                var needGrowNew = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, newCnt1, newCap1, "need_grow_new");

                var growNewBB = setFunc.AppendBasicBlock("grow_new_arch");
                var afterGrowNewBB = setFunc.AppendBasicBlock("after_grow_new_arch");
                _builder.BuildCondBr(needGrowNew, growNewBB, afterGrowNewBB);

                _builder.PositionAtEnd(growNewBB);
                _builder.BuildCall2(growArchType, growArchFunc, new[] { worldParamSet, newArchIdx }, "");
                _builder.BuildBr(afterGrowNewBB);

                _builder.PositionAtEnd(afterGrowNewBB);
                // Reload pointers
                var tablesBaseTr2 = _builder.BuildLoad2(archPtrType, tablesSlotSet, "tables_tr2");
                var curArchPtr2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseTr2, new[] { curArchIdx }, "cur_arch_ptr2");
                var newArchPtr2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseTr2, new[] { newArchIdx }, "new_arch_ptr2");

                var newCntSlot2 = _builder.BuildStructGEP2(_archStructType, newArchPtr2, 1, "new_cnt_slot2");
                var newRow = _builder.BuildLoad2(_context.Int32Type, newCntSlot2, "new_row");
                var nextNewCnt = _builder.BuildAdd(newRow, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_new_cnt");
                _builder.BuildStore(nextNewCnt, newCntSlot2);

                // Store e into newArch.entities[newRow]
                var newEntSlot2 = _builder.BuildStructGEP2(_archStructType, newArchPtr2, 3, "new_ent_slot2");
                var newEntRaw2 = _builder.BuildLoad2(i8PtrType, newEntSlot2, "new_ent_raw2");
                var newEntTyped2 = _builder.BuildBitCast(newEntRaw2, i32PtrType, "new_ent_typed2");
                var newEntElem2 = _builder.BuildInBoundsGEP2(_context.Int32Type, newEntTyped2, new[] { newRow }, "new_ent_elem2");
                _builder.BuildStore(eParam, newEntElem2);

                // Copy all existing components from curArch[curRow] to newArch[newRow]
                var curColsArr = _builder.BuildStructGEP2(_archStructType, curArchPtr2, 4, "cur_cols_arr");
                var newColsArr = _builder.BuildStructGEP2(_archStructType, newArchPtr2, 4, "new_cols_arr");

                for (int c = 0; c < totalComps; c++)
                {
                    var cName = compNames[c];
                    ulong cBit = 1UL << c;
                    ulong cSize = _compSizes[cName];
                    var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                    var cAnd = _builder.BuildAnd(curMask, cBitVal, $"has_{cName}");
                    var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_has_{cName}");

                    var copyCBB = setFunc.AppendBasicBlock($"copy_{cName}");
                    var skipCBB = setFunc.AppendBasicBlock($"skip_{cName}");
                    _builder.BuildCondBr(hasC, copyCBB, skipCBB);

                    _builder.PositionAtEnd(copyCBB);
                    var srcColSlot = _builder.BuildInBoundsGEP2(_colArrayType, curColsArr, new[]
                    {
                        LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                        LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                    }, $"src_col_{cName}");
                    var srcColRaw = _builder.BuildLoad2(i8PtrType, srcColSlot, $"src_raw_{cName}");
                    var srcCompTyped = _builder.BuildBitCast(srcColRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"src_typed_{cName}");
                    var srcElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], srcCompTyped, new[] { curRow }, $"src_elem_{cName}");
                    var srcElemI8 = _builder.BuildBitCast(srcElem, i8PtrType, $"src_i8_{cName}");

                    var dstColSlot = _builder.BuildInBoundsGEP2(_colArrayType, newColsArr, new[]
                    {
                        LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                        LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                    }, $"dst_col_{cName}");
                    var dstColRaw = _builder.BuildLoad2(i8PtrType, dstColSlot, $"dst_raw_{cName}");
                    var dstCompTyped = _builder.BuildBitCast(dstColRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"dst_typed_{cName}");
                    var dstElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], dstCompTyped, new[] { newRow }, $"dst_elem_{cName}");
                    var dstElemI8 = _builder.BuildBitCast(dstElem, i8PtrType, $"dst_i8_{cName}");

                    _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstElemI8, srcElemI8, LLVMValueRef.CreateConstInt(_context.Int64Type, cSize) }, "");
                    _builder.BuildBr(skipCBB);

                    _builder.PositionAtEnd(skipCBB);
                }

                // Swap-remove from curArch
                var curCntSlot = _builder.BuildStructGEP2(_archStructType, curArchPtr2, 1, "cur_cnt_slot");
                var curArchCount = _builder.BuildLoad2(_context.Int32Type, curCntSlot, "cur_arch_count");
                var lastRow = _builder.BuildSub(curArchCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "last_row");
                _builder.BuildStore(lastRow, curCntSlot);

                var isLastRow = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curRow, lastRow, "is_last_row");
                var doSwapBB = setFunc.AppendBasicBlock("do_swap_remove");
                var afterSwapBB = setFunc.AppendBasicBlock("after_swap_remove");
                _builder.BuildCondBr(isLastRow, afterSwapBB, doSwapBB);

                _builder.PositionAtEnd(doSwapBB);
                // Move entity ID from lastRow to curRow
                var curEntSlotSr = _builder.BuildStructGEP2(_archStructType, curArchPtr2, 3, "cur_ent_sr");
                var curEntRawSr = _builder.BuildLoad2(i8PtrType, curEntSlotSr, "cur_ent_raw_sr");
                var curEntTypedSr = _builder.BuildBitCast(curEntRawSr, i32PtrType, "cur_ent_typed_sr");
                var lastEntElem = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedSr, new[] { lastRow }, "last_ent_elem");
                var movedE = _builder.BuildLoad2(_context.Int32Type, lastEntElem, "moved_e");
                var curEntElemSr = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedSr, new[] { curRow }, "cur_ent_elem_sr");
                _builder.BuildStore(movedE, curEntElemSr);

                // Copy all components from lastRow to curRow
                for (int c = 0; c < totalComps; c++)
                {
                    var cName = compNames[c];
                    ulong cBit = 1UL << c;
                    ulong cSize = _compSizes[cName];
                    var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                    var cAnd = _builder.BuildAnd(curMask, cBitVal, $"has_sw_{cName}");
                    var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_has_sw_{cName}");

                    var swapCBB = setFunc.AppendBasicBlock($"swap_{cName}");
                    var skipSwCBB = setFunc.AppendBasicBlock($"skip_sw_{cName}");
                    _builder.BuildCondBr(hasC, swapCBB, skipSwCBB);

                    _builder.PositionAtEnd(swapCBB);
                    var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, curColsArr, new[]
                    {
                        LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                        LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                    }, $"sw_col_{cName}");
                    var colRaw = _builder.BuildLoad2(i8PtrType, colSlot, $"sw_raw_{cName}");
                    var compTyped = _builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"sw_typed_{cName}");
                    var srcElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { lastRow }, $"sw_src_{cName}");
                    var dstElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { curRow }, $"sw_dst_{cName}");
                    var srcI8 = _builder.BuildBitCast(srcElem, i8PtrType, "sw_src_i8");
                    var dstI8 = _builder.BuildBitCast(dstElem, i8PtrType, "sw_dst_i8");
                    _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstI8, srcI8, LLVMValueRef.CreateConstInt(_context.Int64Type, cSize) }, "");
                    _builder.BuildBr(skipSwCBB);

                    _builder.PositionAtEnd(skipSwCBB);
                }

                // Update world.entity_row[movedE] = curRow
                var rowArrSr = _builder.BuildLoad2(i32PtrType, entRowSlotSet, "row_arr_sr");
                var movedERowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSr, new[] { movedE }, "moved_e_row_slot");
                _builder.BuildStore(curRow, movedERowSlot);
                _builder.BuildBr(afterSwapBB);

                _builder.PositionAtEnd(afterSwapBB);
                // Update e's location
                var archArrTr = _builder.BuildLoad2(i32PtrType, entArchSlotSet, "arch_arr_tr");
                var eArchSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrTr, new[] { eParam }, "e_arch_slot_tr");
                _builder.BuildStore(newArchIdx, eArchSlotTr);

                var rowArrTr = _builder.BuildLoad2(i32PtrType, entRowSlotSet, "row_arr_tr");
                var eRowSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrTr, new[] { eParam }, "e_row_slot_tr");
                _builder.BuildStore(newRow, eRowSlotTr);

                _builder.BuildStore(newArchIdx, targetArchAlloca);
                _builder.BuildStore(newRow, targetRowAlloca);
                _builder.BuildBr(storeFieldsBB);

                // --- Store fields into targetArch.columns[k][targetRow] ---
                _builder.PositionAtEnd(storeFieldsBB);
                var finalArchIdx = _builder.BuildLoad2(_context.Int32Type, targetArchAlloca, "final_arch");
                var finalRow = _builder.BuildLoad2(_context.Int32Type, targetRowAlloca, "final_row");

                var latestTablesSf = _builder.BuildLoad2(archPtrType, tablesSlotSet, "latest_tables_sf");
                var finalArchPtr = _builder.BuildInBoundsGEP2(_archStructType, latestTablesSf, new[] { finalArchIdx }, "final_arch_ptr");
                var finalColsArr = _builder.BuildStructGEP2(_archStructType, finalArchPtr, 4, "final_cols");
                var finalColSlot = _builder.BuildInBoundsGEP2(_colArrayType, finalColsArr, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)k)
                }, "final_col_slot");
                var finalColRaw = _builder.BuildLoad2(i8PtrType, finalColSlot, "final_col_raw");
                var finalColTyped = _builder.BuildBitCast(finalColRaw, LLVMTypeRef.CreatePointer(compStructType, 0), "final_col_typed");
                var finalElem = _builder.BuildInBoundsGEP2(compStructType, finalColTyped, new[] { finalRow }, "final_elem");

                for (int f = 0; f < compSym.Fields.Count; f++)
                {
                    var fVal = setFunc.GetParam((uint)(f + 2)); // Param 0 is world, Param 1 is e
                    var fGEP = _builder.BuildStructGEP2(compStructType, finalElem, (uint)f, $"{compSym.Fields[f].Name}_gep");
                    _builder.BuildStore(fVal, fGEP);
                }

                _builder.BuildRetVoid();
            }

            // 2. world_remove_Comp(ptr world, i32 e) -> void
            var remFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType, _context.Int32Type }, false);
            var remFunc = _module.AddFunction($"world_remove_{compName}", remFuncType);
            var remEntryBB = remFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(remEntryBB);

            var worldParamRem = remFunc.GetParam(0);
            var remE = remFunc.GetParam(1);

            var entArchSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 5, "ent_arch_slot_rem");
            var entRowSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 6, "ent_row_slot_rem");
            var tablesSlotRem = _builder.BuildStructGEP2(_worldStructType, worldParamRem, 2, "tables_slot_rem");

            var archArrRem = _builder.BuildLoad2(i32PtrType, entArchSlotRem, "arch_arr_rem");
            var remArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrRem, new[] { remE }, "rem_arch_slot");
            var curArchIdxRem = _builder.BuildLoad2(_context.Int32Type, remArchSlot, "cur_arch_rem");

            var rowArrRem = _builder.BuildLoad2(i32PtrType, entRowSlotRem, "row_arr_rem");
            var remRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrRem, new[] { remE }, "rem_row_slot");
            var curRowRem = _builder.BuildLoad2(_context.Int32Type, remRowSlot, "cur_row_rem");

            var tablesBaseRem = _builder.BuildLoad2(archPtrType, tablesSlotRem, "tables_rem");
            var curArchPtrRem = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseRem, new[] { curArchIdxRem }, "cur_arch_ptr_rem");
            var curMaskSlotRem = _builder.BuildStructGEP2(_archStructType, curArchPtrRem, 0, "cur_mask_rem");
            var curMaskRem = _builder.BuildLoad2(_context.Int64Type, curMaskSlotRem, "cur_mask_val_rem");

            var hasCompRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE,
                _builder.BuildAnd(curMaskRem, compBitVal, "rem_has_bit"),
                LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "has_comp_rem");

            var doRemBB = remFunc.AppendBasicBlock("do_remove");
            var exitRemBB = remFunc.AppendBasicBlock("exit_remove");
            _builder.BuildCondBr(hasCompRem, doRemBB, exitRemBB);

            _builder.PositionAtEnd(doRemBB);
            var notBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, ~compBit);
            var newMaskRem = _builder.BuildAnd(curMaskRem, notBitVal, "new_mask_rem");
            var newArchIdxRem = _builder.BuildCall2(getArchType, getArchFunc, new[] { worldParamRem, newMaskRem }, "new_arch_rem");

            var tablesBaseRemTr1 = _builder.BuildLoad2(archPtrType, tablesSlotRem, "tables_rem_tr1");
            var newArchPtrRem1 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseRemTr1, new[] { newArchIdxRem }, "new_arch_ptr_rem1");
            var newCntSlotRem1 = _builder.BuildStructGEP2(_archStructType, newArchPtrRem1, 1, "cnt_slot_rem1");
            var newCntRem1 = _builder.BuildLoad2(_context.Int32Type, newCntSlotRem1, "cnt_rem1");
            var newCapSlotRem1 = _builder.BuildStructGEP2(_archStructType, newArchPtrRem1, 2, "cap_slot_rem1");
            var newCapRem1 = _builder.BuildLoad2(_context.Int32Type, newCapSlotRem1, "cap_rem1");
            var needGrowRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, newCntRem1, newCapRem1, "need_grow_rem");

            var growRemBB = remFunc.AppendBasicBlock("grow_rem_arch");
            var afterGrowRemBB = remFunc.AppendBasicBlock("after_grow_rem_arch");
            _builder.BuildCondBr(needGrowRem, growRemBB, afterGrowRemBB);

            _builder.PositionAtEnd(growRemBB);
            _builder.BuildCall2(growArchType, growArchFunc, new[] { worldParamRem, newArchIdxRem }, "");
            _builder.BuildBr(afterGrowRemBB);

            _builder.PositionAtEnd(afterGrowRemBB);
            var tablesBaseRemTr2 = _builder.BuildLoad2(archPtrType, tablesSlotRem, "tables_rem_tr2");
            var curArchPtrRem2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseRemTr2, new[] { curArchIdxRem }, "cur_arch_ptr_rem2");
            var newArchPtrRem2 = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseRemTr2, new[] { newArchIdxRem }, "new_arch_ptr_rem2");

            var newCntSlotRem2 = _builder.BuildStructGEP2(_archStructType, newArchPtrRem2, 1, "cnt_slot_rem2");
            var newRowRem = _builder.BuildLoad2(_context.Int32Type, newCntSlotRem2, "new_row_rem");
            var nextNewCntRem = _builder.BuildAdd(newRowRem, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_cnt_rem");
            _builder.BuildStore(nextNewCntRem, newCntSlotRem2);

            // Store entity in newArch
            var newEntSlotRem2 = _builder.BuildStructGEP2(_archStructType, newArchPtrRem2, 3, "new_ent_rem");
            var newEntRawRem2 = _builder.BuildLoad2(i8PtrType, newEntSlotRem2, "new_ent_raw_rem");
            var newEntTypedRem2 = _builder.BuildBitCast(newEntRawRem2, i32PtrType, "new_ent_typed_rem");
            var newEntElemRem2 = _builder.BuildInBoundsGEP2(_context.Int32Type, newEntTypedRem2, new[] { newRowRem }, "new_ent_elem_rem");
            _builder.BuildStore(remE, newEntElemRem2);

            // Copy all components EXCEPT component k
            var curColsArrRem = _builder.BuildStructGEP2(_archStructType, curArchPtrRem2, 4, "cur_cols_rem");
            var newColsArrRem = _builder.BuildStructGEP2(_archStructType, newArchPtrRem2, 4, "new_cols_rem");

            for (int c = 0; c < totalComps; c++)
            {
                if (c == k) continue; // skip removed component

                var cName = compNames[c];
                ulong cBit = 1UL << c;
                ulong cSize = _compSizes[cName];
                var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                var cAnd = _builder.BuildAnd(curMaskRem, cBitVal, $"rem_has_{cName}");
                var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_has_rem_{cName}");

                var copyRemCBB = remFunc.AppendBasicBlock($"copy_rem_{cName}");
                var skipRemCBB = remFunc.AppendBasicBlock($"skip_rem_{cName}");
                _builder.BuildCondBr(hasC, copyRemCBB, skipRemCBB);

                _builder.PositionAtEnd(copyRemCBB);
                var srcColSlot = _builder.BuildInBoundsGEP2(_colArrayType, curColsArrRem, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                }, $"rem_src_col_{cName}");
                var srcColRaw = _builder.BuildLoad2(i8PtrType, srcColSlot, $"rem_src_raw_{cName}");
                var srcCompTyped = _builder.BuildBitCast(srcColRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"rem_src_typed_{cName}");
                var srcElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], srcCompTyped, new[] { curRowRem }, $"rem_src_elem_{cName}");
                var srcElemI8 = _builder.BuildBitCast(srcElem, i8PtrType, "rem_src_i8");

                var dstColSlot = _builder.BuildInBoundsGEP2(_colArrayType, newColsArrRem, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                }, $"rem_dst_col_{cName}");
                var dstColRaw = _builder.BuildLoad2(i8PtrType, dstColSlot, $"rem_dst_raw_{cName}");
                var dstCompTyped = _builder.BuildBitCast(dstColRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"rem_dst_typed_{cName}");
                var dstElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], dstCompTyped, new[] { newRowRem }, $"rem_dst_elem_{cName}");
                var dstElemI8 = _builder.BuildBitCast(dstElem, i8PtrType, "rem_dst_i8");

                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstElemI8, srcElemI8, LLVMValueRef.CreateConstInt(_context.Int64Type, cSize) }, "");
                _builder.BuildBr(skipRemCBB);

                _builder.PositionAtEnd(skipRemCBB);
            }

            // Swap-remove from curArch
            var curCntSlotRem = _builder.BuildStructGEP2(_archStructType, curArchPtrRem2, 1, "cnt_slot_rem");
            var curArchCountRem = _builder.BuildLoad2(_context.Int32Type, curCntSlotRem, "cnt_rem");
            var lastRowRem = _builder.BuildSub(curArchCountRem, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "last_row_rem");
            _builder.BuildStore(lastRowRem, curCntSlotRem);

            var isLastRowRem = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curRowRem, lastRowRem, "is_last_rem");
            var doSwapRemBB = remFunc.AppendBasicBlock("do_swap_rem");
            var afterSwapRemBB = remFunc.AppendBasicBlock("after_swap_rem");
            _builder.BuildCondBr(isLastRowRem, afterSwapRemBB, doSwapRemBB);

            _builder.PositionAtEnd(doSwapRemBB);
            var curEntSlotRemSr = _builder.BuildStructGEP2(_archStructType, curArchPtrRem2, 3, "ent_sr_rem");
            var curEntRawRemSr = _builder.BuildLoad2(i8PtrType, curEntSlotRemSr, "ent_raw_sr_rem");
            var curEntTypedRemSr = _builder.BuildBitCast(curEntRawRemSr, i32PtrType, "ent_typed_sr_rem");
            var lastEntElemRem = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedRemSr, new[] { lastRowRem }, "last_ent_rem");
            var movedERem = _builder.BuildLoad2(_context.Int32Type, lastEntElemRem, "moved_e_rem");
            var curEntElemRemSr = _builder.BuildInBoundsGEP2(_context.Int32Type, curEntTypedRemSr, new[] { curRowRem }, "cur_ent_rem");
            _builder.BuildStore(movedERem, curEntElemRemSr);

            // Copy components of curArch from lastRow to curRow
            for (int c = 0; c < totalComps; c++)
            {
                var cName = compNames[c];
                ulong cBit = 1UL << c;
                ulong cSize = _compSizes[cName];
                var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                var cAnd = _builder.BuildAnd(curMaskRem, cBitVal, $"sw_rem_has_{cName}");
                var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_sw_rem_{cName}");

                var swapRemCBB = remFunc.AppendBasicBlock($"swap_rem_{cName}");
                var skipSwRemCBB = remFunc.AppendBasicBlock($"skip_sw_rem_{cName}");
                _builder.BuildCondBr(hasC, swapRemCBB, skipSwRemCBB);

                _builder.PositionAtEnd(swapRemCBB);
                var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, curColsArrRem, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                }, $"sw_col_rem_{cName}");
                var colRaw = _builder.BuildLoad2(i8PtrType, colSlot, $"sw_raw_rem_{cName}");
                var compTyped = _builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"sw_typed_rem_{cName}");
                var srcElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { lastRowRem }, $"sw_src_rem_{cName}");
                var dstElem = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { curRowRem }, $"sw_dst_rem_{cName}");
                var srcI8 = _builder.BuildBitCast(srcElem, i8PtrType, "sw_src_rem_i8");
                var dstI8 = _builder.BuildBitCast(dstElem, i8PtrType, "sw_dst_rem_i8");
                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { dstI8, srcI8, LLVMValueRef.CreateConstInt(_context.Int64Type, cSize) }, "");
                _builder.BuildBr(skipSwRemCBB);

                _builder.PositionAtEnd(skipSwRemCBB);
            }

            var rowArrRemSr = _builder.BuildLoad2(i32PtrType, entRowSlotRem, "row_arr_rem_sr");
            var movedERowSlotRem = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrRemSr, new[] { movedERem }, "moved_e_row_slot_rem");
            _builder.BuildStore(curRowRem, movedERowSlotRem);
            _builder.BuildBr(afterSwapRemBB);

            _builder.PositionAtEnd(afterSwapRemBB);
            // Update entity records
            var archArrRemTr = _builder.BuildLoad2(i32PtrType, entArchSlotRem, "arch_arr_rem_tr");
            var remArchSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrRemTr, new[] { remE }, "rem_arch_slot_tr");
            _builder.BuildStore(newArchIdxRem, remArchSlotTr);

            var rowArrRemTr = _builder.BuildLoad2(i32PtrType, entRowSlotRem, "row_arr_rem_tr");
            var remRowSlotTr = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrRemTr, new[] { remE }, "rem_row_slot_tr");
            _builder.BuildStore(newRowRem, remRowSlotTr);

            _builder.BuildBr(exitRemBB);

            _builder.PositionAtEnd(exitRemBB);
            _builder.BuildRetVoid();

            // 3. world_has_Comp(ptr world, i32 e) -> bool
            var hasFuncType = LLVMTypeRef.CreateFunction(_context.Int1Type, new[] { worldPtrType, _context.Int32Type }, false);
            var hasFunc = _module.AddFunction($"world_has_{compName}", hasFuncType);
            var hasEntryBB = hasFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(hasEntryBB);

            var worldParamHas = hasFunc.GetParam(0);
            var hasEParam = hasFunc.GetParam(1);

            var entArchSlotHas = _builder.BuildStructGEP2(_worldStructType, worldParamHas, 5, "ent_arch_slot_has");
            var tablesSlotHas = _builder.BuildStructGEP2(_worldStructType, worldParamHas, 2, "tables_slot_has");

            var archArrHas = _builder.BuildLoad2(i32PtrType, entArchSlotHas, "arch_arr_has");
            var hasArchSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, archArrHas, new[] { hasEParam }, "has_arch_slot");
            var curArchIdxHas = _builder.BuildLoad2(_context.Int32Type, hasArchSlot, "cur_arch_idx_has");

            var tablesBaseHas = _builder.BuildLoad2(archPtrType, tablesSlotHas, "tables_has");
            var archPtrHas = _builder.BuildInBoundsGEP2(_archStructType, tablesBaseHas, new[] { curArchIdxHas }, "arch_ptr_has");
            var maskSlotHas = _builder.BuildStructGEP2(_archStructType, archPtrHas, 0, "mask_slot_has");
            var archMaskHas = _builder.BuildLoad2(_context.Int64Type, maskSlotHas, "arch_mask_has");

            var bitAndHas = _builder.BuildAnd(archMaskHas, compBitVal, "bit_and_has");
            var resultHas = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, bitAndHas, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "res_has");
            _builder.BuildRet(resultHas);
        }

        // =========================================================================
        // Helpers for resources: world_set_Time(ptr world, dt)
        // =========================================================================
        foreach (var (resName, resSym) in _typeChecker.Resources)
        {
            var structType = GetComponentStructType(resName);
            int resOff = _resourceWorldOffsets[resName];

            var paramTypes = new List<LLVMTypeRef> { worldPtrType };
            foreach (var f in resSym.Fields)
            {
                paramTypes.Add(MapType(f.Type.Name));
            }

            var setterType = LLVMTypeRef.CreateFunction(_context.VoidType, paramTypes.ToArray(), false);
            var setterFunc = _module.AddFunction($"world_set_{resName}", setterType);
            var bb = setterFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(bb);

            var worldParamRes = setterFunc.GetParam(0);
            var resFieldSlot = _builder.BuildStructGEP2(_worldStructType, worldParamRes, (uint)resOff, $"res_{resName}_slot");

            for (int i = 0; i < resSym.Fields.Count; i++)
            {
                var fieldVal = setterFunc.GetParam((uint)(i + 1));
                var fieldGEP = _builder.BuildStructGEP2(structType, resFieldSlot, (uint)i, $"{resSym.Fields[i].Name}_gep");
                _builder.BuildStore(fieldVal, fieldGEP);
            }
            _builder.BuildRetVoid();
        }

        // =========================================================================
        // world_sort_hierarchy(ptr world): sort entities inside archetypes that have ChildOf
        // =========================================================================
        if (_compIds.TryGetValue("ChildOf", out int childOfId))
        {
            var sortType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType }, false);
            var sortFunc = _module.AddFunction("world_sort_hierarchy", sortType);
            var sEntryBB = sortFunc.AppendBasicBlock("entry");
            _builder.PositionAtEnd(sEntryBB);

            var worldParamSh = sortFunc.GetParam(0);
            var archCountSlotSh = _builder.BuildStructGEP2(_worldStructType, worldParamSh, 0, "arch_count_sh");
            var tablesSlotSh = _builder.BuildStructGEP2(_worldStructType, worldParamSh, 2, "tables_slot_sh");
            var entRowSlotSh = _builder.BuildStructGEP2(_worldStructType, worldParamSh, 6, "ent_row_slot_sh");

            ulong childOfBit = 1UL << childOfId;
            var childOfBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, childOfBit);

            var numArchs = _builder.BuildLoad2(_context.Int32Type, archCountSlotSh, "num_archs");
            var archIdxAlloca = _builder.BuildAlloca(_context.Int32Type, "arch_i");
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), archIdxAlloca);

            var initIAlloca = _builder.BuildAlloca(_context.Int32Type, "init_i");
            var sortIAlloca = _builder.BuildAlloca(_context.Int32Type, "sort_i");
            var sortJAlloca = _builder.BuildAlloca(_context.Int32Type, "sort_j");

            var tempAllocas = new Dictionary<string, LLVMValueRef>(StringComparer.Ordinal);
            foreach (var cName in compNames)
            {
                tempAllocas[cName] = _builder.BuildAlloca(_compStructTypes[cName], $"temp_{cName}");
            }

            var archLoopCondBB = sortFunc.AppendBasicBlock("arch_loop_cond");
            var archLoopBodyBB = sortFunc.AppendBasicBlock("arch_loop_body");
            var archLoopExitBB = sortFunc.AppendBasicBlock("arch_loop_exit");

            _builder.BuildBr(archLoopCondBB);

            _builder.PositionAtEnd(archLoopCondBB);
            var curAIdx = _builder.BuildLoad2(_context.Int32Type, archIdxAlloca, "cur_a_idx");
            var hasMoreArchs = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curAIdx, numArchs, "has_more_archs");
            _builder.BuildCondBr(hasMoreArchs, archLoopBodyBB, archLoopExitBB);

            _builder.PositionAtEnd(archLoopBodyBB);
            var tBase = _builder.BuildLoad2(archPtrType, tablesSlotSh, "t_base");
            var curArch = _builder.BuildInBoundsGEP2(_archStructType, tBase, new[] { curAIdx }, "cur_a");
            var mSlot = _builder.BuildStructGEP2(_archStructType, curArch, 0, "m_slot");
            var mVal = _builder.BuildLoad2(_context.Int64Type, mSlot, "m_val");

            var hasChildOf = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE,
                _builder.BuildAnd(mVal, childOfBitVal, "and_co"),
                LLVMValueRef.CreateConstInt(_context.Int64Type, 0), "has_co");

            var checkSortBB = sortFunc.AppendBasicBlock("check_sort");
            var nextArchBB = sortFunc.AppendBasicBlock("next_arch");

            _builder.BuildCondBr(hasChildOf, checkSortBB, nextArchBB);

            _builder.PositionAtEnd(checkSortBB);
            var cntSlotSh = _builder.BuildStructGEP2(_archStructType, curArch, 1, "cnt_slot_sh");
            var totalCountSh = _builder.BuildLoad2(_context.Int32Type, cntSlotSh, "cnt_sh");
            var canSort = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, totalCountSh, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "can_sort");

            var doSortBB = sortFunc.AppendBasicBlock("do_sort");
            _builder.BuildCondBr(canSort, doSortBB, nextArchBB);

            _builder.PositionAtEnd(doSortBB);
            var count64 = _builder.BuildZExt(totalCountSh, _context.Int64Type, "count64");
            var bytesNeeded = _builder.BuildMul(count64, LLVMValueRef.CreateConstInt(_context.Int64Type, 4), "bytes_needed");
            var depthsRaw = _builder.BuildCall2(mallocType, mallocFunc, new[] { bytesNeeded }, "depths_raw");

            var colsSh = _builder.BuildStructGEP2(_archStructType, curArch, 4, "cols_sh");
            var coSlot = _builder.BuildInBoundsGEP2(_colArrayType, colsSh, new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)childOfId)
            }, "co_slot");
            var coColRaw = _builder.BuildLoad2(i8PtrType, coSlot, "co_col_raw");
            var coColTyped = _builder.BuildBitCast(coColRaw, LLVMTypeRef.CreatePointer(_compStructTypes["ChildOf"], 0), "co_col_typed");

            // Calculate depths (0 = root, 1 = child)
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), initIAlloca);

            var dInitCondBB = sortFunc.AppendBasicBlock("d_init_cond");
            var dInitBodyBB = sortFunc.AppendBasicBlock("d_init_body");
            var dInitExitBB = sortFunc.AppendBasicBlock("d_init_exit");

            _builder.BuildBr(dInitCondBB);

            _builder.PositionAtEnd(dInitCondBB);
            var curInitI = _builder.BuildLoad2(_context.Int32Type, initIAlloca, "cur_init_i");
            var hasInitMore = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curInitI, totalCountSh, "has_init_more");
            _builder.BuildCondBr(hasInitMore, dInitBodyBB, dInitExitBB);

            _builder.PositionAtEnd(dInitBodyBB);
            var childElem = _builder.BuildInBoundsGEP2(_compStructTypes["ChildOf"], coColTyped, new[] { curInitI }, "child_elem");
            var parentGEP = _builder.BuildStructGEP2(_compStructTypes["ChildOf"], childElem, 0, "parent_gep");
            var parentId = _builder.BuildLoad2(_context.Int32Type, parentGEP, "parent_id");
            var isRoot = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, parentId, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "is_root");
            var depthVal = _builder.BuildSelect(isRoot, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "depth_val");
            var depthsTyped = _builder.BuildBitCast(depthsRaw, i32PtrType, "depths_typed");
            var depthSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, depthsTyped, new[] { curInitI }, "depth_slot");
            _builder.BuildStore(depthVal, depthSlot);

            var nextInitI = _builder.BuildAdd(curInitI, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_init_i");
            _builder.BuildStore(nextInitI, initIAlloca);
            _builder.BuildBr(dInitCondBB);

            _builder.PositionAtEnd(dInitExitBB);
            // Bubble sort rows by depth
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), sortIAlloca);

            var outerCondBB = sortFunc.AppendBasicBlock("sort_outer_cond");
            var outerBodyBB = sortFunc.AppendBasicBlock("sort_outer_body");
            var outerExitBB = sortFunc.AppendBasicBlock("sort_outer_exit");

            _builder.BuildBr(outerCondBB);

            _builder.PositionAtEnd(outerCondBB);
            var curSortI = _builder.BuildLoad2(_context.Int32Type, sortIAlloca, "cur_sort_i");
            var outerLimit = _builder.BuildSub(totalCountSh, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "outer_limit");
            var outerMore = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curSortI, outerLimit, "outer_more");
            _builder.BuildCondBr(outerMore, outerBodyBB, outerExitBB);

            _builder.PositionAtEnd(outerBodyBB);
            var innerStart = _builder.BuildAdd(curSortI, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "inner_start");
            _builder.BuildStore(innerStart, sortJAlloca);

            var innerCondBB = sortFunc.AppendBasicBlock("sort_inner_cond");
            var innerBodyBB = sortFunc.AppendBasicBlock("sort_inner_body");
            var innerStepBB = sortFunc.AppendBasicBlock("sort_inner_step");

            _builder.BuildBr(innerCondBB);

            _builder.PositionAtEnd(innerCondBB);
            var curSortJ = _builder.BuildLoad2(_context.Int32Type, sortJAlloca, "cur_sort_j");
            var innerMore = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, curSortJ, totalCountSh, "inner_more");
            _builder.BuildCondBr(innerMore, innerBodyBB, innerStepBB);

            _builder.PositionAtEnd(innerBodyBB);
            var diSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, depthsTyped, new[] { curSortI }, "di_slot");
            var djSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, depthsTyped, new[] { curSortJ }, "dj_slot");
            var di = _builder.BuildLoad2(_context.Int32Type, diSlot, "di");
            var dj = _builder.BuildLoad2(_context.Int32Type, djSlot, "dj");

            var needSwap = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGT, di, dj, "need_swap");
            var doSwapRowBB = sortFunc.AppendBasicBlock("do_swap_row");
            var skipSwapRowBB = sortFunc.AppendBasicBlock("skip_swap_row");
            _builder.BuildCondBr(needSwap, doSwapRowBB, skipSwapRowBB);

            _builder.PositionAtEnd(doSwapRowBB);
            // Swap depths
            _builder.BuildStore(dj, diSlot);
            _builder.BuildStore(di, djSlot);

            // Swap entities
            var entSlotSh = _builder.BuildStructGEP2(_archStructType, curArch, 3, "ent_slot_sh");
            var entRawSh = _builder.BuildLoad2(i8PtrType, entSlotSh, "ent_raw_sh");
            var entTypedSh = _builder.BuildBitCast(entRawSh, i32PtrType, "ent_typed_sh");
            var eiSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, entTypedSh, new[] { curSortI }, "ei_slot");
            var ejSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, entTypedSh, new[] { curSortJ }, "ej_slot");
            var ei = _builder.BuildLoad2(_context.Int32Type, eiSlot, "ei");
            var ej = _builder.BuildLoad2(_context.Int32Type, ejSlot, "ej");
            _builder.BuildStore(ej, eiSlot);
            _builder.BuildStore(ei, ejSlot);

            // Update world.entity_row
            var rowArrSh = _builder.BuildLoad2(i32PtrType, entRowSlotSh, "row_arr_sh");
            var eiRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSh, new[] { ei }, "ei_row_slot");
            var ejRowSlot = _builder.BuildInBoundsGEP2(_context.Int32Type, rowArrSh, new[] { ej }, "ej_row_slot");
            _builder.BuildStore(curSortJ, eiRowSlot);
            _builder.BuildStore(curSortI, ejRowSlot);

            // Swap all component columns in this archetype
            for (int c = 0; c < totalComps; c++)
            {
                var cName = compNames[c];
                ulong cBit = 1UL << c;
                ulong cSize = _compSizes[cName];
                var cBitVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cBit);

                var cAnd = _builder.BuildAnd(mVal, cBitVal, $"sw_co_has_{cName}");
                var hasC = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, cAnd, LLVMValueRef.CreateConstInt(_context.Int64Type, 0), $"is_sw_co_{cName}");

                var swShBB = sortFunc.AppendBasicBlock($"sw_sh_{cName}");
                var skipSwShBB = sortFunc.AppendBasicBlock($"skip_sw_sh_{cName}");
                _builder.BuildCondBr(hasC, swShBB, skipSwShBB);

                _builder.PositionAtEnd(swShBB);
                var colSlot = _builder.BuildInBoundsGEP2(_colArrayType, colsSh, new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)c)
                }, $"sw_sh_col_{cName}");
                var colRaw = _builder.BuildLoad2(i8PtrType, colSlot, $"sw_sh_raw_{cName}");
                var compTyped = _builder.BuildBitCast(colRaw, LLVMTypeRef.CreatePointer(_compStructTypes[cName], 0), $"sw_sh_typed_{cName}");
                var elemI = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { curSortI }, $"elem_i_{cName}");
                var elemJ = _builder.BuildInBoundsGEP2(_compStructTypes[cName], compTyped, new[] { curSortJ }, $"elem_j_{cName}");

                // Temp alloca for swap
                var tempAlloca = tempAllocas[cName];
                var tempI8 = _builder.BuildBitCast(tempAlloca, i8PtrType, "temp_i8");
                var elemII8 = _builder.BuildBitCast(elemI, i8PtrType, "elem_i_i8");
                var elemJI8 = _builder.BuildBitCast(elemJ, i8PtrType, "elem_j_i8");
                var szVal = LLVMValueRef.CreateConstInt(_context.Int64Type, cSize);

                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { tempI8, elemII8, szVal }, "");
                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { elemII8, elemJI8, szVal }, "");
                _builder.BuildCall2(memcpyType, memcpyFunc, new[] { elemJI8, tempI8, szVal }, "");
                _builder.BuildBr(skipSwShBB);

                _builder.PositionAtEnd(skipSwShBB);
            }

            _builder.BuildBr(skipSwapRowBB);

            _builder.PositionAtEnd(skipSwapRowBB);
            var nextJ = _builder.BuildAdd(curSortJ, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_j");
            _builder.BuildStore(nextJ, sortJAlloca);
            _builder.BuildBr(innerCondBB);

            _builder.PositionAtEnd(innerStepBB);
            var nextSortI = _builder.BuildAdd(curSortI, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_sort_i");
            _builder.BuildStore(nextSortI, sortIAlloca);
            _builder.BuildBr(outerCondBB);

            _builder.PositionAtEnd(outerExitBB);
            _builder.BuildCall2(freeType, freeFunc, new[] { depthsRaw }, "");
            _builder.BuildBr(nextArchBB);

            _builder.PositionAtEnd(nextArchBB);
            var nextAIdx = _builder.BuildAdd(curAIdx, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_a_idx");
            _builder.BuildStore(nextAIdx, archIdxAlloca);
            _builder.BuildBr(archLoopCondBB);

            _builder.PositionAtEnd(archLoopExitBB);
            _builder.BuildRetVoid();
        }

        // =========================================================================
        // Event Runtime: world_emit_{Name}(ptr world, ...) and world_swap_events(ptr world)
        // =========================================================================
        foreach (var (evName, evSym) in _typeChecker.Events)
        {
            int evBaseOffset = GetEventWorldOffset(evName);
            ulong evSize = _eventSizes[evName];
            var evStructType = _compStructTypes[evName];
            var evPtrType = LLVMTypeRef.CreatePointer(evStructType, 0);

            var paramTypes = new List<LLVMTypeRef> { worldPtrType };
            foreach (var f in evSym.Fields)
            {
                paramTypes.Add(MapType(f.Type.Name));
            }

            var emitFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, paramTypes.ToArray(), false);
            var emitFunc = _module.AddFunction($"world_emit_{evName}", emitFuncType);
            var wArg = emitFunc.GetParam(0);
            wArg.Name = "world";

            var evEntryBB = emitFunc.AppendBasicBlock("entry");
            var evGrowBB = emitFunc.AppendBasicBlock("grow");
            var evStoreBB = emitFunc.AppendBasicBlock("store_event");

            _builder.PositionAtEnd(evEntryBB);

            var wCountSlot = _builder.BuildStructGEP2(_worldStructType, wArg, (uint)(evBaseOffset + 3), "wcount_slot");
            var wCapSlot = _builder.BuildStructGEP2(_worldStructType, wArg, (uint)(evBaseOffset + 4), "wcap_slot");
            var wDataSlot = _builder.BuildStructGEP2(_worldStructType, wArg, (uint)(evBaseOffset + 5), "wdata_slot");

            var curWCount = _builder.BuildLoad2(_context.Int32Type, wCountSlot, "cur_wcount");
            var curWCap = _builder.BuildLoad2(_context.Int32Type, wCapSlot, "cur_wcap");

            var evNeedGrow = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSGE, curWCount, curWCap, "need_grow");
            _builder.BuildCondBr(evNeedGrow, evGrowBB, evStoreBB);

            // grow block
            _builder.PositionAtEnd(evGrowBB);
            var evCapIsZero = _builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, curWCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 0), "cap_is_zero");
            var evDoubledCap = _builder.BuildMul(curWCap, LLVMValueRef.CreateConstInt(_context.Int32Type, 2), "doubled_cap");
            var evNewCap = _builder.BuildSelect(evCapIsZero, LLVMValueRef.CreateConstInt(_context.Int32Type, 16), evDoubledCap, "new_cap");
            var evNewCap64 = _builder.BuildZExt(evNewCap, _context.Int64Type, "new_cap64");
            var evNewBytes = _builder.BuildMul(evNewCap64, LLVMValueRef.CreateConstInt(_context.Int64Type, evSize), "new_bytes");

            var evOldDataRaw = _builder.BuildLoad2(i8PtrType, wDataSlot, "old_data");
            var evReallocCall = _builder.BuildCall2(reallocType, reallocFunc, new[] { evOldDataRaw, evNewBytes }, "new_data");
            _builder.BuildStore(evNewCap, wCapSlot);
            _builder.BuildStore(evReallocCall, wDataSlot);
            _builder.BuildBr(evStoreBB);

            // store_event block
            _builder.PositionAtEnd(evStoreBB);
            var finalDataRaw = _builder.BuildLoad2(i8PtrType, wDataSlot, "final_data_raw");
            var finalDataTyped = _builder.BuildBitCast(finalDataRaw, evPtrType, "final_data_typed");
            var elemSlot = _builder.BuildInBoundsGEP2(evStructType, finalDataTyped, new[] { curWCount }, "ev_elem_slot");

            for (int i = 0; i < evSym.Fields.Count; i++)
            {
                var fieldArg = emitFunc.GetParam((uint)(i + 1));
                var fieldSlot = _builder.BuildStructGEP2(evStructType, elemSlot, (uint)i, $"ev_f_{evSym.Fields[i].Name}");
                _builder.BuildStore(fieldArg, fieldSlot);
            }

            var nextWCount = _builder.BuildAdd(curWCount, LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next_wcount");
            _builder.BuildStore(nextWCount, wCountSlot);
            _builder.BuildRetVoid();
        }

        // world_swap_events(ptr world)
        var swapFuncType = LLVMTypeRef.CreateFunction(_context.VoidType, new[] { worldPtrType }, false);
        var swapFunc = _module.AddFunction("world_swap_events", swapFuncType);
        var swapWorldArg = swapFunc.GetParam(0);
        swapWorldArg.Name = "world";

        var swapEntryBB = swapFunc.AppendBasicBlock("entry");
        _builder.PositionAtEnd(swapEntryBB);

        foreach (var (evName, _) in _typeChecker.Events)
        {
            int evBaseOffset = GetEventWorldOffset(evName);

            var rCountSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 0), $"{evName}_rcount_slot");
            var rCapSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 1), $"{evName}_rcap_slot");
            var rDataSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 2), $"{evName}_rdata_slot");

            var wCountSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 3), $"{evName}_wcount_slot");
            var wCapSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 4), $"{evName}_wcap_slot");
            var wDataSlot = _builder.BuildStructGEP2(_worldStructType, swapWorldArg, (uint)(evBaseOffset + 5), $"{evName}_wdata_slot");

            var wCount = _builder.BuildLoad2(_context.Int32Type, wCountSlot, $"{evName}_wcount");
            _builder.BuildStore(wCount, rCountSlot);
            _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 0), wCountSlot);

            // Swap pointers and caps
            var rCap = _builder.BuildLoad2(_context.Int32Type, rCapSlot, $"{evName}_rcap");
            var wCap = _builder.BuildLoad2(_context.Int32Type, wCapSlot, $"{evName}_wcap");
            _builder.BuildStore(wCap, rCapSlot);
            _builder.BuildStore(rCap, wCapSlot);

            var rData = _builder.BuildLoad2(i8PtrType, rDataSlot, $"{evName}_rdata");
            var wData = _builder.BuildLoad2(i8PtrType, wDataSlot, $"{evName}_wdata");
            _builder.BuildStore(wData, rDataSlot);
            _builder.BuildStore(rData, wDataSlot);
        }

        _builder.BuildRetVoid();
    }

    private LLVMTypeRef MapType(string typeName) => typeName switch
    {
        "f32" or "float" => _context.FloatType,
        "f64" or "double" => _context.DoubleType,
        "i64" or "u64" => _context.Int64Type,
        "i32" or "u32" or "int" => _context.Int32Type,
        "bool" => _context.Int1Type,
        "string" or "str" => LLVMTypeRef.CreatePointer(_context.Int8Type, 0),
        "World" or "world" => LLVMTypeRef.CreatePointer(_worldStructType, 0),
        _ => _compStructTypes.TryGetValue(typeName, out var st) ? st : _context.Int32Type
    };
}
