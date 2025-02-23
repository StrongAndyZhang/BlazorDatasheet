﻿using BlazorDatasheet.Core.Data;
using BlazorDatasheet.Core.Data.Cells;
using BlazorDatasheet.Core.Edit;
using BlazorDatasheet.Core.Events.Data;
using BlazorDatasheet.Core.Events.Edit;
using BlazorDatasheet.Core.Events.Layout;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.Store;
using BlazorDatasheet.Formula.Core;
using BlazorDatasheet.Formula.Core.Dependencies;
using BlazorDatasheet.Formula.Core.Interpreter.Evaluation;
using BlazorDatasheet.Formula.Core.Interpreter.Parsing;
using BlazorDatashet.Formula.Functions;
using CellFormula = BlazorDatasheet.Formula.Core.Interpreter.CellFormula;

namespace BlazorDatasheet.Core.FormulaEngine;

public class FormulaEngine
{
    private IEnvironment _environment;
    private readonly Parser _parser = new();
    private readonly Evaluator _evaluator;
    internal readonly DependencyManager DependencyManager = new();
    private List<Sheet> _sheets = new();

    public bool IsCalculating { get; private set; }

    internal FormulaEngine(IEnvironment environment)
    {
        _environment = environment;
        _evaluator = new Evaluator(_environment);

        RegisterDefaultFunctions();
    }

    internal void AddSheet(Sheet sheet)
    {
        _sheets.Add(sheet);
        DependencyManager.AddSheet(sheet.Name);
        sheet.Editor.BeforeCellEdit += SheetOnBeforeCellEdit;
        sheet.Cells.CellsChanged += SheetOnCellsChanged;
        sheet.Rows.Removed += RowsOnRemoved;
        sheet.Columns.Removed += RowsOnRemoved;
    }

    internal void RemoveSheet(Sheet sheet)
    {
        _sheets.Remove(sheet);
        DependencyManager.RemoveSheet(sheet.Name);
        sheet.Editor.BeforeCellEdit -= SheetOnBeforeCellEdit;
        sheet.Cells.CellsChanged -= SheetOnCellsChanged;
        sheet.Rows.Removed -= RowsOnRemoved;
        sheet.Columns.Removed -= RowsOnRemoved;
    }

    private void SheetOnCellsChanged(object? sender, CellDataChangedEventArgs e)
    {
        var sheet = ((CellStore)sender!).Sheet;
        if (this.IsCalculating)
            return;

        var cellsReferenced = false;
        foreach (var cell in e.Positions)
        {
            if (IsCellReferenced(cell.row, cell.col, sheet.Name))
            {
                cellsReferenced = true;
                break;
            }
        }

        if (!cellsReferenced)
        {
            foreach (var region in e.Regions)
            {
                if (RegionContainsReferencedCells(region, sheet.Name))
                {
                    cellsReferenced = true;
                    break;
                }
            }
        }

        if (!cellsReferenced)
            return;

        this.CalculateSheet(sheet);
    }

    private void RowsOnRemoved(object? sender, RowColRemovedEventArgs e)
    {
        var sheet = ((RowColInfoStore)sender!).Sheet;
        CalculateSheet(sheet);
    }

    private bool IsCellReferenced(int row, int col, string sheetName)
    {
        return DependencyManager.HasDependents(row, col, sheetName);
    }

    private bool RegionContainsReferencedCells(IRegion region, string sheetName)
    {
        return DependencyManager.HasDependents(region, sheetName);
    }

    private void RegisterDefaultFunctions()
    {
        _environment.RegisterLogicalFunctions();
        _environment.RegisterMathFunctions();
        _environment.RegisterLookupFunctions();
    }

    private void SheetOnBeforeCellEdit(object? sender, BeforeCellEditEventArgs e)
    {
        var sheet = ((Editor)sender!).Sheet;
        var formula = sheet.Cells.GetFormulaString(e.Cell.Row, e.Cell.Col);
        if (formula != null)
        {
            e.EditValue = formula;
        }
    }

    internal DependencyManagerRestoreData SetFormula(int row, int col, string sheetName, CellFormula? formula)
    {
        return DependencyManager.SetFormula(row, col, sheetName, formula);
    }

    public CellFormula ParseFormula(string formulaString)
    {
        return _parser.FromString(formulaString);
    }

    public CellValue Evaluate(CellFormula? formula, bool resolveReferences = true)
    {
        if (formula == null)
            return CellValue.Empty;
        try
        {
            return _evaluator.Evaluate(formula, new FormulaExecutionContext(),
                new FormulaEvaluationOptions(!resolveReferences));
        }
        catch (Exception e)
        {
            return CellValue.Error(ErrorType.Na, $"Error running formula: {e.Message}");
        }
    }

    /// <summary>
    /// Removes any vertices that the formula in this cell is dependent on
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <param name="sheetName"></param>
    internal DependencyManagerRestoreData RemoveFormula(int row, int col, string sheetName)
    {
        return DependencyManager.ClearFormula(row, col, sheetName);
    }

    public IEnumerable<DependencyInfo> GetDependencies() => DependencyManager.GetDependencies();

    public void CalculateSheet(Sheet sheet)
    {
        if (IsCalculating)
            return;

        IsCalculating = true;
        sheet.BatchUpdates();

        var order = DependencyManager.GetCalculationOrder();
        var executionContext = new FormulaExecutionContext();

        foreach (var scc in order)
        {
            bool isCircularGroup = false;

            foreach (var vertex in scc)
            {
                if (vertex.Formula == null || vertex.VertexType != VertexType.Cell)
                    continue;

                // if it's part of a scc group, and we don't have circular references, then the value would
                // already have been evaluated.
                CellValue value;

                // To speed up time in scc group, if one vertex is circular the rest will be.
                if (isCircularGroup)
                    value = CellValue.Error(ErrorType.Circular);
                else
                {
                    // check whether the formula has already been calculated in this scc group - may be the case if we lucked
                    // out on the first value calculation and it wasn't a circular reference.
                    if (scc.Count > 1 && executionContext.TryGetExecutedValue(vertex.Formula, out var result))
                    {
                        //TODO: This is never hit so we are always recalculating
                        value = result;
                    }
                    else
                    {
                        value = _evaluator.Evaluate(vertex.Formula, executionContext);
                        if (value.IsError() && ((FormulaError)value.Data!).ErrorType == ErrorType.Circular)
                            isCircularGroup = true;
                    }
                }

                executionContext.ClearExecuting();

                _environment.SetCellValue(vertex.Region!.Top, vertex.Region!.Left, vertex.SheetName, value);
            }
        }

        sheet.EndBatchUpdates();
        IsCalculating = false;
    }

    /// <summary>
    /// Returns whether a string is a formula - but not necessarily valid.
    /// </summary>
    /// <param name="formula"></param>
    /// <returns></returns>
    public bool IsFormula(string formula)
    {
        return formula.StartsWith('=');
    }

    public void SetVariable(string varName, object value)
    {
        _environment.SetVariable(varName, new CellValue(value));
        foreach (var sheet in _sheets)
        {
            CalculateSheet(sheet);
        }
    }
}