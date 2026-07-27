using Audio;
using Newtonsoft.Json;

namespace StrangeCustoms.HellsBells;

internal class CustomBellProfile
{
	public string Name { get; set; }

	public string File { get; set; }

	public float[]? IndexTimes { get; set; }

	[JsonIgnore]
	public IndexedClipDescriptor? IndexedClip { get; set; }

	[JsonIgnore]
	public IndexedClipDescriptor? OriginalClip { get; set; }
}
