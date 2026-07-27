using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StrangeCustoms;

public interface ISplineyBuilder
{
	GameObject BuildSpliney(string id, Transform parentTransform, JObject data);
}
