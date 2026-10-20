using AudioLeash;
using Xunit;

namespace AudioLeash.Tests;

public class PriorityListEditorTests
{
    private static DeviceRow Row(string id, string name, bool connected = true, string? desc = null)
        => new(id, name, desc, connected);

    private static string[] Ids(IEnumerable<DeviceRow> rows) => rows.Select(r => r.Id).ToArray();

    [Fact]
    public void Constructor_KeepsRankedOrderAndSortsUnranked()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("b", "B"), new RankedDevice("a", "A")],
            [Row("a", "A"), Row("b", "B"), Row("z", "Zed"), Row("m", "mid"), Row("c", "Charlie")]);

        Assert.Equal(["b", "a"], Ids(editor.Ranked));
        Assert.Equal(["c", "m", "z"], Ids(editor.Unranked));
    }

    [Fact]
    public void Constructor_RankedDeviceNotKnown_KeepsSlotWithPersistedNameAndDisconnected()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("gone", "Old Name", "Old Desc")],
            [Row("x", "X")]);

        var row = Assert.Single(editor.Ranked);
        Assert.Equal("gone", row.Id);
        Assert.Equal("Old Name", row.Name);
        Assert.Equal("Old Desc", row.Description);
        Assert.False(row.IsConnected);
    }

    [Fact]
    public void Constructor_RankedDeviceKnown_UsesCurrentNameAndConnection()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "Old", "Old Desc")],
            [Row("a", "New", connected: true, desc: "New Desc")]);

        var row = Assert.Single(editor.Ranked);
        Assert.Equal("New", row.Name);
        Assert.Equal("New Desc", row.Description);
        Assert.True(row.IsConnected);
        Assert.Empty(editor.Unranked);
    }

    [Fact]
    public void Constructor_DuplicateKnownIds_FirstWins()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "A")],
            [Row("a", "First"), Row("a", "Second"), Row("u", "First U"), Row("u", "Second U")]);

        Assert.Equal("First", Assert.Single(editor.Ranked).Name);
        Assert.Equal("First U", Assert.Single(editor.Unranked).Name);
    }

    [Fact]
    public void MoveUp_WithinRanked_SwapsWithPrevious()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "A"), new RankedDevice("b", "B")],
            [Row("a", "A"), Row("b", "B")]);

        Assert.True(editor.MoveUp("b"));
        Assert.Equal(["b", "a"], Ids(editor.Ranked));
    }

    [Fact]
    public void MoveUp_FirstRanked_ReturnsFalse()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "A"), new RankedDevice("b", "B")],
            [Row("a", "A"), Row("b", "B")]);

        Assert.False(editor.MoveUp("a"));
        Assert.Equal(["a", "b"], Ids(editor.Ranked));
        Assert.False(editor.MoveUp("unknown"));
    }

    [Fact]
    public void MoveDown_LastRanked_UnranksIt()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "A"), new RankedDevice("b", "B")],
            [Row("a", "A"), Row("b", "B"), Row("c", "C")]);

        Assert.True(editor.MoveDown("b"));
        Assert.Equal(["a"], Ids(editor.Ranked));
        Assert.Equal(["b", "c"], Ids(editor.Unranked));
    }

    [Fact]
    public void MoveUp_UnrankedRow_AppendsToRanked()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "A")],
            [Row("a", "A"), Row("b", "B"), Row("c", "C")]);

        Assert.True(editor.MoveUp("c"));
        Assert.Equal(["a", "c"], Ids(editor.Ranked));
        Assert.Equal(["b"], Ids(editor.Unranked));
    }

    [Fact]
    public void MoveDown_UnrankedRow_ReturnsFalse()
    {
        var editor = new PriorityListEditor([], [Row("a", "A")]);

        Assert.False(editor.MoveDown("a"));
        Assert.False(editor.MoveDown("unknown"));
        Assert.Equal(["a"], Ids(editor.Unranked));
    }

    [Fact]
    public void MoveTo_UnrankedIntoRankedAtIndex_InsertsThere()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "A"), new RankedDevice("b", "B")],
            [Row("a", "A"), Row("b", "B"), Row("c", "C")]);

        editor.MoveTo("c", toRanked: true, index: 1);

        Assert.Equal(["a", "c", "b"], Ids(editor.Ranked));
        Assert.Empty(editor.Unranked);
    }

    [Fact]
    public void MoveTo_RankedIntoUnranked_RemovesRankAndKeepsSorted()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("b", "Bravo"), new RankedDevice("c", "Charlie")],
            [Row("a", "Alpha"), Row("b", "Bravo"), Row("c", "Charlie"), Row("d", "Delta")]);

        editor.MoveTo("c", toRanked: false, index: 0);

        Assert.Equal(["b"], Ids(editor.Ranked));
        Assert.Equal(["a", "c", "d"], Ids(editor.Unranked));
    }

    [Fact]
    public void MoveTo_IndexOutOfRange_Clamps()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "A"), new RankedDevice("b", "B")],
            [Row("a", "A"), Row("b", "B"), Row("c", "C"), Row("d", "D")]);

        editor.MoveTo("c", toRanked: true, index: 99);
        Assert.Equal(["a", "b", "c"], Ids(editor.Ranked));

        editor.MoveTo("d", toRanked: true, index: -5);
        Assert.Equal(["d", "a", "b", "c"], Ids(editor.Ranked));

        editor.MoveTo("unknown", toRanked: true, index: 0);
        Assert.Equal(["d", "a", "b", "c"], Ids(editor.Ranked));
    }

    [Fact]
    public void ToRankedList_ReflectsCurrentOrderAndRefreshedNames()
    {
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "Old A", "dA"), new RankedDevice("b", "B")],
            [Row("a", "New A", desc: "nA"), Row("b", "B"), Row("c", "C")]);
        editor.MoveUp("b");
        editor.MoveUp("c");

        Assert.Equal(
            [new RankedDevice("b", "B"), new RankedDevice("a", "New A", "nA"), new RankedDevice("c", "C")],
            editor.ToRankedList());
    }
    // Display layout for DropAtGap: ranked rows, the divider, then unranked rows.
    // Gap g is the slot just above display row g (g == row count means the very end).

    private static PriorityListEditor ThreeRankedTwoUnranked() => new(
        [new RankedDevice("a", "A"), new RankedDevice("b", "B"), new RankedDevice("c", "C")],
        [Row("a", "A"), Row("b", "B"), Row("c", "C"), Row("x", "X"), Row("y", "Y")]);

    [Fact]
    public void DropAtGap_RankedRowDraggedDown_LandsAboveTheTargetRow()
    {
        var editor = ThreeRankedTwoUnranked();

        editor.DropAtGap("a", gap: 2); // between b and c

        Assert.Equal(["b", "a", "c"], Ids(editor.Ranked));
    }

    [Fact]
    public void DropAtGap_RankedRowDraggedUp_LandsAboveTheTargetRow()
    {
        var editor = ThreeRankedTwoUnranked();

        editor.DropAtGap("c", gap: 0);

        Assert.Equal(["c", "a", "b"], Ids(editor.Ranked));
    }

    [Fact]
    public void DropAtGap_JustAboveDivider_MovesToEndOfRanked()
    {
        var editor = ThreeRankedTwoUnranked();

        editor.DropAtGap("a", gap: 3);
        Assert.Equal(["b", "c", "a"], Ids(editor.Ranked));

        editor.DropAtGap("x", gap: 3);
        Assert.Equal(["b", "c", "a", "x"], Ids(editor.Ranked));
        Assert.Equal(["y"], Ids(editor.Unranked));
    }

    [Fact]
    public void DropAtGap_OwnSlot_IsNoOp()
    {
        var editor = ThreeRankedTwoUnranked();

        editor.DropAtGap("b", gap: 1);
        editor.DropAtGap("b", gap: 2);

        Assert.Equal(["a", "b", "c"], Ids(editor.Ranked));
    }

    [Fact]
    public void DropAtGap_BelowDivider_Unranks()
    {
        var editor = ThreeRankedTwoUnranked();

        editor.DropAtGap("b", gap: 4); // just below the divider
        Assert.Equal(["a", "c"], Ids(editor.Ranked));
        Assert.Equal(["b", "x", "y"], Ids(editor.Unranked));

        editor.DropAtGap("a", gap: 99); // past the end
        Assert.Equal(["c"], Ids(editor.Ranked));
        Assert.Equal(["a", "b", "x", "y"], Ids(editor.Unranked));
    }

    [Fact]
    public void DropAtGap_UnrankedRowIntoRanked_InsertsAtGap()
    {
        var editor = ThreeRankedTwoUnranked();

        editor.DropAtGap("y", gap: 1);

        Assert.Equal(["a", "y", "b", "c"], Ids(editor.Ranked));
        Assert.Equal(["x"], Ids(editor.Unranked));
    }

    // ── HasChanges ──────────────────────────────────────────────────────

    private static PriorityListEditor TwoRanked() => new(
        [new RankedDevice("a", "A"), new RankedDevice("b", "B")],
        [Row("a", "A"), Row("b", "B"), Row("c", "C")]);

    [Fact]
    public void HasChanges_Untouched_IsFalse() => Assert.False(TwoRanked().HasChanges);

    [Fact]
    public void HasChanges_AfterReorder_IsTrue()
    {
        var editor = TwoRanked();
        editor.MoveUp("b");
        Assert.True(editor.HasChanges);
    }

    [Fact]
    public void HasChanges_AfterRanking_IsTrue()
    {
        var editor = TwoRanked();
        editor.MoveUp("c");
        Assert.True(editor.HasChanges);
    }

    [Fact]
    public void HasChanges_MovedAndMovedBack_IsFalse()
    {
        var editor = TwoRanked();
        editor.MoveUp("b");
        editor.MoveUp("a");
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void HasChanges_DuplicateIdsInPersistedList_UntouchedIsFalse()
    {
        // The editor collapses duplicates; that cleanup alone isn't a user change.
        var editor = new PriorityListEditor(
            [new RankedDevice("a", "A"), new RankedDevice("a", "A again")], [Row("a", "A")]);
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void HasChanges_RankedThenUnrankedAgain_IsFalse()
    {
        var editor = TwoRanked();
        editor.MoveUp("c");   // ranks c at the end
        editor.MoveDown("c"); // last ranked row: unranks it again
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void HasChanges_EverythingUnranked_IsTrue()
    {
        var editor = TwoRanked();
        editor.MoveTo("a", toRanked: false, index: 0);
        editor.MoveTo("b", toRanked: false, index: 0);
        Assert.True(editor.HasChanges);
    }
}
