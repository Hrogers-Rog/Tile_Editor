using System;
using System.Collections.Generic;
using Audio;
using HarmonyLib;
using KeyValue.Runtime;
using Model;
using Model.Definition.Components;
using Serilog;
using UnityEngine;

namespace StrangeCustoms.Horns;

internal class HornController : MonoBehaviour
{
	private static ILogger logger = Log.ForContext<HornController>();

	private static readonly FieldRef<HornPlayer, IAudioSource> _sourceA = AccessTools.FieldRefAccess<HornPlayer, IAudioSource>("_sourceA");

	private static readonly FieldRef<HornPlayer, IAudioSource> _sourceB = AccessTools.FieldRefAccess<HornPlayer, IAudioSource>("_sourceB");

	private IDisposable? customizationObserver;

	private KeyValueObject kvo;

	private Car car;

	private HornProfile? defaultProfile;

	private string? profileName;

	private void Awake()
	{
		kvo = ((Component)this).GetComponentInParent<KeyValueObject>();
		car = ((Component)this).GetComponentInParent<Car>();
	}

	private void OnEnable()
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		OnValueSet(kvo["horn.custom"]);
	}

	private void OnDestroy()
	{
		customizationObserver?.Dispose();
		customizationObserver = null;
	}

	public void Configure(HornComponent component)
	{
		if ((Object)(object)kvo == (Object)null)
		{
			kvo = ((Component)this).GetComponentInParent<KeyValueObject>();
		}
		if ((Object)(object)car == (Object)null)
		{
			car = ((Component)this).GetComponentInParent<Car>();
		}
		customizationObserver = kvo.Observe("horn.custom", (Action<Value>)OnValueSet, false);
	}

	private void OnValueSet(Value value)
	{
		string name = ((Value)(ref value)).StringValue;
		logger.Debug<string, string>("Set horn of {Car} to {Value}", car.DisplayName, name);
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}
		if ((Object)(object)defaultProfile == (Object)null)
		{
			defaultProfile = ((Component)this).GetComponentInChildren<HornPlayer>()?.profile;
		}
		profileName = name;
		if (name == "Default")
		{
			ApplyProfile(defaultProfile);
			return;
		}
		CustomHornProfile customHornProfile = FrenchHorn.LoadHornFiles().Find((CustomHornProfile s) => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		if (customHornProfile == null)
		{
			logger.Warning<string>("Could not find local horn with name {Name}", name);
			return;
		}
		logger.Debug<string, CustomHornProfile>("Load horn {Horn} for {Car}...", name, customHornProfile);
		customHornProfile.LoadFor(this);
	}

	public void ApplyProfile(CustomHornProfile profile)
	{
		if (!(profile.Name != profileName))
		{
			ApplyProfile(profile.Profile);
		}
	}

	private void ApplyProfile(HornProfile profile)
	{
		HornPlayer componentInChildren = ((Component)car).GetComponentInChildren<HornPlayer>();
		componentInChildren.profile = profile;
		if (_sourceA.Invoke(componentInChildren) != null)
		{
			_sourceA.Invoke(componentInChildren).clip = profile.layers[0].clip;
			_sourceB.Invoke(componentInChildren).clip = profile.layers[1].clip;
		}
	}

	internal void Reload(List<CustomHornProfile> profiles)
	{
		profiles.Find((CustomHornProfile p) => p.Name == profileName)?.LoadFor(this);
	}
}
