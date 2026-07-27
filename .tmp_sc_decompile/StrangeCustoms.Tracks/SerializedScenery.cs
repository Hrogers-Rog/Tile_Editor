using System.Collections.Generic;
using Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StrangeCustoms.Tracks;

public class SerializedScenery
{
	public string ModelIdentifier { get; set; }

	public Vector3 Position { get; set; }

	public Vector3 Rotation { get; set; } = Vector3.zero;

	public Vector3 Scale { get; set; } = Vector3.one;

	[JsonExtensionData]
	public Dictionary<string, JToken>? ExtraData { get; set; }

	public SerializedScenery()
	{
	}//IL_0001: Unknown result type (might be due to invalid IL or missing references)
	//IL_0006: Unknown result type (might be due to invalid IL or missing references)
	//IL_000c: Unknown result type (might be due to invalid IL or missing references)
	//IL_0011: Unknown result type (might be due to invalid IL or missing references)


	public SerializedScenery(SceneryAssetInstance scenery)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		ModelIdentifier = scenery.identifier;
		Position = WorldTransformer.WorldToGame(((Component)scenery).transform.position);
		Rotation = ((Component)scenery).transform.eulerAngles;
		Scale = ((Component)scenery).transform.localScale;
	}
}
