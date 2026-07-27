using System.Collections.Generic;
using UI.Console;
using UnityEngine;

namespace StrangeCustoms.Horns;

[ConsoleCommand("/sc-horns-reload", "Reloads all horn information")]
internal class ReloadHornsCommand : IConsoleCommand
{
	public string Execute(string[] components)
	{
		List<CustomHornProfile> profiles = FrenchHorn.LoadHornFiles(forceRefresh: true);
		HornController[] array = Resources.FindObjectsOfTypeAll<HornController>();
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Reload(profiles);
		}
		return "Reloaded.";
	}
}
