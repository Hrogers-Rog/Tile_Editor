using Newtonsoft.Json;

namespace StrangeCustoms.Tracks.InformationDump;

internal class InformationStep
{
	public int Step { get; set; }

	public string ModId { get; set; }

	public string Mixinto { get; set; }

	[JsonProperty(/*Could not decode attribute arguments.*/)]
	public string? Exception { get; set; }
}
