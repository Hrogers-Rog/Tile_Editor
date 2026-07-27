using System;
using System.Collections.Generic;
using System.Linq;
using KeyValue.Runtime;
using Model.OpsNew;
using UnityEngine;

namespace StrangeCustoms.Tracks;

public class SerializedArea
{
	public string Name { get; set; } = "[UNNAMED NEW AREA]";

	public Vector3 Position { get; set; }

	public float Radius { get; set; }

	public float[] TagColor { get; set; } = new float[3];

	public Dictionary<string, SerializedIndustry> Industries { get; set; }

	public int Order { get; set; }

	public SerializedArea()
	{
	}

	public SerializedArea(Area area)
	{
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		Name = ((Object)area).name;
		Position = ((Component)area).transform.localPosition;
		Radius = area.radius;
		TagColor = new float[3]
		{
			area.tagColor.r,
			area.tagColor.g,
			area.tagColor.b
		};
		Industries = area.Industries.ToDictionary((Industry p) => p.identifier, (Industry p) => new SerializedIndustry(p));
	}

	internal void ApplyTo(Area gameArea, PatchingContext ctx)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		ctx.Logger.Debug<string>("Changing {Area}...", gameArea.identifier);
		((Object)gameArea).name = Name;
		((Component)gameArea).transform.localPosition = Position;
		gameArea.radius = Radius;
		gameArea.tagColor = new Color(TagColor[0], TagColor[1], TagColor[2]);
		try
		{
			Dictionary<string, Industry> dictionary = gameArea.Industries.ToDictionary((Industry p) => p.identifier);
			foreach (KeyValuePair<string, SerializedIndustry> industry in Industries)
			{
				string key = industry.Key;
				SerializedIndustry value = industry.Value;
				string text = key;
				if (value != null && value.Validate(text, ctx))
				{
					if (!dictionary.TryGetValue(text, out var value2))
					{
						GameObject val = new GameObject(text);
						val.SetActive(false);
						val.transform.parent = ((Component)gameArea).transform;
						value2 = val.AddComponent<Industry>();
						value2.identifier = text;
						val.SetActive(true);
					}
					try
					{
						value.ApplyTo(value2, ctx);
						dictionary.Remove(text);
					}
					catch (SCPatchingException ex) when (ex.JsonPath != null)
					{
						throw new SCPatchingException(ex, text);
					}
					catch (Exception ex2)
					{
						throw new SCPatchingException("While rematerializing industry " + text + ", an error occurred: " + ex2.Message, ex2);
					}
				}
			}
			ctx.Logger.Debug<int>("Deleting {Count} industries...", dictionary.Count);
			KeyValueObject val2 = default(KeyValueObject);
			foreach (Industry value3 in dictionary.Values)
			{
				ctx.Logger.Debug<string, int>("Deleting {Industry} ({Code})", value3.identifier, ((Object)value3).GetInstanceID());
				if (((Component)value3).gameObject.TryGetComponent<KeyValueObject>(ref val2))
				{
					Object.DestroyImmediate((Object)(object)val2);
				}
				IndustryComponent[] componentsInChildren = ((Component)value3).GetComponentsInChildren<IndustryComponent>();
				for (int num = 0; num < componentsInChildren.Length; num++)
				{
					Object.DestroyImmediate((Object)(object)componentsInChildren[num]);
				}
				Object.DestroyImmediate((Object)(object)value3);
			}
		}
		catch (SCPatchingException ex3) when (ex3.JsonPath != null)
		{
			throw new SCPatchingException(ex3, "industries");
		}
	}
}
