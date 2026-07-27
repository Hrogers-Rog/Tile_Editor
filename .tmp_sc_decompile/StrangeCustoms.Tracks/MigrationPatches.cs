using System;
using System.Collections.Generic;
using System.IO;
using Game.Messages;
using Game.Persistence;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Railloader;
using Serilog;

namespace StrangeCustoms.Tracks;

[HarmonyPatch(typeof(WorldStore), "Migrate", new Type[] { typeof(Snapshot) })]
internal static class MigrationPatches
{
	internal class MigrationHolder
	{
		public Dictionary<string, string> WaybillDestinations { get; set; } = new Dictionary<string, string>();

		public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();

		public Dictionary<string, string> CarTypes { get; set; } = new Dictionary<string, string>();
	}

	private static void Postfix(Snapshot snapshot)
	{
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_0281: Unknown result type (might be due to invalid IL or missing references)
		//IL_028e: Unknown result type (might be due to invalid IL or missing references)
		//IL_029b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		//IL_020f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0217: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_01db: Unknown result type (might be due to invalid IL or missing references)
		//IL_0227: Unknown result type (might be due to invalid IL or missing references)
		//IL_0235: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0312: Unknown result type (might be due to invalid IL or missing references)
		ILogger val = Log.ForContext(typeof(MigrationPatches));
		IEnumerator<ModMixinto> enumerator = SingletonPluginBase<StrangeCustomsPlugin>.Shared.GetMixintos("game-migrations", (MixintoType)1).GetEnumerator();
		if (!enumerator.MoveNext())
		{
			val.Information("No game migrations found. Skiiiip.");
			return;
		}
		Patcher patcher = new Patcher(JObject.FromObject((object)new MigrationHolder(), GraphPatcher.Serializer));
		do
		{
			ModMixinto current = enumerator.Current;
			try
			{
				if (!File.Exists(((ModMixinto)(ref current)).Mixinto))
				{
					throw new FileNotFoundException("Mixinto does not exist", ((ModMixinto)(ref current)).Mixinto);
				}
				val.Debug<string, string>("Applying migration {ModId}/{Mixinto}", ((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto);
				patcher.ApplyPatch(((ModMixinto)(ref current)).Mixinto, JObject.Parse(File.ReadAllText(((ModMixinto)(ref current)).Mixinto)));
			}
			catch (Exception ex)
			{
				val.Error<string, string, string>(ex, "Error while applying migration {ModId}/{Mixinto}: {ExceptionMessage}", ((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto, ex.Message);
			}
		}
		while (enumerator.MoveNext());
		MigrationHolder migrationHolder = ((JToken)patcher.Value).ToObject<MigrationHolder>(GraphPatcher.Serializer);
		List<(string, string)> list = new List<(string, string)>();
		foreach (KeyValuePair<string, Car> car in snapshot.Cars)
		{
			if (migrationHolder.CarTypes.TryGetValue(car.Value.prototypeId, out string value))
			{
				list.Add((car.Key, value));
			}
			if (snapshot.Properties.TryGetValue(car.Key, out var value2) && value2.TryGetValue("ops.waybill", out var value3) && value3 is DictionaryPropertyValue val2)
			{
				if (val2.Value.TryGetValue("originId", out var value4) && value4 is StringPropertyValue val3 && migrationHolder.WaybillDestinations.TryGetValue(val3.Value, out string value5))
				{
					val2.Value["originId"] = (IPropertyValue)(object)new StringPropertyValue(value5);
				}
				if (val2.Value.TryGetValue("destId", out var value6) && value6 is StringPropertyValue val4 && migrationHolder.WaybillDestinations.TryGetValue(val4.Value, out value5))
				{
					val2.Value["destId"] = (IPropertyValue)(object)new StringPropertyValue(value5);
				}
			}
		}
		val.Verbose<List<(string, string)>>("Car patches: {@CarPatches}", list);
		foreach (var item2 in list)
		{
			Dictionary<string, Car> cars = snapshot.Cars;
			string item = item2.Item1;
			Car value7 = snapshot.Cars[item2.Item1];
			value7.prototypeId = item2.Item2;
			cars[item] = value7;
		}
		foreach (KeyValuePair<string, string> property in migrationHolder.Properties)
		{
			if (snapshot.Properties.TryGetValue(property.Key, out var value8))
			{
				snapshot.Properties[property.Value] = value8;
				snapshot.Properties.Remove(property.Key);
			}
		}
	}
}
