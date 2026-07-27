using Newtonsoft.Json.Linq;
using StrangeCustoms.Tracks;
using UnityEngine;

namespace StrangeCustoms.Splineys;

public abstract class GenericSpliney : MonoBehaviour
{
	public abstract void Deserialize(JObject data);
}
public abstract class GenericSpliney<TSettings> : GenericSpliney
{
	public override void Deserialize(JObject data)
	{
		Deserialize(((JToken)data).ToObject<TSettings>(GraphPatcher.Serializer));
	}

	protected abstract void Deserialize(TSettings settings);
}
