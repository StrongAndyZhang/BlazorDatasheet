using BlazorDatasheet.Model;
using BlazorDatasheet.Render;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDatasheet;

public partial class Datasheet : IHandleEvent
{
    [Parameter] public Sheet? Sheet { get; set; }

    private bool IsDataSheetActive { get; set; }
    private Cell? ActiveCell { get; set; }
    private string? ActiveEditValue { get; set; }
    private bool IsSoftEdit { get; set; }
    private bool IsEditing => ActiveCell != null;
    private bool IsMouseInsideSheet { get; set; }
    private ElementReference ActiveCellInputReference;

    private Dictionary<string, Type> RenderComponentTypes { get; set; }

    protected override void OnInitialized()
    {
        RenderComponentTypes = new Dictionary<string, Type>();
        RenderComponentTypes.Add("text", typeof(TextRenderer));
        RenderComponentTypes.Add("number", typeof(NumberRenderer));
        RenderComponentTypes.Add("boolean", typeof(BoolRenderer));
        base.OnInitialized();
    }

    private Type getCellRendererType(string type)
    {
        if (RenderComponentTypes.ContainsKey(type))
            return RenderComponentTypes[type];

        return typeof(TextRenderer);
    }

    private Dictionary<string, object> getCellRendererParameters(Cell cell)
    {
        return new Dictionary<string, object>()
        {
            { "Cell", cell },
        };
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await AddWindowEventsAsync();
        }

        if (ActiveCell != null)
            await ActiveCellInputReference.FocusAsync();
    }

    private async Task AddWindowEventsAsync()
    {
        await _WindowEventService.Init();
        _WindowEventService.OnKeyDown += HandleWindowKeyDown;
        _WindowEventService.OnMouseDown += HandleWindowMouseDown;
    }

    private void HandleCellMouseUp(int row, int col, MouseEventArgs e)
    {
        if (Sheet.IsSelecting)
        {
            Sheet?.EndSelecting();
            StateHasChanged();
        }
    }

    private void HandleCellMouseDown(int row, int col, MouseEventArgs e)
    {
        if (Sheet?.GetCell(row, col) != ActiveCell)
            AcceptEdit(true);

        if (e.ShiftKey)
            Sheet?.ExtendSelection(row, col);
        else
            Sheet?.BeginSelecting(row, col, !e.MetaKey, SelectionMode.Cell);

        StateHasChanged();
    }

    private void HandleColumnMouseDown(int col, MouseEventArgs e)
    {
        AcceptEdit(false);

        if (e.ShiftKey)
            Sheet?.ExtendSelection(Sheet.Rows, col);
        else
        {
            Sheet?.BeginSelecting(0, col, !e.MetaKey, SelectionMode.Column);
            Sheet?.UpdateSelectingEndPosition(Sheet.Rows, col);
        }

        StateHasChanged();
    }

    private void HandleRowMouseDown(int row, MouseEventArgs e)
    {
        AcceptEdit(false);

        if (e.ShiftKey)
            Sheet?.ExtendSelection(row, Sheet.Cols);
        else
        {
            Sheet?.BeginSelecting(row, 0, !e.MetaKey, SelectionMode.Row);
            Sheet?.UpdateSelectingEndPosition(row, Sheet.Cols);
        }

        StateHasChanged();
    }

    private void HandleCellDoubleClick(int row, int col, MouseEventArgs e)
    {
        BeginEdit(row, col, softEdit: false, clear: false);
        StateHasChanged();
    }

    private void BeginEdit(int row, int col, bool softEdit, bool clear, string entryChar = "")
    {
        var cell = Sheet?.GetCell(row, col);
        ActiveEditValue = clear ? entryChar : cell?.Value + entryChar;
        IsSoftEdit = softEdit;
        ActiveCell = cell;
    }

    private void AcceptEdit(bool stateChanged = true)
    {
        if (!IsEditing)
            return;

        if (ActiveCell != null)
        {
            ActiveCell.Value = ActiveEditValue;
        }

        CancelEdit(true);
        if (stateChanged)
            StateHasChanged();
    }

    private void CancelEdit(bool stateChanged = true)
    {
        ActiveCell = null;
        if (stateChanged)
            StateHasChanged();
    }

    private void HandleCellMouseOver(int row, int col, MouseEventArgs e)
    {
        if (Sheet?.IsSelecting == true)
        {
            if (Sheet.SelectionMode == SelectionMode.Cell)
                Sheet.UpdateSelectingEndPosition(row, col);
            else if (Sheet.SelectionMode == SelectionMode.Column)
                Sheet.UpdateSelectingEndPosition(Sheet.Rows, col);
            else if (Sheet.SelectionMode == SelectionMode.Row)
                Sheet.UpdateSelectingEndPosition(row, Sheet.Cols);
            StateHasChanged();
        }
    }

    private bool HandleWindowMouseDown(MouseEventArgs e)
    {
        bool changed = IsDataSheetActive != IsMouseInsideSheet;
        IsDataSheetActive = IsMouseInsideSheet;

        if (!IsDataSheetActive) // if it is outside
        {
            AcceptEdit();
            changed = true;
        }

        if (changed)
            StateHasChanged();

        return false;
    }

    private bool HandleWindowKeyDown(KeyboardEventArgs e)
    {
        if (!IsDataSheetActive)
            return false;
        if (e.Key == "Enter")
        {
            AcceptEdit();
            Sheet?.MoveSelection(1, 0);
            StateHasChanged();
        }
        else if (e.Key == "Escape")
        {
            CancelEdit();
        }
        else if (e.Key == "ArrowRight")
        {
            if (!IsEditing)
            {
                Sheet?.MoveSelection(0, 1);
                StateHasChanged();
            }
            else if (IsSoftEdit)
            {
                AcceptEdit();
                Sheet?.MoveSelection(0, 1);
                StateHasChanged();
            }

            return true;
        }
        else if (e.Key == "ArrowLeft")
        {
            if (!IsEditing)
            {
                Sheet?.MoveSelection(0, -1);
                StateHasChanged();
            }
            else if (IsSoftEdit)
            {
                AcceptEdit();
                Sheet?.MoveSelection(0, -1);
                StateHasChanged();
            }

            return true;
        }
        else if (e.Key == "ArrowUp")
        {
            if (!IsEditing)
            {
                Sheet?.MoveSelection(-1, 0);
                StateHasChanged();
            }
            else if (IsSoftEdit)
            {
                AcceptEdit();
                Sheet?.MoveSelection(-1, 0);
                StateHasChanged();
            }

            return true;
        }
        else if (e.Key == "ArrowDown")
        {
            if (!IsEditing)
            {
                Sheet?.MoveSelection(1, 0);
                StateHasChanged();
            }
            else if (IsSoftEdit)
            {
                AcceptEdit();
                Sheet?.MoveSelection(1, 0);
                StateHasChanged();
            }

            return true;
        }

        if (e.Key.Length == 1 && !IsEditing && IsDataSheetActive)
        {
            char c = e.Key[0];
            if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                var inputPosition = Sheet?.GetInputForSelection();
                if (inputPosition == null)
                    return false;
                BeginEdit(inputPosition.Row, inputPosition.Col, softEdit: true, clear: true, e.Key);
                StateHasChanged();
            }

            return true;
        }

        return false;
    }

    Task IHandleEvent.HandleEventAsync(
        EventCallbackWorkItem callback, object? arg) => callback.InvokeAsync(arg);

    public void Dispose()
    {
        _WindowEventService.Dispose();
    }
}