using Newtonsoft.Json;
using UnityEngine;

namespace StrangeCustoms.Horns;

internal class CustomHornLayer
{
	public string File { get; set; }

	public CustomKeyFrame[] Keyframes { get; set; }

	[JsonIgnore]
	internal AudioClip? Clip { get; set; }
}
