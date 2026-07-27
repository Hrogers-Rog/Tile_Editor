using System.Collections.Generic;
using System.IO;
using UI.Console;
using UnityEngine;

namespace StrangeCustoms;

[ConsoleCommand("/sc-dump-mandelas", "Dumps all currently visible MeshRenderer paths. Expensive operation.")]
public class DumpMandelasCommand : IConsoleCommand
{
	public string Execute(string[] components)
	{
		List<string> list = new List<string>();
		Renderer[] array = Object.FindObjectsByType<Renderer>((FindObjectsInactive)1, (FindObjectsSortMode)0);
		foreach (Renderer val in array)
		{
			list.Add(((Component)val).transform.GetAbsolutePath());
		}
		list.Sort();
		File.WriteAllLines("dumped-mandelas.txt", list);
		return "Dumped to dumped-mandelas.txt";
	}
}
