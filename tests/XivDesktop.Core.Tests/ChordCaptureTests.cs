using XivDesktop.Core.Input;

namespace XivDesktop.Core.Tests;

public class ChordCaptureTests
{
    private static Func<int, bool> Down(params int[] keys) => keys.Contains;

    [Fact]
    public void CapturesModifiersAndDoesNotTreatModifierAloneAsTheKey()
    {
        var capture = new ChordCapture();
        capture.Begin(Down(), 0);
        Assert.Null(capture.Update(Down(KeyChord.VkLControl, KeyChord.VkRShift), 10));
        Assert.True(capture.Active);
        Assert.Equal(KeyChord.Parse("Ctrl+Shift+D"), capture.Update(Down(KeyChord.VkLControl, KeyChord.VkRShift, 'D'), 20));
        Assert.False(capture.Active);
        Assert.Null(capture.Update(Down('A'), 30));
    }

    [Fact]
    public void HeldKeysMustBeReleasedBeforeTheyCanBeRecorded()
    {
        var capture = new ChordCapture();
        capture.Begin(Down(0x0D), 0);
        Assert.Null(capture.Update(Down(0x0D), 100));
        Assert.Null(capture.Update(Down(), 200));
        Assert.Equal(KeyChord.Parse("Enter"), capture.Update(Down(0x0D), 300));
    }

    [Fact]
    public void EscapeCancelsButModifiedEscapeCanBeBound()
    {
        var capture = new ChordCapture();
        capture.Begin(Down(), 0);
        Assert.Null(capture.Update(Down(0x1B), 100));
        Assert.False(capture.Active);
        capture.Begin(Down(), 200);
        Assert.Equal(KeyChord.Parse("Alt+Escape"), capture.Update(Down(KeyChord.VkMenu, 0x1B), 300));
    }

    [Fact]
    public void TimeoutReportsWhetherAModifierWasEverObserved()
    {
        var capture = new ChordCapture();
        capture.Begin(Down(), 0);
        Assert.Null(capture.Update(Down(), ChordCapture.TimeoutMs));
        Assert.False(capture.Active);
        Assert.Contains("No modifiers reached", capture.Message);
        capture.Begin(Down(), 20000);
        capture.Update(Down(KeyChord.VkLWin), 20001);
        capture.Update(Down(), 30000);
        Assert.Contains("Modifiers reached", capture.Message);
    }

    [Fact]
    public void UnsupportedKeysAndMouseClicksAreIgnored()
    {
        var capture = new ChordCapture();
        capture.Begin(Down(), 0);
        Assert.Null(capture.Update(Down(1, 0xFF), 1));
        Assert.True(capture.Active);
        Assert.Equal(KeyChord.Parse("F12"), capture.Update(Down(0x7B), 2));
    }
}
