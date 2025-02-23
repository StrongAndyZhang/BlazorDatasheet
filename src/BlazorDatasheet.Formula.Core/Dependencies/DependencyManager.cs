﻿using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.Graph;
using BlazorDatasheet.DataStructures.Store;
using BlazorDatasheet.Formula.Core.Interpreter;
using BlazorDatasheet.Formula.Core.Interpreter.References;

namespace BlazorDatasheet.Formula.Core.Dependencies;

public class DependencyManager
{
    private readonly DependencyGraph<FormulaVertex> _dependencyGraph = new();

    /// <summary>
    /// Stores regions that are referenced by formula vertices, and holds a reference
    /// to each formula vertex that references the region.
    /// E.g. for a formula "=A1 + sum(B1:B2)", the region A1 and B1:B2 will have a reference to the formula vertex
    /// </summary>
    private readonly RegionDataStore<FormulaVertex> _rferencedVertexStore = new(0, false);

    private readonly Dictionary<string, RegionDataStore<FormulaVertex>> _referencedVertexStores = new();

    internal int FormulaCount => _dependencyGraph.Count;

    private RegionDataStore<FormulaVertex> GetReferencedVertexStore(string sheetName)
    {
        return _referencedVertexStores.GetValueOrDefault(sheetName) ?? new RegionDataStore<FormulaVertex>();
    }

    public void AddSheet(string sheetName)
    {
        _referencedVertexStores.Add(sheetName, new RegionDataStore<FormulaVertex>());
    }

    public void RemoveSheet(string sheetName)
    {
        _referencedVertexStores.Remove(sheetName);
    }

    public void RenameSheet(string oldName, string newName)
    {
    }

