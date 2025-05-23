using System;
using System.Collections.Generic;
using System.Linq;
using BlazorDatasheet.Core.Data;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.Util;
using FluentAssertions;
using NUnit.Framework;

namespace BlazorDatasheet.Test.SheetTests;

public class SheetTests
{
    [Test]
    public void Create_Sheet_2x1_Has_Empty_Cells()
    {
        var sheet = new Sheet(2, 1);
        Assert.AreEqual(2, sheet.NumRows);
        Assert.AreEqual(null, sheet.Cells.GetCell(0, 0).Value);
        Assert.AreEqual(null, sheet.Cells.GetCell(1, 0).Value);
    }

    [Test]
    [TestCase(0, 1, 0, 1)]
    [TestCase(0, 1, 0, 0)]
    [TestCase(1, 2, 1, 1)]
    public void Get_delim_Data_from_Sheet(int copyPasteRegionR0, int copyPasteRegionR1, int copyPasteRegionC0,
        int copyPasteRegionC1)
    {
        var sheet = new Sheet(5, 5);
        var copyPasteRegion = new Region(copyPasteRegionR0, copyPasteRegionR1, copyPasteRegionC0, copyPasteRegionC1);

        foreach (var posn in copyPasteRegion)
            sheet.Cells.SetValue(posn.row, posn.col, GetCellPositionString(posn.row, posn.col));

        var copy = sheet.GetRegionAsDelimitedText(copyPasteRegion);
        Assert.NotNull(copy);
        Assert.AreNotEqual(String.Empty, copy);

        // Clear the sheet so we are pasting over empty data
        sheet.Cells.ClearCells(copyPasteRegion);

        var insertedRegions = sheet.InsertDelimitedText(copy, copyPasteRegion.TopLeft);

        Assert.NotNull(insertedRegions);
        Assert.True(insertedRegions!.Equals(copyPasteRegion));

        foreach (var posn in copyPasteRegion)
        {
            var sheetCell = sheet.Cells.GetCell(posn.row, posn.col);
            var cellValue = sheetCell.GetValue<string>();
            var expected = GetCellPositionString(posn.row, posn.col);
            cellValue.Should().Be(expected);
        }
    }

    private string GetCellPositionString(int row, int col)
    {
        return $"({row},{col})";
    }

    [Test]
    public void Range_String_Specification_Tests()
    {
        var sheet = new Sheet(1, 1); // size doesn't matter, could be anything
        sheet.Range("a1")!.Region.Should()
            .BeEquivalentTo(new { Left = 0, Top = 0 }, options => options.ExcludingMissingMembers());
        sheet.Range("b2")!.Region.Should()
            .BeEquivalentTo(new { Left = 1, Top = 1 }, options => options.ExcludingMissingMembers());

        sheet.Range("2a")?.Region.Should().BeNull();

        sheet.Range("A1:B2")!.Region.Should()
            .BeEquivalentTo(new { Left = 0, Top = 0, Right = 1, Bottom = 1 },
                options => options.ExcludingMissingMembers());

        sheet.Range("B:C")!.Region.Should().BeOfType<ColumnRegion>();
        sheet.Range("B:C")!.Region.Should()
            .BeOfType<ColumnRegion>().And
            .BeEquivalentTo(new { Left = 1, Right = 2 }, options => options.ExcludingMissingMembers());

        sheet.Range("2:3")!.Region.Should().BeOfType<RowRegion>();
        sheet.Range("2:3")!.Region.Should()
            .BeEquivalentTo(new { Top = 1, Bottom = 2 }, options => options.ExcludingMissingMembers());

        sheet.Range("2:$3")!.Region.Should().BeOfType<RowRegion>();
        sheet.Range("2:$3")!.Region.Should()
            .BeEquivalentTo(new { Top = 1, Bottom = 2 }, options => options.ExcludingMissingMembers());


        sheet.Range("$2:3")!.Region.Should().BeOfType<RowRegion>();
        sheet.Range("$2:3")!.Region.Should()
            .BeEquivalentTo(new { Top = 1, Bottom = 2 }, options => options.ExcludingMissingMembers());
    }

    [Test]
    [TestCase("")]
    [TestCase("A,1")]
    [TestCase("A:1")]
    [TestCase("1")]
    public void Bad_Range_Strings_return_Empty(string badText)
    {
        var sheet = new Sheet(1, 1);
        sheet.Range(badText)?.Region.Should().BeNull();
    }

    [Test]
    public void BatchChanges_Batches_Changes()
    {
        var sheet = new Sheet(10, 10);
        var posnsChanged = new List<CellPosition>();
        sheet.Cells.CellsChanged += (sender, args) => { posnsChanged = args.Positions.ToList(); };
        sheet.BatchUpdates();
        sheet.Cells.SetValue(0, 0, 0);
        sheet.Cells.SetValue(1, 1, 1);
        sheet.EndBatchUpdates();
        posnsChanged.Should().HaveCount(2);
    }

    [Test]
    public void Cell_Changes_Emits_Cell_ChangeEvent()
    {
        var sheet = new Sheet(10, 10);
        var posnsChanged = new List<CellPosition>();
        sheet.Cells.CellsChanged += (sender,
            args) =>
        {
            posnsChanged = args.Positions.ToList();
        };
        sheet.Cells.SetValue(1, 1, 1);
        posnsChanged.Should().HaveCount(1);
        posnsChanged.First().col.Should().Be(1);
        posnsChanged.First().row.Should().Be(1);
    }

    [Test]
    public void Cancel_Before_Range_Sort_Cancels_Sorting()
    {
        var sheet = new Sheet(10, 10);
        sheet.Range("A1")!.Value = 2;
        sheet.Range("A2")!.Value = 1;

        // disable default sort
        sheet.BeforeRangeSort += (sender, args) =>
        {
            args.Cancel = true;
            args.SortOptions.Should().NotBeNull();
        };

        sheet.SortRange(new ColumnRegion(0));
        sheet.Cells[0, 0].Value.Should().Be(2);
        sheet.Cells[1, 0].Value.Should().Be(1);
    }

    [Test]
    [TestCase("A1", 0, 0)]
    [TestCase("A$1", 0, 0)]
    [TestCase("$A1", 0, 0)]
    [TestCase("C7", 6, 2)]
    [TestCase("$C$7", 6, 2)]
    public void Get_Sheet_Cell_With_Correct_Row_Col(string cellAddress, int row, int col)
    {
        var sheet = new Sheet(100, 100);
        var sheetCell = sheet.Cells[cellAddress];
        sheetCell.Should().NotBeNull();
        sheetCell!.Row.Should().Be(row);
        sheetCell!.Col.Should().Be(col);
    }

    [Test]
    [TestCase("A")]
    [TestCase("3")]
    [TestCase("A1:A3")]
    [TestCase("3A")]
    [TestCase("4:4")]
    [TestCase("4:4")]
    public void Invalid_Single_Cell_Addresses_Should_Return_Null_From_Cell_Store_Access(string cellAddress)
    {
        var sheet = new Sheet(100, 100);
        sheet.Cells[cellAddress].Should().BeNull();
    }

    [Test]
    public void Get_Next_Visible_Col_In_Row_Correct()
    {
        var sheet = new Sheet(100, 200);
        sheet.Cells.SetValue(5, 4, "A");
        sheet.Cells.SetValue(5, 10, "B");
        sheet.Cells.SetValue(5, 1, "C");
        sheet.Cells.GetNextInRow(5, 4)!.Col.Should().Be(10);
        sheet.Cells.GetNextInRow(5, 4, -1)!.Col.Should().Be(1);
    }
}