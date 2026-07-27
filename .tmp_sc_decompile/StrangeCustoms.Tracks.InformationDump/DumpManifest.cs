using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Railloader;

namespace StrangeCustoms.Tracks.InformationDump;

internal class DumpManifest : IDisposable
{
	[JsonIgnore]
	private ZipArchive archive;

	public DateTimeOffset DumpStart { get; set; } = DateTimeOffset.UtcNow;

	public bool Successful { get; set; } = true;

	public List<InformationStep> Steps { get; set; } = new List<InformationStep>();

	[JsonIgnore]
	public string? Exception { get; set; }

	public DumpManifest()
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		FileStream fileStream = new FileStream("graph-dump.zip", FileMode.Create);
		archive = new ZipArchive((Stream)fileStream, (ZipArchiveMode)1, false);
	}

	public void WriteOriginalState(JObject state)
	{
		WriteEntry("graph-original.json", (JToken)(object)state);
	}

	public void WriteStep(ModMixinto mixinto, Exception? exception, JObject? state, IReadOnlyDictionary<string, string>? touchers)
	{
		int num = Steps.Count + 1;
		if (state != null)
		{
			WriteEntry($"step-{num:00}-{((IModDefinition)((ModMixinto)(ref mixinto)).Source).Id}.json", (JToken)(object)state);
		}
		if (touchers != null)
		{
			WriteEntry($"changes-{num:00}-{((IModDefinition)((ModMixinto)(ref mixinto)).Source).Id}.json", (JToken)(object)JObject.FromObject((object)touchers));
		}
		if (exception != null)
		{
			Successful = false;
		}
		Steps.Add(new InformationStep
		{
			Step = num,
			Exception = exception?.ToString(),
			Mixinto = ((ModMixinto)(ref mixinto)).Mixinto,
			ModId = ((IModDefinition)((ModMixinto)(ref mixinto)).Source).Id
		});
	}

	public void WriteFinal(JObject state, IReadOnlyDictionary<string, string> touchers)
	{
		WriteEntry("graph-final.json", (JToken)(object)state);
		WriteEntry("changes-final.json", JToken.FromObject((object)touchers));
	}

	public void WriteException(Exception exception)
	{
		Successful = false;
		Exception = exception.ToString();
	}

	private void WriteEntry(string name, JToken obj)
	{
		using Stream stream = archive.CreateEntry(name).Open();
		using StreamWriter streamWriter = new StreamWriter(stream);
		StupidWeirdoJsonWriter stupidWeirdoJsonWriter = new StupidWeirdoJsonWriter(streamWriter);
		try
		{
			GraphPatcher.Serializer.Serialize((JsonWriter)(object)stupidWeirdoJsonWriter, (object)obj);
			streamWriter.Close();
			stream.Close();
		}
		finally
		{
			((IDisposable)stupidWeirdoJsonWriter)?.Dispose();
		}
	}

	public void Dispose()
	{
		WriteEntry("manifest.json", JToken.FromObject((object)this, GraphPatcher.Serializer));
		string text = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		try
		{
			File.Copy("railloader.log", text);
			archive.CreateEntryFromFile(text, "railloader.log");
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
		archive.Dispose();
	}
}
