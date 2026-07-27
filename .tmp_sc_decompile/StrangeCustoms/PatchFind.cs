using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StrangeCustoms;

internal class PatchFind
{
	[JsonProperty("path")]
	public string? Path { get; set; }

	[JsonProperty("value")]
	public JToken? Value { get; set; }

	[JsonProperty("comp")]
	public Comparison Comparison { get; set; }

	public override string ToString()
	{
		return $"{Path} {Comparison} {Value}";
	}
}
