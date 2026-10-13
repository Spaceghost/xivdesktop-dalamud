using System.Numerics;

namespace XivDesktop.Core.Tests;

public class ControllerRingTests
{
    private static AppInfo App(string id) => new() { Id = id, Name = id };
    private static AppInfo[] Apps => Enumerable.Range(0, 8).Select(i => App(i.ToString())).ToArray();

    [Fact]
    public void FlickAndReleaseLaunchTheSnapshotFavouriteOnce()
    {
        var ring = new ControllerRing();
        ring.Update(true, Vector2.UnitX, false, false, false, true, Apps);
        Assert.Equal(2, ring.Selected);
        ring.Update(true, Vector2.Zero, false, false, false, true, [App("changed")]);
        Assert.Equal("2", ring.Update(false, Vector2.Zero, false, false, false, true, []).LaunchId);
        Assert.Null(ring.Update(false, Vector2.Zero, false, false, false, true, []).LaunchId);
        Assert.False(ring.Open);
    }

    [Fact]
    public void NeutralReleaseAndLostFocusNeverLaunch()
    {
        var ring = new ControllerRing();
        ring.Update(true, Vector2.Zero, false, false, false, true, Apps);
        Assert.Null(ring.Update(false, Vector2.Zero, false, false, false, true, Apps).LaunchId);
        ring.Update(true, Vector2.UnitY, false, false, false, true, Apps);
        ring.Update(true, Vector2.UnitY, false, false, false, false, Apps);
        ring.Update(true, Vector2.UnitY, false, false, false, true, Apps);
        Assert.False(ring.Open);
        Assert.Null(ring.Update(false, Vector2.Zero, false, false, false, true, Apps).LaunchId);
        ring.Update(true, Vector2.UnitY, false, false, false, true, Apps);
        Assert.True(ring.Open);
    }

    [Fact]
    public void ShoulderStepsUseEdgesAndOpposingPressesCancel()
    {
        var ring = new ControllerRing();
        Assert.Equal(1, ring.Update(true, Vector2.Zero, false, true, false, true, Apps).WorkspaceStep);
        Assert.Equal(0, ring.Update(true, Vector2.Zero, false, true, false, true, Apps).WorkspaceStep);
        ring.Update(true, Vector2.Zero, false, false, false, true, Apps);
        Assert.Equal(0, ring.Update(true, Vector2.Zero, true, true, false, true, Apps).WorkspaceStep);
        ring.Update(true, Vector2.Zero, false, false, false, true, Apps);
        Assert.Equal(-1, ring.Update(true, Vector2.Zero, true, false, false, true, Apps).WorkspaceStep);
    }

    [Fact]
    public void CancelRequiresReleaseAndFavouriteCountIsBounded()
    {
        var ring = new ControllerRing();
        ring.Update(true, Vector2.UnitY, false, false, false, true, Apps.Concat(Apps));
        Assert.Equal(8, ring.Apps.Count);
        ring.Update(true, Vector2.UnitY, false, false, true, true, Apps);
        ring.Update(true, Vector2.UnitY, false, false, false, true, Apps);
        Assert.False(ring.Open);
        Assert.Null(ring.Update(false, Vector2.Zero, false, false, false, true, Apps).LaunchId);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 2)]
    [InlineData(0, -1, 4)]
    [InlineData(-1, 0, 6)]
    [InlineData(0.1f, 0.1f, -1)]
    public void StickMapsToClockwiseSectors(float x, float y, int expected)
        => Assert.Equal(expected, ControllerRing.Sector(new(x,y), 8));
}
