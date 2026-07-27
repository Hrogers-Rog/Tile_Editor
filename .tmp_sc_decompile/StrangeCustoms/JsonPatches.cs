using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Definition;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railloader;
using Serilog;

namespace StrangeCustoms;

[HarmonyPatch]
internal static class JsonPatches
{
	private static JsonSerializer? serializer;

	[HarmonyPatch(typeof(ContainerSerialization), "JsonSerializerSettings")]
	[HarmonyReversePatch(/*Could not decode attribute arguments.*/)]
	private static JsonSerializerSettings JsonSerializerSettings()
	{
		throw new NotImplementedException();
	}

	private static Container CustomDeserialization(string text, string identifier)
	{
		JObject result;
		return CustomDeserialization(text, identifier, returnJObject: false, out result);
	}

	internal static void RewriteObject(JObject obj)
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Expected O, but got Unknown
		JProperty val = obj.Property("$objectsByIdentifier", StringComparison.OrdinalIgnoreCase);
		if (val == null)
		{
			return;
		}
		JToken value = val.Value;
		JObject val2 = (JObject)(object)((value is JObject) ? value : null);
		if (val2 == null)
		{
			return;
		}
		JArray val3 = AssureObjects();
		foreach (KeyValuePair<string, JToken> item in val2)
		{
			if (item.Value != null)
			{
				JObject val4 = (JObject)item.Value;
				val4["$find"] = JToken.FromObject((object)new PatchFind[1]
				{
					new PatchFind
					{
						Path = "identifier",
						Value = JToken.op_Implicit(item.Key),
						Comparison = Comparison.Equals
					}
				});
				val3.Add((JToken)(object)val4);
			}
		}
		((JToken)val).Remove();
		JArray AssureObjects()
		{
			//IL_003b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0041: Expected O, but got Unknown
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Expected O, but got Unknown
			//IL_0038: Expected O, but got Unknown
			JProperty obj2 = obj.Property("objects", StringComparison.OrdinalIgnoreCase);
			JToken val5 = ((obj2 != null) ? obj2.Value : null);
			if (val5 == null)
			{
				JObject obj3 = obj;
				JArray val6 = new JArray();
				JToken val7 = (JToken)val6;
				obj3["objects"] = (JToken)val6;
				val5 = val7;
			}
			return (JArray)val5;
		}
	}

	internal static Container CustomDeserialization(string text, string identifier, bool returnJObject, out JObject? result)
	{
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		ILogger val = Log.ForContext(typeof(JsonPatches));
		IEnumerator<ModMixinto> enumerator = SingletonPluginBase<StrangeCustomsPlugin>.Shared.GetMixintos("container:" + identifier, (MixintoType)1).GetEnumerator();
		Patcher patcher = null;
		Dictionary<(string, string), string> failures = SingletonPluginBase<StrangeCustomsPlugin>.Shared.Failures;
		if (enumerator.MoveNext())
		{
			val.Debug<string>("Applying generic container patches for {Identifier}...", identifier);
			if (patcher == null)
			{
				patcher = new Patcher(JObject.Parse(text));
			}
			do
			{
				ModMixinto current = enumerator.Current;
				(string, string) key = (((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto);
				failures.Remove(key);
				try
				{
					if (!File.Exists(((ModMixinto)(ref current)).Mixinto))
					{
						failures.Add(key, "Mixinto file does not exist: " + ((ModMixinto)(ref current)).Mixinto);
						continue;
					}
					val.Debug<string, string>("Applying {Mod}/{File}...", ((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto);
					JObject val2 = JObject.Parse(File.ReadAllText(((ModMixinto)(ref current)).Mixinto));
					RewriteObject(val2);
					patcher.ApplyPatch(((ModMixinto)(ref current)).Mixinto, val2);
				}
				catch (Exception ex)
				{
					val.Error(ex, "Could not apply {Mod}/{File} to {Identifier}: {ExceptionMessage}", new object[4]
					{
						((IModDefinition)((ModMixinto)(ref current)).Source).Id,
						((ModMixinto)(ref current)).Mixinto,
						identifier,
						ex.Message
					});
					failures.Add(key, ex.Message);
				}
			}
			while (enumerator.MoveNext());
			if (serializer == null)
			{
				serializer = JsonSerializer.CreateDefault(JsonSerializerSettings());
			}
			result = (returnJObject ? patcher.Value : null);
			return ((JToken)patcher.Value).ToObject<Container>(serializer);
		}
		result = null;
		return ContainerSerialization.Deserialize(text);
	}

	[HarmonyPatch(typeof(AssetPackRuntimeStore), "Container")]
	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> SwapContainerDeserialization(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo call = SymbolExtensions.GetMethodInfo((Expression<Action>)(() => ContainerSerialization.Deserialize((string)null)));
		MethodInfo ourMethod = SymbolExtensions.GetMethodInfo((Expression<Action>)(() => CustomDeserialization(null, null)));
		MethodInfo prop = AccessTools.PropertyGetter(typeof(AssetPackRuntimeStore), "Identifier");
		foreach (CodeInstruction instruction in instructions)
		{
			if (CodeInstructionExtensions.Calls(instruction, call))
			{
				yield return new CodeInstruction(OpCodes.Ldarg_0, (object)null);
				yield return new CodeInstruction(OpCodes.Call, (object)prop);
				yield return new CodeInstruction(OpCodes.Call, (object)ourMethod);
			}
			else
			{
				yield return instruction;
			}
		}
	}
}