    public DependencyManagerRestoreData SetFormula(int row, int col, string sheetName, CellFormula? formula)
    {
        var formulaVertex = new FormulaVertex(row, col, sheetName, formula);
        // Clear any dependency tracking for old formula if there is one
        var restoreData = ClearFormula(row, col, sheetName);

        if (formula == null)
            return restoreData;

        _dependencyGraph.AddVertex(formulaVertex);
        restoreData.VerticesAdded.Add(formulaVertex);

        // find formula inside any of the regions that this formula references
        // and add a dependency edge to them
        foreach (var formulaRef in formula.References)
        {
            // add edges to any formula that already exist
            if (formulaRef is not NamedReference)
            {
                var formulaInsideRegion = GetVerticesInRegion(formulaRef.Region);
                foreach (var f in formulaInsideRegion)
                {
                    _dependencyGraph.AddEdge(f, formulaVertex);
                    restoreData.EdgesAdded.Add((f.Key, formulaVertex.Key));
                }

                restoreData.RegionRestoreData.Merge(
                    GetReferencedVertexStore(formulaRef.SheetName).Add(formulaRef.Region.Clone(), formulaVertex));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        // find any formula that reference this formula and add edges to them
        foreach (var dependents in GetDirectDependents(new Region(row, col), sheetName))
        {
            _dependencyGraph.AddEdge(formulaVertex, dependents);
            restoreData.EdgesAdded.Add((formulaVertex.Key, dependents.Key));
        }

        return restoreData;
    }

    public DependencyManagerRestoreData ClearFormula(int row, int col, string sheetName)
    {
        var restoreData = new DependencyManagerRestoreData();
        var formulaVertex = new FormulaVertex(row, col, sheetName, null);
        if (!_dependencyGraph.HasVertex(formulaVertex.Key))
            return restoreData;

        formulaVertex = _dependencyGraph.GetVertex(formulaVertex.Key);

        // remove the references that refer to this formula cell
        var formulaReferences = formulaVertex.Formula?.References;

        if (formulaReferences != null)
        {
            foreach (var formulaRef in formulaReferences)
            {
                List<DataRegion<FormulaVertex>> dataToDelete = [];
                switch (formulaRef)
                {
                    case CellReference cellRef:
                        dataToDelete = GetReferencedVertexStore(cellRef.SheetName)
                            .GetDataRegions(new Region(cellRef.RowIndex, cellRef.ColIndex), formulaVertex).ToList();
                        break;
                    case RangeReference rangeReference:
                        dataToDelete = GetReferencedVertexStore(rangeReference.SheetName)
                            .GetDataRegions(rangeReference.Region, formulaVertex).ToList();
                        break;
                    case NamedReference namedReference:
                        throw new NotImplementedException();
                        break;
                }

                if (dataToDelete.Count != 0)
                    restoreData.RegionRestoreData = GetReferencedVertexStore(formulaRef.SheetName).Delete(dataToDelete);
            }
        }

        // we should only delete edges that show cells that this formula is dependent on
        // if there are formula that depend on this formula, they shouldn't be removed?

        foreach (var vertex in _dependencyGraph.Adj(formulaVertex))
            restoreData.EdgesRemoved.Add((formulaVertex.Key, vertex.Key));

        foreach (var vertex in _dependencyGraph.Prec(formulaVertex))
            restoreData.EdgesRemoved.Add((vertex.Key, formulaVertex.Key));

        _dependencyGraph.RemoveVertex(formulaVertex, false);
        restoreData.VerticesRemoved.Add(formulaVertex);

        return restoreData;
    }

    public bool HasDependents(IRegion region, string sheetName)
    {
        return GetReferencedVertexStore(sheetName).Any(region);
    }

    public bool HasDependents(int row, int col, string sheetName)
    {
        var formulaReferenced = _dependencyGraph.HasVertex(new FormulaVertex(row, col, sheetName, null).Key);
        if (formulaReferenced)
            return true;
        return GetReferencedVertexStore(sheetName).Any(row, col);
    }

    /// <summary>
    /// Returns the vertices that are directly dependent on the given region
    /// </summary>
    /// <param name="region"></param>
    /// <param name="sheetName"></param>
    /// <returns></returns>
    public IEnumerable<FormulaVertex> GetDirectDependents(IRegion region, string sheetName)
    {
        return GetReferencedVertexStore(sheetName).GetData(region);
    }

    public DependencyManagerRestoreData InsertRowAt(int row, int count, string sheetName) =>
        InsertRowColAt(row, count, Axis.Row, sheetName);

    public DependencyManagerRestoreData InsertRowColAt(int index, int count, Axis axis, string sheetName)
    {
        var restoreData = new DependencyManagerRestoreData()
        {
            Shifts = { new AppliedShift(axis, index, count) }
        };
        IRegion affectedRegion = axis == Axis.Col
            ? new ColumnRegion(index, int.MaxValue)
            : new RowRegion(index, int.MaxValue);

        int dCol = axis == Axis.Col ? count : 0;
        int dRow = axis == Axis.Row ? count : 0;

        // find anything that depends directly on the regions that are shifted
        // and shift the formula references
        // needs to be done before we shift vertices

        var formulaDependents = GetDirectDependents(affectedRegion, sheetName);

        foreach (var dependent in formulaDependents)
        {
            // capture the current references before they are modified
            var existingRegions = dependent.Formula!.References.Select(r => r.Region.Clone()).ToList();
            var existingValidities = dependent.Formula!.References.Select(r => r.IsInvalid).ToList();
            restoreData.ModifiedFormulaReferences.Add(new ReferenceRestoreData(dependent.Formula!, existingRegions,
                existingValidities, sheetName));
            dependent.Formula!.InsertRowColIntoReferences(index, count, axis, sheetName);
        }

        restoreData.Merge(ShiftVerticesInRegion(affectedRegion, dRow, dCol));
        restoreData.RegionRestoreData = GetReferencedVertexStore(sheetName).InsertRowColAt(index, count, axis);

        return restoreData;
    }

    private List<FormulaVertex> GetVerticesInRegion(IRegion region)
    {
        var vertices = new List<FormulaVertex>();
        foreach (var v in _dependencyGraph.GetAll())
        {
            if (region.Intersects(v.Region))
            {
                vertices.Add(v);
            }
        }

        return vertices;
    }

    public DependencyManagerRestoreData InsertColAt(int col, int count, string sheetName) =>
        InsertRowColAt(col, count, Axis.Col, sheetName);

    public DependencyManagerRestoreData RemoveColAt(int col, int count, string sheetName) =>
        RemoveRowColAt(col, count, Axis.Col, sheetName);

    public DependencyManagerRestoreData RemoveRowAt(int row, int count, string sheetName) =>
        RemoveRowColAt(row, count, Axis.Row, sheetName);

    public DependencyManagerRestoreData RemoveRowColAt(int index, int count, Axis axis, string sheetName)
    {
        var restoreData = new DependencyManagerRestoreData()
        {
            Shifts = { new AppliedShift(axis, index, -count) }
        };
        IRegion regionRemoved =
            axis == Axis.Col
                ? new ColumnRegion(index, index + count - 1)
                : new RowRegion(index, index + count - 1);

        // remove any formula in the region being removed
        var vertices = GetVerticesInRegion(regionRemoved);
        foreach (var vertex in vertices)
        {
            restoreData.Merge(ClearFormula(vertex.Region!.Top, vertex.Region!.Left, sheetName));
        }

        int dCol = axis == Axis.Col ? -count : 0;
        int dRow = axis == Axis.Row ? -count : 0;

        // find anything that depends directly on the regions that are shifted
        // and modify the formula references
        // needs to be done before we shift vertices

        IRegion affectedRegion = axis == Axis.Col
            ? new ColumnRegion(index, int.MaxValue)
            : new RowRegion(index, int.MaxValue);

        var dependentFormula = GetReferencedVertexStore(sheetName).GetData(affectedRegion);

        foreach (var dependent in dependentFormula)
        {
            // capture the current references before they are modified
            var existingRegions = dependent.Formula!.References.Select(r => r.Region.Clone()).ToList();
            var existingValidities = dependent.Formula!.References.Select(r => r.IsInvalid).ToList();
            restoreData.ModifiedFormulaReferences.Add(new ReferenceRestoreData(dependent.Formula!, existingRegions,
                existingValidities, sheetName));
            dependent.Formula!.RemoveRowColFromReferences(index, count, axis, sheetName);
        }

        restoreData.Merge(ShiftVerticesInRegion(affectedRegion, dRow, dCol));
        restoreData.RegionRestoreData.Merge(GetReferencedVertexStore(sheetName).RemoveRowColAt(index, count, axis));
        return restoreData;
    }

    private DependencyManagerRestoreData ShiftVerticesInRegion(IRegion region, int dRow, int dCol)
    {
        var restoreData = new DependencyManagerRestoreData();
        // shift any affected vertices by the number inserted
        var affectedVertices = GetVerticesInRegion(region);
        foreach (var v in affectedVertices)
        {
            // need to shift without changing the reference
            // needs to update key in dependency graph
            // and also shift the region it refers to
            v.Region!.Shift(dRow, dCol);
            _dependencyGraph.RefreshKey(v);
        }

        return restoreData;
    }

    /// <summary>
    /// Returns the topological sort of the vertices. Each group of vertices is a strongly connected group.
    /// </summary>
    /// <returns></returns>
    public IList<IList<FormulaVertex>> GetCalculationOrder()
    {
        var sort = new SccSort<FormulaVertex>(_dependencyGraph);
        return sort.Sort();
    }

    public IEnumerable<DependencyInfo> GetDependencies()
    {
        var results = new List<DependencyInfo>();
        foreach (var vertex in _dependencyGraph.GetAll())
        {
            foreach (var dependent in _dependencyGraph.Adj(vertex))
            {
                if (dependent.VertexType != VertexType.Named)
                    results.Add(new DependencyInfo(dependent.Region!, vertex.Region!, DependencyType.CalculationOrder));
            }
        }

        var dataRegions = _referencedVertexStores.SelectMany(x => x.Value.GetAllDataRegions());
        foreach (var region in dataRegions)
        {
            results.Add(new DependencyInfo(region.Data.Region!, region.Region, DependencyType.Region));
        }

        return results;
    }

    public void Restore(DependencyManagerRestoreData restoreData)
    {
        foreach (var shift in restoreData.Shifts)
        {
            IRegion r = shift.Axis == Axis.Col
                ? new ColumnRegion(shift.Index, int.MaxValue)
                : new RowRegion(shift.Index, int.MaxValue);

            var dRow = shift.Axis == Axis.Row ? -shift.Amount : 0;
            var dCol = shift.Axis == Axis.Col ? -shift.Amount : 0;

            ShiftVerticesInRegion(r, dRow, dCol);
        }

        foreach (var vertex in restoreData.VerticesAdded)
        {
            _dependencyGraph.RemoveVertex(vertex);
        }

        foreach (var vertex in restoreData.VerticesRemoved)
        {
            _dependencyGraph.AddVertex(vertex);
        }

        foreach (var edge in restoreData.EdgesAdded)
        {
            _dependencyGraph.RemoveEdge(edge.Item1, edge.Item2);
        }

        foreach (var edge in restoreData.EdgesRemoved)
        {
            _dependencyGraph.AddEdge(edge.Item1, edge.Item2);
        }

        // 1. shift & restore referenced vertex store
        _referencedVertexStores.First().Value.Restore(restoreData.RegionRestoreData);

        // 2. restore contrracted/expanded/shifted formula references from the records

        foreach (var regionModification in restoreData.ModifiedFormulaReferences)
        {
            int refIndex = 0;
            foreach (var formulaReference in regionModification.Formula.References)
            {
                formulaReference.SetRegion(regionModification.OldRegions[refIndex]);
                formulaReference.SetValidity(!regionModification.OldInvalidStates[refIndex]);
                refIndex++;
            }
        }
    }
}

public class DependencyManagerRestoreData
{
    public RegionRestoreData<FormulaVertex> RegionRestoreData { get; set; } = new();
    public List<FormulaVertex> VerticesRemoved { get; set; } = new();
    public List<FormulaVertex> VerticesAdded { get; set; } = new();
    public readonly List<(string, string)> EdgesRemoved = new();
    public readonly List<(string, string)> EdgesAdded = new();
    public readonly List<AppliedShift> Shifts = new();
    internal readonly List<ReferenceRestoreData> ModifiedFormulaReferences = new();

    public void Merge(DependencyManagerRestoreData other)
    {
        RegionRestoreData.Merge(other.RegionRestoreData);
        VerticesAdded.AddRange(other.VerticesAdded);
        VerticesRemoved.AddRange(other.VerticesRemoved);
        EdgesAdded.AddRange(other.EdgesAdded);
        EdgesRemoved.AddRange(other.EdgesRemoved);
        Shifts.AddRange(other.Shifts);
        ModifiedFormulaReferences.AddRange(other.ModifiedFormulaReferences);
    }
}

internal class ReferenceRestoreData
{
    public CellFormula Formula { get; }
    public List<IRegion> OldRegions { get; }
    public List<bool> OldInvalidStates { get; }
    public string SheetName { get; }

    public ReferenceRestoreData(CellFormula formula, List<IRegion> oldRegions, List<bool> oldInvalidStates,
        string sheetName)
    {
        Formula = formula;
        OldRegions = oldRegions;
        OldInvalidStates = oldInvalidStates;
        SheetName = sheetName;
    }
}