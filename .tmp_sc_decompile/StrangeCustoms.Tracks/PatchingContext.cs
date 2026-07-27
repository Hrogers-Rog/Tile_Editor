using System.Collections.Generic;
using System.Linq;
using Model;
using Model.Ops.Definition;
using Serilog;
using Track;
using UnityEngine;

namespace StrangeCustoms.Tracks;

public class PatchingContext(ILogger logger, IReadOnlyDictionary<string, string> changedEntries)
{
	private readonly Dictionary<string, Load> loadsById = CarPrototypeLibrary.instance.opsLoads.ToDictionary((Load p) => p.id);

	public ILogger Logger { get; } = logger;

	public Dictionary<string, TrackNode>? NodesById { get; set; }

	public Dictionary<string, TrackSegment>? SegmentsById { get; set; }

	public Dictionary<string, TrackSpan>? SpansById { get; set; }

	public IReadOnlyDictionary<string, string> TouchedKeys { get; } = changedEntries;

	public IEnumerable<Load> AllLoads => loadsById.Values;

	internal void AddLoad(Load load)
	{
		loadsById.Add(load.id, load);
	}

	public Load GetLoad(string id)
	{
		if (!loadsById.TryGetValue(id, out Load value) || !Object.op_Implicit((Object)(object)value))
		{
			throw new SCPatchingException("Cannot find load " + id);
		}
		return value;
	}

	internal void SetLoads()
	{
		CarPrototypeLibrary.instance.opsLoads = loadsById.Values.ToArray();
	}

	internal TrackSpan GetSpan(string id)
	{
		if (!(SpansById ?? throw new SCPatchingException("Spans are unavailable at this time")).TryGetValue(id, out TrackSpan value) || !((Object)(object)value != (Object)null))
		{
			throw new SCPatchingException("Cannot find span " + id);
		}
		return value;
	}
}
