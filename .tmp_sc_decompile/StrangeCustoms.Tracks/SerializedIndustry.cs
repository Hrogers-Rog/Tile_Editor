using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model.OpsNew;
using UnityEngine;

namespace StrangeCustoms.Tracks;

public class SerializedIndustry
{
	private static readonly FieldRef<Industry, IndustryComponent[]> _cachedComponents = AccessTools.FieldRefAccess<Industry, IndustryComponent[]>("_cachedComponents");

	public string Name { get; set; }

	public Vector3 LocalPosition { get; set; }

	public bool UsesContract { get; set; }

	public Dictionary<string, SerializedComponent> Components { get; set; }

	public SerializedIndustry()
	{
	}

	public SerializedIndustry(Industry industry)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		Name = ((Object)industry).name;
		LocalPosition = ((Component)industry).transform.localPosition;
		UsesContract = industry.usesContract;
		Components = industry.Components.ToDictionary((IndustryComponent p) => p.subIdentifier, (IndustryComponent p) => new SerializedComponent(p));
	}

	internal void ApplyTo(Industry gameIndustry, PatchingContext ctx)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		((Object)((Component)gameIndustry).gameObject).name = Name;
		((Component)gameIndustry).transform.localPosition = LocalPosition;
		gameIndustry.usesContract = UsesContract;
		Dictionary<string, IndustryComponent> dictionary = gameIndustry.Components.ToDictionary((IndustryComponent p) => p.subIdentifier);
		bool activeSelf = ((Component)gameIndustry).gameObject.activeSelf;
		foreach (KeyValuePair<string, SerializedComponent> component in Components)
		{
			string key = component.Key;
			SerializedComponent value = component.Value;
			string text = key;
			if (value != null)
			{
				if (!dictionary.TryGetValue(text, out var value2))
				{
					((Component)gameIndustry).gameObject.SetActive(false);
					value2 = value.Create(gameIndustry);
					value2.subIdentifier = text;
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
					throw new SCPatchingException("While rematerializing component " + text + ", an exception occurred: " + ex2.Message, text);
				}
			}
		}
		foreach (IndustryComponent value3 in dictionary.Values)
		{
			Object.DestroyImmediate((Object)(object)value3);
		}
		_cachedComponents.Invoke(gameIndustry) = null;
		((Component)gameIndustry).gameObject.SetActive(activeSelf);
	}

	internal bool Validate(string id, PatchingContext ctx)
	{
		bool result = true;
		if (Components == null)
		{
			ctx.Logger.Error<string>("Industry '{Id}' has no components defined", id);
			result = false;
		}
		return result;
	}
}
