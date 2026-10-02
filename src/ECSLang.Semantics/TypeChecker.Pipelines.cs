using ECSLang.Core.AST;

namespace ECSLang.Semantics;

public sealed partial class TypeChecker
{
    private readonly Dictionary<ParallelAutoBlockNode, IReadOnlyList<IReadOnlyList<SystemCallAction>>> _parallelAutoBatches = new();

    public IReadOnlyList<IReadOnlyList<SystemCallAction>> GetParallelAutoBatches(ParallelAutoBlockNode node)
    {
        if (_parallelAutoBatches.TryGetValue(node, out var batches))
        {
            return batches;
        }
        return Array.Empty<IReadOnlyList<SystemCallAction>>();
    }

    private void CheckParallelAutoBlock(ParallelAutoBlockNode autoBlock, StageDefinition stage)
    {
        var resolved = new List<(SystemCallAction Action, SystemSymbol Symbol)>();
        foreach (var sCall in autoBlock.Systems)
        {
            if (sCall.SystemName.Contains("<"))
            {
                EnsureMonomorphizedSystem(sCall.SystemName, null, sCall.Span);
            }

            if (!_systems.TryGetValue(sCall.SystemName, out var sSym) &&
                !_systems.TryGetValue(TypeSymbol.ToMonomorphizedIdentifier(sCall.SystemName), out sSym))
            {
                _diagnostics.ReportError($"Undefined system '{sCall.SystemName}' in parallel auto stage '{stage.Name}'.", sCall.Span);
            }
            else
            {
                resolved.Add((sCall, sSym));
            }
        }

        // Build DAG Batches using Topological Layering
        // For each system i (0 <= i < resolved.Count):
        // System i depends on all previous systems j (0 <= j < i) that conflict with system i.
        // Layer(i) = max(0, max_{j < i, conflict(j, i)} (Layer(j) + 1))
        var layers = new int[resolved.Count];
        for (int i = 0; i < resolved.Count; i++)
        {
            int maxDepLayer = -1;
            var (_, symI) = resolved[i];
            for (int j = 0; j < i; j++)
            {
                var (_, symJ) = resolved[j];
                if (symI.HasConflictWith(symJ, out _))
                {
                    if (layers[j] > maxDepLayer)
                    {
                        maxDepLayer = layers[j];
                    }
                }
            }
            layers[i] = maxDepLayer + 1;
        }

        int numBatches = layers.Length > 0 ? layers.Max() + 1 : 0;
        var batchLists = new List<SystemCallAction>[numBatches];
        for (int b = 0; b < numBatches; b++)
        {
            batchLists[b] = new List<SystemCallAction>();
        }

        for (int i = 0; i < resolved.Count; i++)
        {
            batchLists[layers[i]].Add(resolved[i].Action);
        }

        _parallelAutoBatches[autoBlock] = batchLists;
    }
}
