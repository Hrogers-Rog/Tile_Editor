using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace StrangeCustoms.Tracks;

internal class UpdateWatcher : IDisposable
{
	private ILogger logger = Log.ForContext<UpdateWatcher>();

	private Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);

	private DateTime notBefore = DateTime.MaxValue;

	private readonly GraphPatcher patcher;

	private CancellationTokenSource cts = new CancellationTokenSource();

	public UpdateWatcher(GraphPatcher patcher)
	{
		this.patcher = patcher;
		RunAsync(cts.Token);
	}

	public void AddFile(string fileName)
	{
		if (!watchers.ContainsKey(fileName))
		{
			FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(Path.GetDirectoryName(fileName), Path.GetFileName(fileName));
			fileSystemWatcher.EnableRaisingEvents = true;
			fileSystemWatcher.Changed += OnEvent;
			fileSystemWatcher.Created += OnEvent;
			fileSystemWatcher.Deleted += OnEvent;
			watchers.Add(fileName, fileSystemWatcher);
		}
	}

	private void OnEvent(object sender, FileSystemEventArgs e)
	{
		logger.Debug<WatcherChangeTypes, string>("Received an event: {ChangeType} {Path}", e.ChangeType, e.FullPath);
		notBefore = DateTime.Now.AddMilliseconds(500.0);
	}

	private async Task RunAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			if (DateTime.Now >= notBefore)
			{
				notBefore = DateTime.MaxValue;
				try
				{
					patcher.Patch();
					Console.Log("Reloaded map-data.");
				}
				catch (Exception ex)
				{
					logger.Error<string>(ex, "Something went terribly wrong while hot-patching the graph: {Message}", ex.Message);
					Console.Log("Could not hot-reload graph: Check the log file for errors: " + ex.Message);
				}
			}
			await Task.Delay(250, ct);
		}
	}

	public void Dispose()
	{
		foreach (FileSystemWatcher value in watchers.Values)
		{
			value.Dispose();
		}
		cts.Cancel();
	}
}
