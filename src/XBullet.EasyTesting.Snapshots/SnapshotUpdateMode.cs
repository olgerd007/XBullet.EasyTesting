namespace XBullet.EasyTesting.Snapshots;

/// <summary>Controls when received snapshots are automatically promoted to verified snapshots.</summary>
public enum SnapshotUpdateMode
{
    /// <summary>Never update verified snapshots automatically.</summary>
    None,

    /// <summary>Automatically approve snapshots only when no verified snapshot exists.</summary>
    Missing,

    /// <summary>Automatically create and overwrite verified snapshots.</summary>
    All
}
