using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetPack.Runtime;
using HarmonyLib;
using Model.Database;
using Model.Definition;
using Newtonsoft.Json.Linq;
using Serilog;
using UI.Console;

namespace StrangeCustoms.Patching;

[ConsoleCommand("/sc-patches", "Re-runs all previously run patches and log the results to the console.")]
internal class VerifyPatchesCommand : IConsoleCommand
{
	public string Execute(string[] components)
	{
		int num = components.Length;
		if ((num < 2 || num > 3) ? true : false)
		{
			return $"Expected 1-2 argument, got {components.Length - 1}. Usage: /sc-patches <mixinto-name> [print|reload]";
		}
		string[] parts = components[1].Split(new char[1] { ':' }, 2);
		IPrefabStore prefabStore = TrainController.Shared.PrefabStore;
		if (parts[0] != "container")
		{
			return "Unsupported scheme " + parts[0];
		}
		if (!(AccessTools.Field(((object)prefabStore).GetType(), "_stores").GetValue(prefabStore) is List<AssetPackRuntimeStore> list))
		{
			return "Unable to load stores.";
		}
		AssetPackRuntimeStore val = list.Find((AssetPackRuntimeStore s) => s.Identifier == parts[1]);
		if (val == null)
		{
			return "Unable to find store with identifier " + parts[1] + ". Valid options: " + string.Join(", ", list.Select((AssetPackRuntimeStore s) => s.Identifier));
		}
		if (parts[0] == "container")
		{
			string text = "DefinitionsPath";
			string text2 = text;
			JObject result;
			Container value = JsonPatches.CustomDeserialization(File.ReadAllText((string)AccessTools.PropertyGetter(((object)val).GetType(), text2).Invoke(val, null)), parts[1], returnJObject: true, out result);
			if (components.Length == 2)
			{
				return "Success";
			}
			text = components[2];
			if (!(text == "print"))
			{
				if (text == "reload")
				{
					AccessTools.Field(((object)val).GetType(), "_container").SetValue(val, value);
					return "Experimentally re-loaded the data.";
				}
				return "Unknown command " + components[2];
			}
			Log.ForContext<VerifyPatchesCommand>().Information<string, string>("Final result for {Identifier}: {Result:l}", components[1], ((object)result)?.ToString());
			return "Final JSON printed to log file.";
		}
		throw new NotImplementedException();
	}
}
