using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Model.Ops.Definition;
using Model.OpsNew;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StrangeCustoms.Tracks.Industries;
using Track;
using UnityEngine;

namespace StrangeCustoms.Tracks;

[JsonObject(/*Could not decode attribute arguments.*/)]
public class SerializedComponent
{
	private static FieldRef<RepairTrack, Load> repairPartsToLoad = AccessTools.FieldRefAccess<RepairTrack, Load>("repairPartsLoad");

	public string Type { get; set; }

	public string Name { get; set; }

	public string[] TrackSpans { get; set; }

	public string CarTypeFilter { get; set; }

	public bool SharedStorage { get; set; } = true;

	public string? LoadId { get; set; }

	public float? StorageChangeRate { get; set; }

	public float? MaxStorage { get; set; }

	public bool? OrderAroundEmpties { get; set; }

	public float? CarTransferRate { get; set; }

	public bool? OrderAroundLoaded { get; set; }

	public string[]? InputSpans { get; set; }

	public string[]? OutputSpans { get; set; }

	public float? CarLoadPeriod { get; set; }

	public float? CarLengthFeet { get; set; }

	public Dictionary<string, float>? InputTermsPerDay { get; set; }

	public Dictionary<string, float>? OutputTermsPerDay { get; set; }

	public bool? CanOverhaul { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JToken>? ExtraData { get; set; }

	public SerializedComponent()
	{
	}

	public SerializedComponent(IndustryComponent component)
	{
		Type = ((object)component).GetType().FullName;
		Name = ((Object)component).name;
		CarTypeFilter = component.carTypeFilter.queryString;
		TrackSpans = component.TrackSpans.Select((TrackSpan s) => s.id).ToArray();
		SharedStorage = component.sharedStorage;
		IndustryLoaderBase val = (IndustryLoaderBase)(object)((component is IndustryLoaderBase) ? component : null);
		if (val != null)
		{
			LoadId = val.load.id;
			MaxStorage = val.maxStorage;
			StorageChangeRate = val.productionRate;
			OrderAroundEmpties = val.orderEmpties;
			IndustryLoader val2 = (IndustryLoader)(object)((component is IndustryLoader) ? component : null);
			if (val2 != null)
			{
				CarTransferRate = val2.carLoadRate;
				OrderAroundLoaded = val2.orderAwayLoaded;
			}
			else
			{
				TeleportLoadingIndustry val3 = (TeleportLoadingIndustry)(object)((component is TeleportLoadingIndustry) ? component : null);
				if (val3 != null)
				{
					InputSpans = val3.inputSpans.Select((TrackSpan s) => s.id).ToArray();
					OutputSpans = val3.outputSpans.Select((TrackSpan s) => s.id).ToArray();
					CarLoadPeriod = val3.carLoadPeriod;
					CarLengthFeet = val3.carLengthFeet;
				}
			}
		}
		else
		{
			IndustryUnloader val4 = (IndustryUnloader)(object)((component is IndustryUnloader) ? component : null);
			if (val4 != null)
			{
				LoadId = val4.load.id;
				MaxStorage = val4.maxStorage;
				CarTransferRate = val4.carUnloadRate;
				StorageChangeRate = val4.storageConsumptionRate;
				OrderAroundEmpties = val4.orderAwayEmpties;
				OrderAroundLoaded = val4.orderLoads;
			}
			else
			{
				FormulaicIndustryComponent val5 = (FormulaicIndustryComponent)(object)((component is FormulaicIndustryComponent) ? component : null);
				if (val5 != null)
				{
					InputTermsPerDay = val5.inputTerms.ToDictionary((Term p) => p.load.id, (Term p) => p.unitsPerDay);
					OutputTermsPerDay = val5.outputTerms.ToDictionary((Term p) => p.load.id, (Term p) => p.unitsPerDay);
				}
				else
				{
					InterchangedIndustryLoader val6 = (InterchangedIndustryLoader)(object)((component is InterchangedIndustryLoader) ? component : null);
					if (val6 != null)
					{
						LoadId = val6.load.id;
					}
					else
					{
						RepairTrack val7 = (RepairTrack)(object)((component is RepairTrack) ? component : null);
						if (val7 != null)
						{
							LoadId = repairPartsToLoad.Invoke(val7).id;
							CanOverhaul = val7.canOverhaul;
						}
					}
				}
			}
		}
		if (component is ICustomIndustryComponent customIndustryComponent)
		{
			customIndustryComponent.SerializeComponent(this);
		}
	}

	internal void ApplyTo(IndustryComponent gameComponent, PatchingContext ctx)
	{
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Expected O, but got Unknown
		if (((object)gameComponent).GetType().FullName != Type)
		{
			throw new SCPatchingException("Cannot change industry component type: '" + gameComponent.Identifier + "' cannot be changed from " + ((object)gameComponent).GetType().FullName + " to " + Type, "type");
		}
		((Object)gameComponent).name = Name ?? throw new SCPatchingException("Component missing a name", "name");
		gameComponent.trackSpans = TrackSpans?.Select(ctx.GetSpan).ToArray() ?? Array.Empty<TrackSpan>();
		gameComponent.carTypeFilter = new CarTypeFilter(CarTypeFilter);
		gameComponent.sharedStorage = SharedStorage;
		IndustryLoaderBase val = (IndustryLoaderBase)(object)((gameComponent is IndustryLoaderBase) ? gameComponent : null);
		if (val != null)
		{
			ApplyTo(val, ctx);
			IndustryLoader val2 = (IndustryLoader)(object)((gameComponent is IndustryLoader) ? gameComponent : null);
			if (val2 != null)
			{
				ApplyTo(val2, ctx);
			}
			else
			{
				TeleportLoadingIndustry val3 = (TeleportLoadingIndustry)(object)((gameComponent is TeleportLoadingIndustry) ? gameComponent : null);
				if (val3 != null)
				{
					ApplyTo(val3, ctx);
				}
			}
		}
		else
		{
			IndustryUnloader val4 = (IndustryUnloader)(object)((gameComponent is IndustryUnloader) ? gameComponent : null);
			if (val4 != null)
			{
				ApplyTo(val4, ctx);
			}
			else
			{
				FormulaicIndustryComponent val5 = (FormulaicIndustryComponent)(object)((gameComponent is FormulaicIndustryComponent) ? gameComponent : null);
				if (val5 != null)
				{
					ApplyTo(val5, ctx);
				}
				else
				{
					InterchangedIndustryLoader val6 = (InterchangedIndustryLoader)(object)((gameComponent is InterchangedIndustryLoader) ? gameComponent : null);
					if (val6 != null)
					{
						ApplyTo(val6, ctx);
					}
					else
					{
						RepairTrack val7 = (RepairTrack)(object)((gameComponent is RepairTrack) ? gameComponent : null);
						if (val7 != null)
						{
							ApplyTo(val7, ctx);
						}
						else if (gameComponent is TeamTrack)
						{
							ctx.Logger.Warning("Team Tracks are only supported in Railroader 2024.6.3 and later.");
						}
					}
				}
			}
		}
		if (gameComponent is ICustomIndustryComponent customIndustryComponent)
		{
			customIndustryComponent.DeserializeComponent(this, ctx);
		}
	}

	private void ApplyTo(IndustryLoaderBase lb, PatchingContext ctx)
	{
		if (TrackSpans.Length == 0)
		{
			throw new SCPatchingException("At least one TrackSpan must be specified.", "trackSpans");
		}
		lb.load = ctx.GetLoad(LoadId ?? throw new SCPatchingException("No LoadId specified", "loadId"));
		lb.orderEmpties = OrderAroundEmpties ?? lb.orderEmpties;
		lb.productionRate = StorageChangeRate ?? lb.productionRate;
		lb.maxStorage = MaxStorage ?? lb.maxStorage;
	}

	private void ApplyTo(IndustryLoader ll, PatchingContext ctx)
	{
		ll.carLoadRate = CarTransferRate ?? ll.carLoadRate;
		ll.orderAwayLoaded = OrderAroundLoaded ?? ll.orderAwayLoaded;
	}

	private void ApplyTo(IndustryUnloader ul, PatchingContext ctx)
	{
		if (TrackSpans.Length == 0)
		{
			throw new SCPatchingException("At least one TrackSpan must be specified.", "trackSpans");
		}
		ul.load = ctx.GetLoad(LoadId ?? throw new SCPatchingException("No LoadId specified", "loadId"));
		ul.maxStorage = MaxStorage ?? ul.maxStorage;
		ul.carUnloadRate = CarTransferRate ?? ul.carUnloadRate;
		ul.storageConsumptionRate = StorageChangeRate ?? ul.storageConsumptionRate;
		ul.orderAwayEmpties = OrderAroundEmpties ?? ul.orderAwayEmpties;
		ul.orderLoads = OrderAroundLoaded ?? ul.orderLoads;
	}

	private void ApplyTo(TeleportLoadingIndustry tp, PatchingContext ctx)
	{
		tp.inputSpans = InputSpans.Select((string s) => ctx.GetSpan(s ?? throw new SCPatchingException("NULL in InputSpans", "inputSpans"))).ToArray();
		tp.outputSpans = OutputSpans.Select((string s) => ctx.GetSpan(s ?? throw new SCPatchingException("NULL in OutputSpans", "outputSpans"))).ToArray();
		tp.carLoadPeriod = CarLoadPeriod ?? tp.carLoadPeriod;
		tp.carLengthFeet = CarLengthFeet ?? tp.carLengthFeet;
	}

	private void ApplyTo(FormulaicIndustryComponent fc, PatchingContext ctx)
	{
		fc.inputTerms.Clear();
		fc.outputTerms.Clear();
		foreach (KeyValuePair<string, float> item in InputTermsPerDay ?? throw new SCPatchingException("InputTermsPerDay is not set", "inputTermsPerDay"))
		{
			if (item.Value <= 0f)
			{
				throw new SCPatchingException("InputTerm should be > 0. If you want to remove it, use `\"" + item.Key + "\": { \"$remove\": true }` instead.", "inputTermsPerDay");
			}
		}
		foreach (KeyValuePair<string, float> item2 in OutputTermsPerDay ?? throw new SCPatchingException("OutputTermsPerDay is not set", "outputTermsPerDay"))
		{
			if (item2.Value <= 0f)
			{
				throw new SCPatchingException("OutputTerm should be > 0. If you want to remove it, use `\"" + item2.Key + "\": { \"$remove\": true }` instead.", "outputTermsPerDay");
			}
		}
		fc.inputTerms.AddRange(InputTermsPerDay.Select<KeyValuePair<string, float>, Term>((KeyValuePair<string, float> s) => new Term
		{
			load = ctx.GetLoad(s.Key),
			unitsPerDay = s.Value
		}));
		fc.outputTerms.AddRange(OutputTermsPerDay.Select<KeyValuePair<string, float>, Term>((KeyValuePair<string, float> s) => new Term
		{
			load = ctx.GetLoad(s.Key),
			unitsPerDay = s.Value
		}));
	}

	internal void ApplyTo(InterchangedIndustryLoader ll, PatchingContext ctx)
	{
		if (TrackSpans.Length == 0)
		{
			throw new SCPatchingException("At least one TrackSpan must be specified.", "trackSpans");
		}
		ll.load = ctx.GetLoad(LoadId ?? throw new SCPatchingException("No LoadId specified", "loadId"));
	}

	internal void ApplyTo(RepairTrack rt, PatchingContext ctx)
	{
		if (TrackSpans.Length == 0)
		{
			throw new SCPatchingException("At least one TrackSpan must be specified.", "trackSpans");
		}
		repairPartsToLoad.Invoke(rt) = ctx.GetLoad(LoadId ?? throw new SCPatchingException("No LoadId specified", "loadId"));
		rt.canOverhaul = CanOverhaul.Value;
	}

	internal IndustryComponent Create(Industry industry)
	{
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Expected O, but got Unknown
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected O, but got Unknown
		if (!GraphPatcher.TryGetType<IndustryComponent>(Type, out Type type))
		{
			throw new ArgumentException("Could not find industry component type '" + Type + "'");
		}
		GameObject val = null;
		if (type == typeof(FormulaicIndustryComponent))
		{
			val = ((Component)industry).gameObject;
		}
		if ((Object)(object)val == (Object)null)
		{
			val = new GameObject("Component");
			val.transform.parent = ((Component)industry).transform;
		}
		return (IndustryComponent)val.AddComponent(type);
	}
}
