using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StrangeCustoms.Tracks;

internal class Vector3Converter : JsonConverter<Vector3>
{
	public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Invalid comparison between Unknown and I4
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Invalid comparison between Unknown and I4
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		JToken val = serializer.Deserialize<JToken>(reader);
		if ((int)val.Type == 1)
		{
			return new Vector3(val.Value<float>((object)"x"), val.Value<float>((object)"y"), val.Value<float>((object)"z"));
		}
		if ((int)val.Type == 2)
		{
			return new Vector3(val.Value<float>((object)0), val.Value<float>((object)1), val.Value<float>((object)2));
		}
		throw new ArgumentException($"Invalid JSON: {val.Type} cannot be converted to Vector3");
	}

	public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		serializer.Serialize(writer, (object)new { value.x, value.y, value.z });
	}
}
