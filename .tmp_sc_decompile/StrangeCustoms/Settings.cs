namespace StrangeCustoms;

internal class Settings
{
	public bool DecoupleConditionLimits { get; set; }

	public bool RandomizeVisualCondition { get; set; }

	public float RandomMinimumValue { get; set; } = 0.6f;

	public float RandomMaximumValue { get; set; } = 1f;

	public bool AllowTrackAutoReload { get; set; }

	public string? InitialGraphDumpPath { get; set; }

	public string? FinalGraphDumpPath { get; set; }

	public bool DisplayTracker { get; set; }

	public bool LogSkippedEntities { get; set; }

	public bool UseChangeTracking { get; set; }
}
