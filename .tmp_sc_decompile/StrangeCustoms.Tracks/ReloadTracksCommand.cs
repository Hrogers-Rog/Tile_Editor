using System;
using Railloader;
using UI.Console;

namespace StrangeCustoms.Tracks;

[ConsoleCommand("/sc-tracks-reload", "Reloads the tracks JSONs.")]
internal class ReloadTracksCommand : IConsoleCommand
{
	public string Execute(string[] components)
	{
		try
		{
			GraphPatcher trackPatcher = SingletonPluginBase<StrangeCustomsPlugin>.Shared.TrackPatcher;
			if (trackPatcher == null)
			{
				return "No track patcher found";
			}
			trackPatcher.Patch();
			return "OK";
		}
		catch (Exception ex)
		{
			return "Patching went wrong; check logfile: " + ex.Message;
		}
	}
}
