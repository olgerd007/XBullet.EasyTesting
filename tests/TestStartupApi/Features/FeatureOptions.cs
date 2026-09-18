namespace TestStartupApi.Features;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    public bool OrderItems { get; set; }
}

public static class FeatureNames
{
    public const string OrderItems = nameof(FeatureOptions.OrderItems);
}
