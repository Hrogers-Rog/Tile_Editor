using UI.Console;

namespace StrangeCustoms.Whistles;

[ConsoleCommand("/sc-whistles-reload", "Reloads all whistle information")]
internal class ReloadWhistlesCommand : IConsoleCommand
{
	public string Execute(string[] components)
	{
		SpanishWhistle.LoadWhistleDefinitions(forceRefresh: true);
		return "Reloaded. Re-assign whistle to take effect.";
	}
}
