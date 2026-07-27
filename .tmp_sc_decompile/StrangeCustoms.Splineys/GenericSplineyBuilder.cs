using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StrangeCustoms.Splineys;

public class GenericSplineyBuilder<T> : ISplineyBuilder where T : GenericSpliney
{
	public GameObject BuildSpliney(string id, Transform parentTransform, JObject data)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected O, but got Unknown
		GameObject val = new GameObject(id);
		val.transform.parent = parentTransform;
		val.AddComponent<T>().Deserialize(data);
		return val;
	}
}
