namespace AGSTools.Tests;

public class EditHistoryTests
{
    private sealed class Item
    {
        public Item(string value) => Value = value;
        public string Value { get; set; }
    }

    private static readonly DateTime T0 = new(2026, 11, 21, 12, 0, 0);

    private readonly EditHistory<Item> _history = new(limit: 3);

    // Sets a value the way the editor does: the change is recorded
    private void Edit(Item item, string value, DateTime time)
    {
        string before = item.Value;
        item.Value = value;
        _history.Record(item, before, value, time);
    }

    private static void Set(Item item, string value) => item.Value = value;

    [Fact]
    public void Typing_in_one_entry_is_one_step_and_a_pause_starts_another()
    {
        var a = new Item("");
        Edit(a, "W", T0);
        Edit(a, "Wo", T0.AddSeconds(0.5));
        Edit(a, "Wo bin", T0.AddSeconds(1));
        Edit(a, "Wo bin ich?", T0.AddSeconds(5)); // after a pause

        _history.Undo(Set);
        Assert.Equal("Wo bin", a.Value);
        _history.Undo(Set);
        Assert.Equal("", a.Value);
        Assert.False(_history.CanUndo);
    }

    [Fact]
    public void Undo_and_redo_go_back_and_forth_across_entries()
    {
        Item a = new("a"), b = new("b");
        Edit(a, "A", T0);
        Edit(b, "B", T0.AddSeconds(0.1));

        Assert.Equal(new[] { b }, _history.Undo(Set));
        Assert.Equal(("A", "b"), (a.Value, b.Value));
        Assert.Equal(new[] { a }, _history.Undo(Set));
        Assert.Equal(("a", "b"), (a.Value, b.Value));

        _history.Redo(Set);
        _history.Redo(Set);
        Assert.Equal(("A", "B"), (a.Value, b.Value));
        Assert.False(_history.CanRedo);
    }

    [Fact]
    public void A_batch_is_one_step_with_its_label()
    {
        Item a = new(""), b = new(""), c = new("");
        _history.BeginBatch("Auto-translate");
        Edit(a, "Eins", T0);
        Edit(b, "Zwei", T0.AddMinutes(1));
        Edit(a, "Eins!", T0.AddMinutes(2)); // same entry twice: back to its first value
        _history.EndBatch();
        Edit(c, "Drei", T0.AddMinutes(3));

        _history.Undo(Set);
        Assert.Equal("Auto-translate", _history.UndoLabel);
        Assert.Equal(new[] { b, a }, _history.Undo(Set));
        Assert.Equal(("", "", ""), (a.Value, b.Value, c.Value));
    }

    [Fact]
    public void Changes_while_undoing_are_not_recorded_and_a_new_edit_clears_redo()
    {
        var a = new Item("x");
        Edit(a, "y", T0);
        // The setter of a real entry raises a change event, which records again: must be ignored
        _history.Undo((item, value) => Edit(item, value, T0.AddSeconds(10)));
        Assert.Equal("x", a.Value);
        Assert.False(_history.CanUndo);
        Assert.True(_history.CanRedo);

        Edit(a, "z", T0.AddSeconds(20));
        Assert.False(_history.CanRedo);
    }

    [Fact]
    public void Only_the_newest_steps_are_kept()
    {
        var a = new Item("0");
        for (int i = 1; i <= 5; i++)
            Edit(a, i.ToString(), T0.AddMinutes(i));

        while (_history.CanUndo)
            _history.Undo(Set);
        Assert.Equal("2", a.Value); // limit 3: steps 3, 4, 5
    }

    [Fact]
    public void Typing_after_undo_or_redo_starts_a_new_step()
    {
        var a = new Item("");
        Edit(a, "ab", T0);
        _history.Undo(Set);
        _history.Redo(Set);
        Edit(a, "abc", T0.AddSeconds(0.1));

        _history.Undo(Set);
        Assert.Equal("ab", a.Value);
    }
}
