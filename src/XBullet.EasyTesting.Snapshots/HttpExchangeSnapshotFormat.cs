namespace XBullet.EasyTesting.Snapshots;

/// <summary>Selects the committed file format for complete HTTP exchange snapshots.</summary>
public enum HttpExchangeSnapshotFormat
{
    /// <summary>Stores the exchange as structured JSON in a <c>.verified.json</c> file.</summary>
    Json,

    /// <summary>Stores the exchange as an HTTP-style transcript in a <c>.verified.txt</c> file.</summary>
    Http,

    /// <summary>Stores the exchange as YAML in a <c>.verified.yaml</c> file.</summary>
    Yaml
}
