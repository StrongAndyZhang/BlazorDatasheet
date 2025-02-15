﻿using BlazorDatasheet.Core.Data;
using BlazorDatasheet.Core.Data.Cells;
using BlazorDatasheet.DataStructures.Geometry;

namespace BlazorDatasheet.Core.Commands.Data;

/// <summary>
/// Clears cell values in the given ranges
/// </summary>
public class ClearCellsCommand : BaseCommand, IUndoableCommand
{
    private readonly IEnumerable<IRegion> _regions;
    private CellStoreRestoreData _restoreData = null!;

    public ClearCellsCommand(SheetRange range) : this(new[] { range.Region })
    {
    }

    public ClearCellsCommand(IEnumerable<IRegion> regions)
    {
        _regions = regions.Select(x => x.Clone()).ToList();
    }

    public override bool Execute(Sheet sheet)
    {
        _restoreData = sheet.Cells.ClearCellsImpl(_regions);
        return true;
    }

    public override bool CanExecute(Sheet sheet) => true;

    public bool Undo(Sheet sheet)
    {
        sheet.Cells.Restore(_restoreData);
        return true;
    }
}