using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using AutoTrestle;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using Game.Progression;
using HarmonyLib;
using Helpers;
using KeyValue.Runtime;
using Map.Runtime.MaskComponents;
using Model;
using Model.Ops.Definition;
using Model.OpsNew;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Railloader;
using Serilog;
using StrangeCustoms.Tracks.InformationDump;
using TMPro;
using Track;
using UI.Map;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StrangeCustoms.Tracks;

internal class GraphPatcher : IDisposable
{
	private class PatchState
	{
		internal List<PathSegment> Changes;

		internal Dictionary<string, TrackNode> FinalNodes;

		internal TrackState NewState;

		internal bool LogNoops;

		internal Dictionary<string, TrackNode>? NodesById;

		internal Transform Root;

		internal Graph Graph;

		internal TrackObjectManager TrackObjectManager;

		public UpdateWatcher? fsWatcher;

		public bool UseChangeTracking { get; set; }

		public Patcher Patcher { get; set; }

		public DumpManifest? Dump { get; set; }

		public bool HasAnyChangeMatching(IEnumerable<PathSegment> changeset, params string[] parts)
		{
			if (UseChangeTracking)
			{
				return changeset.Any((PathSegment p) => p.IsSubsetOf(parts));
			}
			return true;
		}

		public void ApplyPatches(IEnumerator<ModMixinto> enumerator)
		{
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			//IL_01df: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_007e: Unknown result type (might be due to invalid IL or missing references)
			//IL_017d: Unknown result type (might be due to invalid IL or missing references)
			Dictionary<(string, string), string> failures = SingletonPluginBase<StrangeCustomsPlugin>.Shared.Failures;
			do
			{
				ModMixinto current = enumerator.Current;
				(string, string) key = (((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto);
				failures.Remove(key);
				if (!File.Exists(((ModMixinto)(ref current)).Mixinto))
				{
					logger.Warning<ModMixinto>("Mixinto {Mixinto} not loaded: File not found", current);
					failures.Add(key, "Mixinto not loaded: File " + ((ModMixinto)(ref current)).Mixinto + " does not exist");
					Dump?.WriteStep(current, new FileNotFoundException("Could not find mixinto", ((ModMixinto)(ref current)).Mixinto), null, null);
					continue;
				}
				fsWatcher?.AddFile(((ModMixinto)(ref current)).Mixinto);
				try
				{
					logger.Debug<string, string>("Applying {ModId}/{Mixinto}", ((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto);
					Patcher.ApplyPatch(((IModDefinition)((ModMixinto)(ref current)).Source).Id + "/" + ((ModMixinto)(ref current)).Mixinto, LoadPatch(((ModMixinto)(ref current)).Mixinto));
					if (UseChangeTracking)
					{
						logger.Debug<Dictionary<string, string>>("Current state: {Patcher}", Patcher.Touchers.ToDictionary<KeyValuePair<string, string>, string, string>((KeyValuePair<string, string> p) => p.Key, (KeyValuePair<string, string> p) => Path.GetFileNameWithoutExtension(p.Value)));
					}
					Dump?.WriteStep(current, null, Patcher.Value, Patcher.Touchers);
				}
				catch (Exception ex)
				{
					logger.Error<string, string, string>(ex, "Error while applying track patch {ModId}/{Mixinto}: {ExceptionMessage}", ((IModDefinition)((ModMixinto)(ref current)).Source).Id, ((ModMixinto)(ref current)).Mixinto, ex.Message);
					failures.Add(key, ex.Message);
					Dump?.WriteStep(current, ex, Patcher.Value, Patcher.Touchers);
				}
			}
			while (enumerator.MoveNext());
		}

		public void DeserializeNodes()
		{
			//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
			//IL_010a: Unknown result type (might be due to invalid IL or missing references)
			//IL_013a: Unknown result type (might be due to invalid IL or missing references)
			//IL_013f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0144: Unknown result type (might be due to invalid IL or missing references)
			//IL_0148: Unknown result type (might be due to invalid IL or missing references)
			//IL_014f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0192: Unknown result type (might be due to invalid IL or missing references)
			//IL_0197: Unknown result type (might be due to invalid IL or missing references)
			//IL_015d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0162: Unknown result type (might be due to invalid IL or missing references)
			try
			{
				List<PathSegment> list = Changes.Where((PathSegment s) => HasAnyChangeMatching(Changes, "tracks", "nodes")).ToList();
				if (UseChangeTracking && list.Count == 0)
				{
					logger.Debug("No changes to nodes found; skipping node processing.");
				}
				else
				{
					logger.Debug("Process nodes...");
					foreach (KeyValuePair<string, SerializedNode> node in NewState.Tracks.Nodes)
					{
						string key = node.Key;
						SerializedNode value = node.Value;
						string text = key;
						if (value == null)
						{
							continue;
						}
						if (!HasAnyChangeMatching(list, "tracks", "nodes", text))
						{
							if (LogNoops)
							{
								logger.Verbose<string>("Skip node {Id}; not changed", text);
							}
							NodesById.Remove(text);
							continue;
						}
						TrackNode value2;
						bool flag = NodesById.TryGetValue(text, out value2);
						if (!flag)
						{
							GameObject val = new GameObject(text);
							val.SetActive(false);
							val.transform.parent = Root;
							value2 = val.AddComponent<TrackNode>();
							value2.id = text;
							val.SetActive(true);
						}
						else
						{
							Graph.InvalidateNode(value2);
						}
						Transform transform = ((Component)value2).transform;
						Quaternion val2 = Quaternion.Euler(value.Rotation);
						if (transform.localPosition != value.Position || transform.localRotation != val2 || value2.flipSwitchStand != value.FlipSwitchStand)
						{
							value2.flipSwitchStand = value.FlipSwitchStand;
							((Component)value2).transform.SetLocalPositionAndRotation(value.Position, val2);
							if (!flag)
							{
								Graph.AddNode(value2);
							}
							else if ((Object)(object)TrackObjectManager.Graph != (Object)null)
							{
								TrackObjectManager.SetNeedsRebuild(value2);
							}
						}
						else if (!flag)
						{
							Graph.AddNode(value2);
						}
						FinalNodes.Add(text, value2);
						NodesById.Remove(text);
					}
					logger.Debug<int>("Delete {Count} obsolete nodes", NodesById.Count);
					foreach (TrackNode value3 in NodesById.Values)
					{
						Object.Destroy((Object)(object)value3);
					}
				}
				NodesById = null;
			}
			catch (SCPatchingException ex) when (ex.JsonPath != null)
			{
				throw new SCPatchingException(ex, "tracks.nodes");
			}
			catch (Exception ex2)
			{
				logger.Error<string>(ex2, "While rematerializing the nodes, an exception occurred: {Message}", ex2.Message);
				throw new SCPatchingException("Exception while rematerializing the nodes: " + ex2.Message, ex2);
			}
		}
	}

	private static readonly ILogger logger = Log.ForContext<GraphPatcher>();

	private static Dictionary<string, Type?> typeLookup = new Dictionary<string, Type>();

	private static Dictionary<string, object?> instanceLookup = new Dictionary<string, object>();

	private TrackState? originalState;

	private UpdateWatcher? fsWatcher;

	private CancellationTokenSource cts;

	private Dictionary<string, SceneryAssetInstance>? sceneryById = new Dictionary<string, SceneryAssetInstance>();

	private Dictionary<string, GameObject>? splineys;

	internal static JsonSerializerSettings SerializerSettings { get; } = new JsonSerializerSettings
	{
		ContractResolver = (IContractResolver)new DefaultContractResolver
		{
			NamingStrategy = (NamingStrategy)new CamelCaseNamingStrategy
			{
				ProcessDictionaryKeys = false
			}
		},
		Converters = new List<JsonConverter>(2)
		{
			(JsonConverter)(object)new Vector3Converter(),
			(JsonConverter)new StringEnumConverter()
		}
	};

	internal static JsonSerializer Serializer { get; } = JsonSerializer.CreateDefault(SerializerSettings);

	public GraphPatcher(bool allowReloads)
	{
		fsWatcher = (allowReloads ? new UpdateWatcher(this) : null);
	}

	public void Dispose()
	{
		fsWatcher?.Dispose();
	}

	private void LoadOriginalState()
	{
		//IL_02ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_033c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0341: Unknown result type (might be due to invalid IL or missing references)
		if (originalState != null)
		{
			return;
		}
		logger.Debug("Initialize original state...");
		TrackState state = new TrackState();
		logger.Debug("Fetching nodes..");
		TrackNode[] array = Object.FindObjectsByType<TrackNode>((FindObjectsInactive)0, (FindObjectsSortMode)0);
		foreach (TrackNode val in array)
		{
			state.Tracks.Nodes.Add(val.id, new SerializedNode(val));
		}
		logger.Debug("Fetching segments...");
		TrackSegment[] array2 = Object.FindObjectsByType<TrackSegment>((FindObjectsInactive)0, (FindObjectsSortMode)0);
		foreach (TrackSegment val2 in array2)
		{
			state.Tracks.Segments.Add(val2.id, new SerializedSegment(val2));
		}
		logger.Debug("Fetching spans...");
		TrackSpan[] array3 = Object.FindObjectsByType<TrackSpan>((FindObjectsInactive)0, (FindObjectsSortMode)0);
		foreach (TrackSpan val3 in array3)
		{
			state.Tracks.Spans.Add(val3.id, new SerializedSpan(val3));
		}
		logger.Debug("Fetching areas...");
		Area[] array4 = Object.FindObjectsByType<Area>((FindObjectsInactive)0, (FindObjectsSortMode)0);
		foreach (Area val4 in array4)
		{
			state.Areas.Add(val4.identifier, new SerializedArea(val4)
			{
				Order = ((Component)val4).transform.GetSiblingIndex() * 100
			});
		}
		logger.Debug("Fetch loads...");
		Load[] opsLoads = CarPrototypeLibrary.instance.opsLoads;
		foreach (Load val5 in opsLoads)
		{
			state.Loads.Add(val5.id, new SerializedLoad(val5));
		}
		logger.Debug("Fetch signs...");
		state.Texts = (from s in Object.FindObjectsByType<TextSynchronizer>((FindObjectsInactive)1, (FindObjectsSortMode)0)
			select s.text).Concat(from s in Object.FindObjectsByType<MapLabel>((FindObjectsInactive)1, (FindObjectsSortMode)0)
			select ((Component)s).GetComponentInChildren<TMP_Text>().text).Distinct().ToDictionary((string p) => p);
		logger.Debug("Splineys...");
		splineys = new Dictionary<string, GameObject>();
		RiverBuilder[] array5 = Object.FindObjectsByType<RiverBuilder>((FindObjectsInactive)1, (FindObjectsSortMode)0);
		foreach (RiverBuilder val6 in array5)
		{
			RiverPath component = ((Component)val6).GetComponent<RiverPath>();
			Vector3 c = ((Component)val6).transform.position;
			AddSpliney(((Component)val6).gameObject, new
			{
				handler = "StrangeCustoms.FlowyThingBuilder",
				profile = ((Object)FlowyThingBuilder.splineProfile.Invoke(val6)).name,
				style = ((object)System.Runtime.CompilerServices.Unsafe.As<RiverPathStyle, RiverPathStyle>(ref component.style)/*cast due to constrained. prefix*/).ToString(),
				points = component.points.Select((Point s) => new
				{
					position = c + s.position,
					rotation = new
					{
						s.eulerAngles.x,
						s.eulerAngles.y,
						s.eulerAngles.z
					},
					width = s.width
				})
			});
		}
		AutoTrestle[] array6 = Object.FindObjectsByType<AutoTrestle>((FindObjectsInactive)1, (FindObjectsSortMode)0);
		foreach (AutoTrestle val7 in array6)
		{
			Vector3 c2 = ((Component)val7).transform.position;
			AddSpliney(((Component)val7).gameObject, new
			{
				handler = "StrangeCustoms.AutoTrestleBuilder",
				points = val7.controlPoints.Select((ControlPoint s) => new
				{
					position = c2 + s.position,
					rotation = ((Quaternion)(ref s.rotation)).eulerAngles
				}),
				headStyle = ((object)System.Runtime.CompilerServices.Unsafe.As<EndStyle, EndStyle>(ref val7.headStyle)/*cast due to constrained. prefix*/).ToString(),
				tailStyle = ((object)System.Runtime.CompilerServices.Unsafe.As<EndStyle, EndStyle>(ref val7.tailStyle)/*cast due to constrained. prefix*/).ToString()
			});
		}
		originalState = state;
		logger.Debug("Original state completely built");
		void AddSpliney(GameObject rootObject, object data)
		{
			string absolutePath = rootObject.transform.GetAbsolutePath();
			splineys.Add(absolutePath, rootObject);
			state.Splineys.Add(absolutePath, JObject.FromObject(data, Serializer));
		}
	}

	internal static void SaveObject(JObject obj, string path)
	{
		Serializer.Formatting = (Formatting)1;
		using StreamWriter textWriter = new StreamWriter(path, append: false);
		StupidWeirdoJsonWriter stupidWeirdoJsonWriter = new StupidWeirdoJsonWriter(textWriter);
		try
		{
			Serializer.Serialize((JsonWriter)(object)stupidWeirdoJsonWriter, (object)obj);
		}
		finally
		{
			((IDisposable)stupidWeirdoJsonWriter)?.Dispose();
		}
	}

	[Conditional("DEBUG")]
	private void NukeNormalGame()
	{
		logger.Warning("NUKING EXISTING MAP.");
		MapFeature[] componentsInChildren = GameObject.Find("MapFeatures").GetComponentsInChildren<MapFeature>();
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			Object.DestroyImmediate((Object)(object)((Component)componentsInChildren[i]).gameObject);
		}
		Progression componentInChildren = GameObject.Find("Progressions").GetComponentInChildren<Progression>();
		AccessTools.FieldRefAccess<Progression, MapFeature[]>("enableFeaturesAtStart").Invoke(componentInChildren) = Array.Empty<MapFeature>();
		Section[] componentsInChildren2 = ((Component)componentInChildren).GetComponentsInChildren<Section>();
		for (int i = 0; i < componentsInChildren2.Length; i++)
		{
			Object.DestroyImmediate((Object)(object)((Component)componentsInChildren2[i]).gameObject);
		}
		Object.DestroyImmediate((Object)(object)GameObject.Find("CTC"));
		Area[] componentsInChildren3 = ((Component)GameObject.Find("Ops").GetComponent<OpsController>()).GetComponentsInChildren<Area>();
		for (int i = 0; i < componentsInChildren3.Length; i++)
		{
			Object.DestroyImmediate((Object)(object)((Component)componentsInChildren3[i]).gameObject);
		}
		Transform transform = GameObject.Find("World").transform;
		for (int num = transform.childCount - 1; num >= 0; num--)
		{
			Transform child = transform.GetChild(num);
			if (!((Object)(object)((Component)child).GetComponent<MonoBehaviour>() != (Object)null))
			{
				Object.DestroyImmediate((Object)(object)((Component)child).gameObject);
			}
		}
		Transform transform2 = ((Component)Object.FindAnyObjectByType<Graph>((FindObjectsInactive)1)).transform;
		for (int num2 = transform2.childCount - 1; num2 >= 0; num2--)
		{
			Object.DestroyImmediate((Object)(object)((Component)transform2.GetChild(num2)).gameObject);
		}
	}

	public void Patch(bool emitZip = false)
	{
		//IL_064a: Unknown result type (might be due to invalid IL or missing references)
		//IL_064f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0656: Unknown result type (might be due to invalid IL or missing references)
		//IL_0663: Unknown result type (might be due to invalid IL or missing references)
		//IL_08b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_08b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_08c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_08c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cbb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cc0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cc7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cd4: Unknown result type (might be due to invalid IL or missing references)
		//IL_1049: Unknown result type (might be due to invalid IL or missing references)
		//IL_104e: Unknown result type (might be due to invalid IL or missing references)
		//IL_1077: Unknown result type (might be due to invalid IL or missing references)
		//IL_107e: Unknown result type (might be due to invalid IL or missing references)
		//IL_1083: Unknown result type (might be due to invalid IL or missing references)
		//IL_1096: Unknown result type (might be due to invalid IL or missing references)
		//IL_147a: Unknown result type (might be due to invalid IL or missing references)
		//IL_1480: Unknown result type (might be due to invalid IL or missing references)
		using DumpManifest dumpManifest = (emitZip ? new DumpManifest() : null);
		IEnumerator<ModMixinto> enumerator = SingletonPluginBase<StrangeCustomsPlugin>.Shared.GetMixintos("game-graph", (MixintoType)1).GetEnumerator();
		if (!enumerator.MoveNext())
		{
			logger.Information("No track patches found. Skip track manipulation.");
			return;
		}
		try
		{
			_ = originalState;
			LoadOriginalState();
			cts?.Cancel();
			logger.Information("Starting graph patching...");
			SerializerSettings.Converters.OfType<Vector3Converter>().First();
			if (fsWatcher != null)
			{
				originalState.Scenery = sceneryById.ToDictionary<KeyValuePair<string, SceneryAssetInstance>, string, SerializedScenery>((KeyValuePair<string, SceneryAssetInstance> p) => p.Key, (KeyValuePair<string, SceneryAssetInstance> p) => new SerializedScenery(p.Value));
			}
			JObject val = JObject.FromObject((object)originalState, Serializer);
			dumpManifest?.WriteOriginalState(val);
			string initialGraphDumpPath = SingletonPluginBase<StrangeCustomsPlugin>.Shared.Settings.InitialGraphDumpPath;
			if (initialGraphDumpPath != null)
			{
				logger.Debug<string>("Saving pre-patched graph to {DumpPath}", initialGraphDumpPath);
				SaveObject(val, initialGraphDumpPath);
			}
			logger.Debug("Adding nodes...");
			Dictionary<string, TrackNode> dictionary = new Dictionary<string, TrackNode>();
			TrackNode[] array = Object.FindObjectsByType<TrackNode>((FindObjectsInactive)0, (FindObjectsSortMode)0);
			foreach (TrackNode val2 in array)
			{
				dictionary.Add(val2.id, val2);
			}
			logger.Debug("Adding segments...");
			Dictionary<string, TrackSegment> dictionary2 = new Dictionary<string, TrackSegment>();
			TrackSegment[] array2 = Object.FindObjectsByType<TrackSegment>((FindObjectsInactive)0, (FindObjectsSortMode)0);
			foreach (TrackSegment val3 in array2)
			{
				dictionary2.Add(val3.id, val3);
			}
			logger.Debug("Adding spans...");
			Dictionary<string, TrackSpan> dictionary3 = new Dictionary<string, TrackSpan>();
			TrackSpan[] array3 = Object.FindObjectsByType<TrackSpan>((FindObjectsInactive)0, (FindObjectsSortMode)0);
			foreach (TrackSpan val4 in array3)
			{
				dictionary3.Add(val4.id, val4);
			}
			logger.Debug("Adding areas...");
			Dictionary<string, Area> dictionary4 = new Dictionary<string, Area>();
			Area[] array4 = Object.FindObjectsByType<Area>((FindObjectsInactive)0, (FindObjectsSortMode)0);
			foreach (Area val5 in array4)
			{
				dictionary4.Add(val5.identifier, val5);
			}
			logger.Debug("Add loads...");
			Dictionary<string, Load> dictionary5 = CarPrototypeLibrary.instance.opsLoads.ToDictionary((Load p) => p.id);
			logger.Debug("Lookup creation complete.");
			Transform transform = ((Component)Object.FindAnyObjectByType<Graph>((FindObjectsInactive)1)).transform;
			Patcher patcher = new Patcher(val);
			bool useChangeTracking = SingletonPluginBase<StrangeCustomsPlugin>.Shared.Settings.UseChangeTracking;
			_ = SingletonPluginBase<StrangeCustomsPlugin>.Shared.Failures;
			PatchState patchState = new PatchState
			{
				Patcher = patcher,
				Dump = dumpManifest,
				UseChangeTracking = useChangeTracking,
				NodesById = dictionary,
				Root = transform,
				fsWatcher = fsWatcher
			};
			patchState.ApplyPatches(enumerator);
			Messenger.Default.Send<GraphJsonWillDeserializeEvent>(new GraphJsonWillDeserializeEvent(patcher));
			string finalGraphDumpPath = SingletonPluginBase<StrangeCustomsPlugin>.Shared.Settings.FinalGraphDumpPath;
			if (finalGraphDumpPath != null)
			{
				logger.Debug<string>("Saving final graph to {DumpPath}", finalGraphDumpPath);
				SaveObject(patcher.Value, finalGraphDumpPath);
			}
			dumpManifest?.WriteFinal(patcher.Value, patcher.Touchers);
			TrackState newState;
			try
			{
				newState = (patchState.NewState = ((JToken)patcher.Value).ToObject<TrackState>());
			}
			catch (Exception ex)
			{
				logger.Error<string>(ex, "While deserializing the patch data, an exception occurred: {Message}", ex.Message);
				throw new SCPatchingException("While deserializing the patched data, an exception occurred: " + ex.Message);
			}
			logger.Debug("Invoke GraphWillChange");
			List<PathSegment> list = patcher.Touchers.Keys.Select(PathSegment.Create).ToList();
			Messenger.Default.Send<GraphWillChangeEvent>(new GraphWillChangeEvent(newState, list));
			Graph val6 = (patchState.Graph = Graph.Shared);
			patchState.TrackObjectManager = TrackObjectManager.Instance;
			PatchingContext patchingContext = new PatchingContext(logger, patcher.Touchers);
			Dictionary<string, TrackNode> dictionary6 = (patchingContext.NodesById = new Dictionary<string, TrackNode>());
			Dictionary<string, TrackNode> dictionary8 = dictionary6;
			patchState.LogNoops = SingletonPluginBase<StrangeCustomsPlugin>.Shared.Settings.LogSkippedEntities;
			patchState.Changes = list;
			patchState.FinalNodes = dictionary8;
			patchState.DeserializeNodes();
			Dictionary<string, TrackSegment> dictionary9 = (patchingContext.SegmentsById = new Dictionary<string, TrackSegment>());
			Dictionary<string, TrackSegment> dictionary11 = dictionary9;
			try
			{
				List<PathSegment> list2 = list.Where((PathSegment s) => s.IsSubsetOf("tracks", "segments")).ToList();
				if (useChangeTracking && list2.Count == 0)
				{
					logger.Debug("No changes to tracks found; skipping track adjustment.");
				}
				else
				{
					logger.Debug("Process segments...");
					foreach (KeyValuePair<string, SerializedSegment> segment in newState.Tracks.Segments)
					{
						string key = segment.Key;
						SerializedSegment value = segment.Value;
						string text = key;
						TrackSegment value2;
						bool flag = dictionary2.TryGetValue(text, out value2);
						if (!patchState.HasAnyChangeMatching(list2, "tracks", "segments", text))
						{
							if (patchState.LogNoops)
							{
								logger.Verbose<string>("Skip segment {Id}; not changed", text);
							}
							dictionary2.Remove(text);
							dictionary11.Add(text, value2 ?? throw new SCPatchingException("Segment " + text + " does not exist, but is supposed to be unchanged?", text));
						}
						else if (value != null)
						{
							TrackNode value3 = null;
							TrackNode value4 = null;
							bool flag2 = value.StartId != null && dictionary8.TryGetValue(value.StartId, out value3);
							bool flag3 = value.EndId != null && dictionary8.TryGetValue(value.EndId, out value4);
							if (!flag2 || !flag3)
							{
								throw new SCPatchingException("Segment " + text + " has invalid ends: start " + (flag2 ? "exists" : "missing") + ", end " + (flag3 ? "exists" : "missing"), text);
							}
							if (!flag)
							{
								GameObject val7 = new GameObject(text);
								val7.SetActive(false);
								val7.transform.parent = transform;
								value2 = val7.AddComponent<TrackSegment>();
								value2.id = text;
								val7.SetActive(true);
							}
							value2.a = value3;
							value2.b = value4;
							value.ApplyTo(value2);
							if (!flag)
							{
								val6.AddSegment(value2, true);
							}
							dictionary11.Add(text, value2);
							dictionary2.Remove(text);
						}
					}
					logger.Debug<int>("Delete {Count} obsolete segments...", dictionary2.Count);
					foreach (TrackSegment value18 in dictionary2.Values)
					{
						Object.Destroy((Object)(object)value18);
					}
				}
				dictionary2 = null;
			}
			catch (SCPatchingException ex2) when (ex2.JsonPath != null)
			{
				throw new SCPatchingException(ex2, "tracks.segments");
			}
			catch (Exception ex3)
			{
				logger.Error<string>(ex3, "While rematerializing the segments, an exception occurred: {Message}", ex3.Message);
				throw new SCPatchingException("Exception while rematerializing the segments: " + ex3.Message, ex3);
			}
			Dictionary<string, TrackSpan> dictionary12 = (patchingContext.SpansById = new Dictionary<string, TrackSpan>());
			Dictionary<string, TrackSpan> dictionary14 = dictionary12;
			try
			{
				List<PathSegment> list3 = list.Where((PathSegment s) => s.IsSubsetOf("tracks", "spans")).ToList();
				if (useChangeTracking && list3.Count == 0)
				{
					logger.Debug("No changes to spans found; skipping span processing.");
				}
				else
				{
					logger.Debug("Process spans...");
					foreach (KeyValuePair<string, SerializedSpan> span in newState.Tracks.Spans)
					{
						string key2 = span.Key;
						SerializedSpan value5 = span.Value;
						string text2 = key2;
						TrackSpan value6;
						bool flag4 = dictionary3.TryGetValue(text2, out value6);
						if (!patchState.HasAnyChangeMatching(list3, "tracks", "spans", text2))
						{
							if (patchState.LogNoops)
							{
								logger.Verbose<string>("Skip span {Id}; not changed.", text2);
							}
							dictionary14.Add(text2, value6 ?? throw new SCPatchingException("Span " + text2 + " was not changed, but does also not exist? Uh?", text2));
							dictionary3.Remove(text2);
						}
						else if (value5 != null)
						{
							if (!flag4)
							{
								GameObject val8 = new GameObject(text2);
								val8.transform.parent = transform;
								val8.SetActive(false);
								value6 = val8.AddComponent<TrackSpan>();
								value6.id = text2;
								val8.SetActive(true);
							}
							value5.ApplyTo(text2, patchingContext, value6);
							dictionary14.Add(text2, value6);
							dictionary3.Remove(text2);
						}
					}
					logger.Debug<int>("Delete {Count} obsolete spans...", dictionary3.Count);
					foreach (TrackSpan value19 in dictionary3.Values)
					{
						Object.Destroy((Object)(object)value19);
					}
				}
				dictionary3 = null;
			}
			catch (SCPatchingException ex4) when (ex4.JsonPath != null)
			{
				throw new SCPatchingException(ex4, "tracks.spans");
			}
			catch (Exception ex5)
			{
				logger.Error<string>(ex5, "While rematerializing the spans, an exception occurred: {Message}", ex5.Message);
				throw new SCPatchingException("Exception while rematerializing the spans: " + ex5.Message, ex5);
			}
			try
			{
				List<PathSegment> list4 = list.Where((PathSegment s) => s.IsSubsetOf("loads")).ToList();
				if (useChangeTracking && list4.Count == 0)
				{
					logger.Debug("No loads have been modified; skipping load processing.");
				}
				else
				{
					logger.Debug("Process loads...");
					foreach (KeyValuePair<string, SerializedLoad> load in newState.Loads)
					{
						string key3 = load.Key;
						SerializedLoad value7 = load.Value;
						string text3 = key3;
						if (value7 == null)
						{
							logger.Error("Deleting loads is not supported.");
							continue;
						}
						if (!patchState.HasAnyChangeMatching(list4, "loads", text3))
						{
							if (patchState.LogNoops)
							{
								logger.Verbose<string>("Skip load {Id}; not changed", text3);
							}
							continue;
						}
						if (!dictionary5.TryGetValue(text3, out var value8))
						{
							value8 = ScriptableObject.CreateInstance<Load>();
							((Object)value8).name = text3;
							patchingContext.AddLoad(value8);
						}
						value7.ApplyTo(value8);
					}
				}
				patchingContext.SetLoads();
			}
			catch (SCPatchingException ex6) when (ex6.JsonPath != null)
			{
				throw new SCPatchingException(ex6, "loads");
			}
			catch (Exception ex7)
			{
				logger.Error<string>(ex7, "While rematerializing the loads, an exception occurred: {Message}", ex7.Message);
				throw new SCPatchingException("Exception while re-materializing the loads: " + ex7.Message, ex7);
			}
			Transform transform2 = GameObject.Find("Ops").transform;
			List<(Area, int)> list5 = new List<(Area, int)>();
			try
			{
				List<PathSegment> list6 = list.Where((PathSegment s) => s.IsSubsetOf("areas")).ToList();
				if (useChangeTracking && list6.Count == 0)
				{
					logger.Debug("No areas have been changed; skipping area processing...");
				}
				else
				{
					logger.Debug("Process areas...");
					List<PathSegment> list7 = new List<PathSegment>();
					foreach (KeyValuePair<string, SerializedArea> area in newState.Areas)
					{
						string key3 = area.Key;
						SerializedArea value9 = area.Value;
						string id = key3;
						Area value10;
						bool flag5 = dictionary4.TryGetValue(id, out value10);
						if (value9 == null)
						{
							logger.Error("Deleting areas is not supported.");
							continue;
						}
						list7.Clear();
						list7.AddRange(list6.Where((PathSegment s) => s.IsSubsetOf("areas", id)));
						if (useChangeTracking && list7.Count == 0)
						{
							if (patchState.LogNoops)
							{
								logger.Verbose<string>("Skip area {Id}; not modified", id);
							}
							if (flag5)
							{
								list5.Add((value10, value9.Order));
							}
							continue;
						}
						if (!flag5)
						{
							GameObject val9 = new GameObject(value9.Name);
							val9.SetActive(false);
							val9.transform.parent = transform2;
							value10 = val9.AddComponent<Area>();
							value10.identifier = id;
							val9.SetActive(true);
						}
						try
						{
							value9.ApplyTo(value10, patchingContext);
							list5.Add((value10, value9.Order));
						}
						catch (SCPatchingException ex8) when (ex8.JsonPath != null)
						{
							throw new SCPatchingException(ex8, id);
						}
						catch (Exception ex9)
						{
							logger.Error<string, string>(ex9, "While rematerializing area {AreaId}, an exception occurred: {Message}", id, ex9.Message);
							throw new SCPatchingException("Exception while rematerializing " + id + ": " + ex9.Message, ex9);
						}
					}
					list5.Sort(((Area Area, int Order) a, (Area Area, int Order) b) => a.Order - b.Order);
					for (int num2 = 0; num2 < list5.Count; num2++)
					{
						logger.Information<int, string, string>("Area #{Index}: {Area} ({Path})", num2, ((Object)list5[num2].Item1).name, ((Component)list5[num2].Item1).transform.GetAbsolutePath());
						((Component)list5[num2].Item1).transform.SetSiblingIndex(num2);
					}
				}
			}
			catch (SCPatchingException ex10) when (ex10.JsonPath != null)
			{
				throw new SCPatchingException(ex10, "areas");
			}
			catch (Exception ex11)
			{
				logger.Error<string>(ex11, "While rematerializing the areas, an exception occurred: {Message}", ex11.Message);
				throw new SCPatchingException("Exception while rematerializing the areas: " + ex11.Message, ex11);
			}
			try
			{
				logger.Debug("Process signs...");
				TextSynchronizer[] array5 = Object.FindObjectsByType<TextSynchronizer>((FindObjectsInactive)1, (FindObjectsSortMode)0);
				foreach (TextSynchronizer val10 in array5)
				{
					if (newState.Texts.TryGetValue(val10.text, out string value11))
					{
						val10.text = value11;
						val10.ApplyText();
					}
				}
				MapLabel[] array6 = Object.FindObjectsByType<MapLabel>((FindObjectsInactive)1, (FindObjectsSortMode)0);
				foreach (MapLabel val11 in array6)
				{
					TMP_Text componentInChildren = ((Component)val11).GetComponentInChildren<TMP_Text>();
					if (!((Object)(object)componentInChildren == (Object)null) && newState.Texts.TryGetValue(componentInChildren.text, out string value12))
					{
						componentInChildren.text = (val11.text = value12);
						componentInChildren.autoSizeTextContainer = true;
					}
				}
			}
			catch (Exception ex12)
			{
				logger.Error<string>(ex12, "While rematerializing the signs, an exception occurred: {Message}", ex12.Message);
				throw new SCPatchingException("Exception while rematerializing the signs: " + ex12.Message, ex12);
			}
			if (fsWatcher != null)
			{
				cts = new CancellationTokenSource();
			}
			try
			{
				logger.Debug("Process scenery...");
				WorldTransformer val12 = default(WorldTransformer);
				if (!WorldTransformer.TryGetShared(ref val12))
				{
					throw new InvalidOperationException("Cannot get world transformer");
				}
				foreach (KeyValuePair<string, SerializedScenery> item in newState.Scenery)
				{
					string key4 = item.Key;
					SerializedScenery value13 = item.Value;
					string text4 = key4;
					bool active = true;
					SceneryAssetInstance value14;
					bool flag6 = sceneryById.TryGetValue(text4, out value14);
					if (flag6)
					{
						active = ((Component)value14).gameObject.activeSelf;
						Object.Destroy((Object)(object)((Component)value14).gameObject);
						flag6 = false;
					}
					if (value13 != null)
					{
						if (!flag6 || value14.identifier != value13.ModelIdentifier)
						{
							GameObject val13 = new GameObject(text4);
							val13.SetActive(false);
							value14 = val13.AddComponent<SceneryAssetInstance>();
							value14.identifier = value13.ModelIdentifier;
						}
						if (value13 != null)
						{
							((Component)value14).transform.SetPositionAndRotation(value13.Position, Quaternion.Euler(value13.Rotation));
							((Component)value14).transform.localScale = value13.Scale;
							val12.AddObjectToMove(((Component)value14).transform);
							sceneryById[text4] = value14;
							((Component)value14).gameObject.SetActive(active);
						}
					}
				}
			}
			catch (Exception ex13)
			{
				logger.Error<string>(ex13, "While rematerializing the scenery, an exception occurred: {Message}", ex13.Message);
				throw new SCPatchingException("Exception while rematerializing the scenery: " + ex13.Message, ex13);
			}
			List<PathSegment> list8 = list.Where((PathSegment s) => s.IsSubsetOf("splineys")).ToList();
			if (useChangeTracking && list8.Count == 0)
			{
				logger.Debug("No spliney changes found; skipping");
			}
			else if (splineys != null)
			{
				foreach (KeyValuePair<string, GameObject> item2 in splineys.ToList())
				{
					if (!useChangeTracking || patchState.HasAnyChangeMatching(list8, "splineys", item2.Key))
					{
						Object.Destroy((Object)(object)item2.Value);
						splineys.Remove(item2.Key);
					}
				}
			}
			if ((Object)(object)val6 != (Object)null)
			{
				val6.RebuildCollections();
			}
			if (!useChangeTracking || list8.Count > 0)
			{
				logger.Debug("Process splineys...");
				if (splineys == null)
				{
					splineys = new Dictionary<string, GameObject>();
				}
				foreach (KeyValuePair<string, JObject> spliney in newState.Splineys)
				{
					string key5 = spliney.Key;
					JObject value15 = spliney.Value;
					string text5 = key5;
					if (useChangeTracking && !patchState.HasAnyChangeMatching(list8, "splineys", text5))
					{
						if (patchState.LogNoops)
						{
							logger.Debug<string>("Skip spliney {Id}; not modified", text5);
						}
						continue;
					}
					if (value15 == null)
					{
						if (splineys.TryGetValue(text5, out GameObject value16))
						{
							Object.DestroyImmediate((Object)(object)value16);
						}
						continue;
					}
					try
					{
						string text6 = ((JToken)value15).ToObject<SerializedSpliney>()?.Handler;
						if (text6 != null && TryGetInstance<StrangeCustoms.ISplineyBuilder>(text6, out StrangeCustoms.ISplineyBuilder instance))
						{
							GameObject value17 = instance.BuildSpliney(text5, transform, value15);
							splineys?.Add(text5, value17);
							continue;
						}
						throw new SCPatchingException("Could not find spliney handler " + text6, text5);
					}
					catch (SCPatchingException)
					{
						throw;
					}
					catch (Exception ex15)
					{
						logger.Error<string>(ex15, "While rematerializing the splineys, an exception occurred: {Message}", ex15.Message);
						throw new SCPatchingException("Exception while rematerializing the splineys: " + ex15.Message, ex15);
					}
				}
			}
			List<PathSegment> list9 = list.Where((PathSegment s) => s.IsSubsetOf("mandelas")).ToList();
			if (!useChangeTracking || list9.Count > 0)
			{
				logger.Debug("Process Mandelas");
				try
				{
					ProcessMandelas(in newState);
				}
				catch (SCPatchingException)
				{
					throw;
				}
				catch (Exception ex17)
				{
					logger.Error<string>(ex17, "While doing mandelas, an exception occurred: {Message}", ex17.Message);
					throw new SCPatchingException("Exception while rematerializing mandelas: " + ex17.Message, ex17);
				}
			}
			if ((Object)(object)TrainController.Shared?.graph != (Object)null)
			{
				MapBuilder shared = MapBuilder.Shared;
				if (shared != null)
				{
					shared.Rebuild();
				}
			}
			if (fsWatcher == null)
			{
				originalState = null;
				sceneryById = null;
			}
			logger.Debug("Invoke GraphDidChange");
			Messenger.Default.Send<GraphDidChangeEvent>(new GraphDidChangeEvent(newState));
			logger.Debug("Invoke IndustriesDidChange");
			Messenger.Default.Send<IndustriesDidChange>(default(IndustriesDidChange));
		}
		catch (Exception exception)
		{
			dumpManifest?.WriteException(exception);
			throw;
		}
	}

	private void ProcessMandelas(in TrackState newState)
	{
		//IL_04d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_04dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0501: Unknown result type (might be due to invalid IL or missing references)
		//IL_053f: Unknown result type (might be due to invalid IL or missing references)
		//IL_051c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0521: Unknown result type (might be due to invalid IL or missing references)
		//IL_0525: Unknown result type (might be due to invalid IL or missing references)
		//IL_0430: Unknown result type (might be due to invalid IL or missing references)
		//IL_0435: Unknown result type (might be due to invalid IL or missing references)
		//IL_03be: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03dc: Unknown result type (might be due to invalid IL or missing references)
		Dictionary<string, GameObject> dictionary = (from g in Enumerable.Range(0, SceneManager.sceneCount).Select((Func<int, Scene>)SceneManager.GetSceneAt).SelectMany((Scene s) => ((Scene)(ref s)).GetRootGameObjects())
			group g by ((Object)g).name into p
			where !p.Skip(1).Any()
			select p).ToDictionary((IGrouping<string, GameObject> p) => p.Key, (IGrouping<string, GameObject> p) => p.First());
		foreach (KeyValuePair<string, Mandela> mandela in newState.Mandelas)
		{
			logger.Verbose<string>("Process {Mandela}", mandela.Key);
			try
			{
				string[] array = mandela.Key.Split(new char[1] { '/' }, 2);
				if (array.Length != 2)
				{
					throw new SCPatchingException("Invalid mandela path '" + mandela.Key + "'; expect at least one sub-object", mandela.Key);
				}
				if (!dictionary.TryGetValue(array[0], out var value))
				{
					throw new SCPatchingException("Cannot find root object of '" + mandela.Key + "' (" + array[0] + "). Choices: " + string.Join("/", dictionary.Keys), mandela.Key);
				}
				Mandela value2 = mandela.Value;
				Transform val = value.transform.Find(array[1]);
				string instantiateFrom = value2.InstantiateFrom;
				if (instantiateFrom != null)
				{
					if ((Object)(object)val != (Object)null)
					{
						Object.Destroy((Object)(object)((Component)val).gameObject);
					}
					string[] array2 = instantiateFrom.Split(new char[1] { '/' }, 2);
					if (array2.Length != 2)
					{
						throw new SCPatchingException("Invalid prefab path '" + instantiateFrom + "' for mandela '" + mandela.Key + "'", mandela.Key + ".instantiateFrom");
					}
					if (!dictionary.TryGetValue(array2[0], out var value3))
					{
						throw new SCPatchingException("Cannot find prefab root '" + array2[0] + "' for '" + mandela.Key + "'", mandela.Key + ".instantiateFrom");
					}
					Transform val2 = value3.transform.Find(array2[1]);
					if ((Object)(object)val2 == (Object)null)
					{
						throw new SCPatchingException("Cannot find prefab child '" + array2[1] + "' of '" + array2[0] + "' for '" + mandela.Key + "'", mandela.Key + ".instantiateFrom");
					}
					if ((Object)(object)((Component)val2).GetComponentInChildren<KeyValueObject>() != (Object)null)
					{
						throw new SCPatchingException("Prefab '" + instantiateFrom + "' contains a KeyValueObject. You definitely should not clone that.", mandela.Key + ".instantiateFrom");
					}
					if ((Object)(object)((Component)val2).GetComponentInChildren<SceneryAssetInstance>() != (Object)null)
					{
						throw new SCPatchingException("Prefab '" + instantiateFrom + "' contains a SceneryAssetInstance. Use `scenery` instead.");
					}
					string[] array3 = array[1].Split('/');
					Transform val3 = value.transform;
					for (int num = 0; num < array3.Length - 1; num++)
					{
						val = val3.Find(array3[num]);
						if ((Object)(object)val == (Object)null)
						{
							GameObject val4 = new GameObject(array3[num]);
							val4.transform.SetParent(val3, false);
							val4.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
							val = val4.transform;
						}
						val3 = val;
					}
					logger.Debug<string, string>("Instantiate {PrefabPath} into {NewPath}", instantiateFrom, mandela.Key);
					val = ((Component)Object.Instantiate<Transform>(val2, val3)).transform;
					((Component)val).transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
					((Object)val).name = array3[array3.Length - 1];
				}
				else if ((Object)(object)val == (Object)null)
				{
					throw new SCPatchingException("Cannot find child '" + array[1] + "' of '" + array[1] + "'.", mandela.Key);
				}
				bool? enabled = value2.Enabled;
				if (enabled.HasValue)
				{
					bool valueOrDefault = enabled == true;
					((Component)val).gameObject.SetActive(valueOrDefault);
				}
				Vector3? localPosition = value2.LocalPosition;
				if (localPosition.HasValue)
				{
					Vector3 valueOrDefault2 = localPosition.GetValueOrDefault();
					val.localPosition = valueOrDefault2;
				}
				localPosition = value2.LocalRotation;
				if (localPosition.HasValue)
				{
					Vector3 valueOrDefault3 = localPosition.GetValueOrDefault();
					val.localEulerAngles = valueOrDefault3;
				}
				localPosition = value2.LocalScale;
				if (localPosition.HasValue)
				{
					Vector3 valueOrDefault4 = localPosition.GetValueOrDefault();
					val.localScale = valueOrDefault4;
				}
				logger.Information<Transform, Vector3>("Created {Child} at {Position}", val, ((Component)val).transform.position);
			}
			catch (SCPatchingException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				logger.Error<string, string>("An exception occurred while patching {Mandela}: {Message}", mandela.Key, ex2.Message);
				throw new SCPatchingException("While deserializing the mandela '" + mandela.Key + "', an exception ocurred: " + ex2.Message);
			}
		}
	}

	internal static bool TryGetType<T>(string typeName, out Type? type) where T : class
	{
		if (string.IsNullOrEmpty(typeName))
		{
			type = null;
			return false;
		}
		type = AccessTools.TypeByName(typeName);
		if (type == null)
		{
			Type type2 = (typeLookup[typeName] = null);
			type = type2;
			return false;
		}
		if (!typeof(T).IsAssignableFrom(type))
		{
			type = null;
			return false;
		}
		return true;
	}

	private static bool TryGetInstance<T>(string typeName, out T? instance) where T : class
	{
		if (string.IsNullOrEmpty(typeName))
		{
			instance = null;
			return false;
		}
		string key = typeof(T).FullName + ":" + typeName;
		if (!instanceLookup.TryGetValue(key, out object value))
		{
			if (!TryGetType<T>(typeName, out Type type))
			{
				instanceLookup[key] = (instance = null);
				return false;
			}
			try
			{
				object obj = (instanceLookup[key] = (T)Activator.CreateInstance(type));
				value = obj;
			}
			catch (Exception ex)
			{
				logger.Error<string, string>(ex, "Could not create instance of {TypeName}: {ExceptionMessage}", typeName, ex.Message);
				instanceLookup[key] = null;
				instance = null;
				return false;
			}
		}
		instance = (T)value;
		return true;
	}

	internal static void MigrateGraph(JObject graph)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected O, but got Unknown
		JObject tracks = (JObject)graph["tracks"];
		MoveTo("nodes");
		MoveTo("segments");
		MoveTo("spans");
		foreach (JObject item in ((JToken)graph).SelectTokens("$.areas.*.industries.*.components.*").OfType<JObject>())
		{
			JProperty obj = item.Property("type", StringComparison.OrdinalIgnoreCase);
			object obj2;
			if (obj == null)
			{
				obj2 = null;
			}
			else
			{
				JToken value = obj.Value;
				obj2 = ((value != null) ? Extensions.Value<string>((IEnumerable<JToken>)value) : null);
			}
			string text = (string)obj2;
			if (text != null && text.StartsWith("Model.Ops.") && !TryGetType<IndustryComponent>(text, out Type type))
			{
				string text2 = text.Substring(0, 9) + "New" + text.Substring(9);
				if (TryGetType<IndustryComponent>(text2, out type))
				{
					logger.Warning<string, string>("Industry component at path {Path} uses incompatible type {Type}. Auto-migrated to fitting version.", ((JToken)item).Path, text);
					item["type"] = JToken.op_Implicit(text2);
				}
			}
		}
		JProperty obj3 = graph.Property("splineys", StringComparison.OrdinalIgnoreCase);
		JToken obj4 = ((obj3 != null) ? obj3.Value : null);
		JObject val = (JObject)(object)((obj4 is JObject) ? obj4 : null);
		if (val == null)
		{
			return;
		}
		foreach (KeyValuePair<string, JToken> item2 in val)
		{
			JToken value2 = item2.Value;
			JObject val2 = (JObject)(object)((value2 is JObject) ? value2 : null);
			if (val2 == null)
			{
				continue;
			}
			JProperty val3 = val2.Property("MeshBuilder", StringComparison.OrdinalIgnoreCase);
			JProperty val4 = val2.Property("handler", StringComparison.OrdinalIgnoreCase);
			if (val4 == null && val3 != null)
			{
				val2["handler"] = val3.Value;
				val4 = val2.Property("handler", StringComparison.OrdinalIgnoreCase);
				((JToken)val3).Remove();
			}
			if (val4 != null)
			{
				string text3 = Extensions.Value<string>((IEnumerable<JToken>)val4.Value);
				if (text3 == "StrangeCustoms.AutoTrestle")
				{
					val4.Value = JToken.op_Implicit("StrangeCustoms.AutoTrestleBuilder");
				}
				else if (text3 == "StrangeCustoms.Tracks.FlowyThingBuilder")
				{
					val4.Value = JToken.op_Implicit("StrangeCustoms.FlowyThingBuilder");
				}
			}
		}
		JObject GetTracks()
		{
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Expected O, but got Unknown
			//IL_0022: Expected O, but got Unknown
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			object obj5 = tracks;
			if (obj5 == null)
			{
				JObject obj6 = graph;
				JObject val5 = new JObject();
				JObject val6 = val5;
				tracks = val5;
				JToken val7 = (obj6["tracks"] = (JToken)(object)val6);
				obj5 = (object)(JObject)val7;
			}
			return (JObject)obj5;
		}
		void MoveTo(string name)
		{
			JToken val5 = graph[name];
			if (val5 != null)
			{
				GetTracks()[name] = val5;
				graph.Remove(name);
			}
		}
	}

	private static JObject LoadPatch(string file)
	{
		JObject obj = JObject.Parse(File.ReadAllText(file));
		MigrateGraph(obj);
		return obj;
	}
}
