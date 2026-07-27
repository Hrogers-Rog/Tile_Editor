using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StrangeCustoms;

public struct GraphJsonWillDeserializeEvent
{
	private Patcher patcher;

	public IReadOnlyDictionary<string, string> ChangedKeys => patcher.Touchers;

	internal GraphJsonWillDeserializeEvent(Patcher patcher)
	{
		this.patcher = patcher;
	}

	public void ApplyPatch(string patchSource, JObject patch)
	{
		patcher.ApplyPatch(patchSource, patch);
	}
}
