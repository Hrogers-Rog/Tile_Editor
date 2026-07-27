using Map.Runtime.MaskComponents;

namespace StrangeCustoms;

internal class FlowyData
{
	public SerializedRiverPoint[] Points { get; set; }

	public string Profile { get; set; }

	public RiverPathStyle Style { get; set; }

	public float OffsetY { get; set; } = -0.1f;
}
