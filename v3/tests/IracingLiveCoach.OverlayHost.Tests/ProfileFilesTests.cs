using IracingLiveCoach.OverlayHost.Persistence;

namespace IracingLiveCoach.OverlayHost.Tests;

[Collection("ProfileFiles")] // DataDir is process-wide static state
public sealed class ProfileFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lc-profiles-" + Guid.NewGuid().ToString("N"));
    private readonly string _previousDir = ProfileFiles.DataDir;

    public ProfileFilesTests()
    {
        Directory.CreateDirectory(_dir);
        ProfileFiles.DataDir = _dir;
        File.WriteAllText(ProfileFiles.LiveFile, "{\"live\":1}");
    }

    public void Dispose()
    {
        ProfileFiles.DataDir = _previousDir;
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Save_copies_the_live_layout_and_marks_the_profile_active()
    {
        Assert.True(ProfileFiles.Save("Corrida"));
        Assert.Equal(["Corrida"], ProfileFiles.List());
        Assert.Equal("Corrida", ProfileFiles.ActiveName);
        Assert.Equal("{\"live\":1}", File.ReadAllText(ProfileFiles.PathFor("Corrida")));
    }

    [Fact]
    public void Activate_overwrites_the_live_layout_with_the_profile()
    {
        ProfileFiles.Save("A");
        File.WriteAllText(ProfileFiles.LiveFile, "{\"live\":2}");
        Assert.True(ProfileFiles.Activate("A"));
        Assert.Equal("{\"live\":1}", File.ReadAllText(ProfileFiles.LiveFile));
    }

    [Fact]
    public void Backup_keeps_the_previous_layout_hidden_from_the_list()
    {
        ProfileFiles.BackupCurrent();
        Assert.True(File.Exists(Path.Combine(ProfileFiles.Root, "_anterior.json")));
        Assert.Empty(ProfileFiles.List());
    }

    [Fact]
    public void Duplicate_refuses_to_overwrite_and_delete_clears_the_active_marker()
    {
        ProfileFiles.Save("A");
        Assert.True(ProfileFiles.Duplicate("A", "B"));
        Assert.False(ProfileFiles.Duplicate("A", "B"));
        Assert.False(ProfileFiles.Duplicate("missing", "C"));
        Assert.True(ProfileFiles.Delete("A"));
        Assert.Null(ProfileFiles.ActiveName);
        Assert.Equal(["B"], ProfileFiles.List());
    }

    [Fact]
    public void Names_are_sanitised_and_empty_names_are_rejected()
    {
        Assert.False(ProfileFiles.Save("   "));
        Assert.True(ProfileFiles.Save("GT3/Spa:*"));
        Assert.Equal(["GT3_Spa__"], ProfileFiles.List());
        Assert.False(ProfileFiles.Activate("does-not-exist"));
    }

    [Fact]
    public void Save_fails_cleanly_without_a_live_layout()
    {
        File.Delete(ProfileFiles.LiveFile);
        Assert.False(ProfileFiles.Save("A"));
    }
}
