using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StrangeCustoms;

[JsonObject(/*Could not decode attribute arguments.*/)]
internal class PatchInstructions
{
	[JsonProperty("$add")]
	public JToken? Add { get; set; }

	[JsonProperty("$append")]
	public JToken[]? Append { get; set; }

	[JsonProperty("$find")]
	public PatchFind[]? Find { get; set; }

	[JsonProperty("$replace")]
	public JToken? Replace { get; set; }

	[JsonProperty(/*Could not decode attribute arguments.*/)]
	public bool Remove { get; set; }

	[JsonProperty(/*Could not decode attribute arguments.*/)]
	public bool Optional { get; set; }

	[JsonProperty(/*Could not decode attribute arguments.*/)]
	public bool Clone { get; set; }

	[JsonProperty("$moveTo")]
	public string? MoveTo { get; set; }

	public bool IsValid
	{
		get
		{
			if (Add == null && Append == null && !Remove && Replace == null && Find == null)
			{
				return MoveTo != null;
			}
			return true;
		}
	}
}
