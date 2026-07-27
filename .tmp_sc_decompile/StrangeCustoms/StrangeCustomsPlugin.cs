using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GalaSoft.MvvmLight.Messaging;
using Game.Events;
using HarmonyLib;
using Model;
using Railloader;
using Railloader.Compatibility;
using Serilog;
using StrangeCustoms.Horns;
using StrangeCustoms.Patching;
using StrangeCustoms.Tracks;
using StrangeCustoms.Whistles;
using TMPro;
using UI.Builder;
using UI.Common;
using UI.Console;
using UnityEngine;
using UnityEngine.UI;

namespace StrangeCustoms;

public class StrangeCustomsPlugin : SingletonPluginBase<StrangeCustomsPlugin>, IModTabHandler
{
	private bool devMode;

	private readonly IModDefinition self;

	private readonly IUIHelper uiHelper;

	private Exception? lastPatchingException;

	internal IModdingContext ModdingContext { get; }

	internal GraphPatcher? TrackPatcher { get; private set; }

	internal Settings Settings { get; private set; }

	internal Dictionary<(string ModId, string Mixinto), string> Failures { get; set; } = new Dictionary<(string, string), string>();

	public StrangeCustomsPlugin(IModDefinition self, IModdingContext moddingContext, IUIHelper uiHelper)
	{
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Expected O, but got Unknown
		if (typeof(Harmony).Assembly.GetName().Version < new Version(2, 3, 1, 1))
		{
			Log.ForContext<StrangeCustomsPlugin>().Error<Version>("Cannot load Strange Customs: Available Harmony is {Version}, which is older than the required 2.3.0.", typeof(Harmony).Assembly.GetName().Version);
			throw new InvalidOperationException("Incompatible Harmony detected; Strange Customs does not support this version of Harmony.");
		}
		this.self = self;
		ModdingContext = moddingContext;
		this.uiHelper = uiHelper;
		Settings = moddingContext.LoadSettingsData<Settings>(self.Id) ?? new Settings();
		new Harmony("Zamu.StrangeCustoms").PatchAll(((object)this).GetType().Assembly);
		GameObject val = new GameObject("[ZSC FileCache]");
		Object.DontDestroyOnLoad((Object)val);
		val.AddComponent<FileCache>();
		SetAccessLevels();
		moddingContext.RegisterConsoleCommand((IConsoleCommand)(object)new ReloadHornsCommand());
		moddingContext.RegisterConsoleCommand((IConsoleCommand)(object)new VerifyPatchesCommand());
		moddingContext.RegisterConsoleCommand((IConsoleCommand)(object)new ReloadWhistlesCommand());
		moddingContext.RegisterConsoleCommand((IConsoleCommand)(object)new ReloadTracksCommand());
		moddingContext.RegisterConsoleCommand((IConsoleCommand)(object)new DumpMandelasCommand());
		Messenger.Default.Register<MapDidLoadEvent>((object)this, (Action<MapDidLoadEvent>)OnMapDidLoad);
		Messenger.Default.Register<MapWillUnloadEvent>((object)this, (Action<MapWillUnloadEvent>)OnMapWillUnload);
		devMode = Settings.InitialGraphDumpPath != null || Settings.AllowTrackAutoReload || Settings.DisplayTracker;
	}

	public override void OnDisable()
	{
		Messenger.Default.Unregister((object)this);
	}

	private void MagicButton(UIPanelBuilder builder)
	{
		((UIPanelBuilder)(ref builder)).Spacer(3f);
		((UIPanelBuilder)(ref builder)).AddLabel("If you want to, you can press the button below to create a zip file that contains all steps taken by Strange Customs, the state at each step, and what was changed. This can help you to diagnose when, or who, is responsible for a change. Clicking the button will create this zip and show it in the explorer.", (Action<TMP_Text>)delegate(TMP_Text t)
		{
			t.fontSize *= 0.7f;
		});
		((UIPanelBuilder)(ref builder)).AddButtonCompact("Create graph-dump.zip and show it", (Action)delegate
		{
			try
			{
				TrackPatcher?.Patch(emitZip: true);
			}
			catch (Exception ex)
			{
				Log.ForContext<StrangeCustomsPlugin>().Error<string>(ex, "Cannot dump the graph: {Message}", ex.Message);
			}
			try
			{
				string fullName = new FileInfo(Path.Combine(Application.dataPath, "..", "graph-dump.zip")).FullName;
				if (File.Exists(fullName))
				{
					Process.Start("explorer.exe", "/select,\"" + fullName + "\"").Dispose();
				}
				else
				{
					Toast.Present("Could not create zip!", (ToastPosition)0);
				}
			}
			catch
			{
			}
		});
		((UIPanelBuilder)(ref builder)).Spacer(3f);
	}

	private void OnMapDidLoad(MapDidLoadEvent @event)
	{
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		if (lastPatchingException == null && Failures.Count <= 0)
		{
			return;
		}
		Window val = uiHelper.CreateWindow(800, 400, (Position)4);
		val.Title = "Strange Customs reports an oopsie woopsie";
		Vector2 val2 = new Vector2(600f, 400f);
		Resolution currentResolution = Screen.currentResolution;
		float num = ((Resolution)(ref currentResolution)).width;
		currentResolution = Screen.currentResolution;
		val.SetResizable(val2, new Vector2(num, (float)((Resolution)(ref currentResolution)).height));
		bool fullMessage = Settings.AllowTrackAutoReload || lastPatchingException is SCPatchingException;
		uiHelper.PopulateWindow(val, (Action<UIPanelBuilder>)delegate(UIPanelBuilder builder)
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			UIPanelBuilderCompatibility.VScrollViewCompat(builder, (Action<UIPanelBuilder>)delegate(UIPanelBuilder val3)
			{
				//IL_000e: Unknown result type (might be due to invalid IL or missing references)
				//IL_000f: Unknown result type (might be due to invalid IL or missing references)
				//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
				//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
				((UIPanelBuilder)(ref val3)).AddTitle("Something went wrong while adjusting the map", "You probably want to report this to someone.");
				((UIPanelBuilder)(ref val3)).AddLabel("The game is currently in a wonky state, since not all patches have been applied successfully.\nIt's possible that various things are broken or in different states of bork. Remove offending mods, then re-load the save.\n<b>This is a potential serious issue. Do not ignore this message. Do not just click it away.</b>");
				foreach (KeyValuePair<(string, string), string> failure in Failures)
				{
					((UIPanelBuilder)(ref val3)).AddField(failure.Key.Item1, failure.Key.Item2 + "\n" + failure.Value);
				}
				if (lastPatchingException != null)
				{
					MagicButton(val3);
					((UIPanelBuilder)(ref val3)).AddField("Show Details", UIPanelBuilderCompatibility.AddToggleCompat(val3, (Func<bool>)(() => fullMessage), (Action<bool>)delegate(bool s)
					{
						fullMessage = s;
						((UIPanelBuilder)(ref val3)).Rebuild();
					}, true));
					((UIPanelBuilder)(ref val3)).AddLabel(fullMessage ? lastPatchingException.ToString() : lastPatchingException.Message, (Action<TMP_Text>)delegate
					{
					});
				}
			}, (RectOffset)null);
		});
		val.ShowWindow();
	}

	private void OnMapWillUnload(MapWillUnloadEvent @event)
	{
		TrackPatcher?.Dispose();
		TrackPatcher = null;
	}

	internal bool RunGraphPatcher()
	{
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		if (TrackPatcher != null || !((PluginBase)this).IsEnabled)
		{
			return false;
		}
		lastPatchingException = null;
		try
		{
			TrackPatcher = new GraphPatcher(Settings.AllowTrackAutoReload);
			TrackPatcher.Patch();
			Tracker tracker = Object.FindObjectOfType<Tracker>();
			if ((Object)(object)tracker != (Object)null)
			{
				Object.Destroy((Object)(object)((Component)tracker).gameObject);
			}
			if (Settings.DisplayTracker)
			{
				new GameObject().AddComponent<Tracker>();
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.ForContext<StrangeCustomsPlugin>().Error<string>(ex, "An error occurred while running the patcher: {Message}", ex.Message);
			lastPatchingException = ex;
			return false;
		}
	}

	private static void SetAccessLevels()
	{
		ref string[] reference = ref AccessTools.StaticFieldRefAccess<Car, string[]>("TrainmasterPrefixes");
		string[] array = new string[reference.Length + 1];
		Array.Copy(reference, array, reference.Length);
		array[array.Length - 1] = "horn.custom";
		reference = array;
	}

	internal IEnumerable<ModMixinto> GetMixintos(string identifier, MixintoType limitToType)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		string baseDir = ModdingContext.ModsBaseDirectory;
		foreach (ModMixinto mixinto in ModdingContext.GetMixintos(identifier))
		{
			ModMixinto current = mixinto;
			if (((ModMixinto)(ref current)).Type == limitToType)
			{
				if (!((ModMixinto)(ref current)).Mixinto.StartsWith(baseDir))
				{
					Log.ForContext(typeof(StrangeCustomsPlugin)).Warning<ModMixinto>("Filtered mixinto {Mixinto}: Path leads outside the mods directory", current);
				}
				else
				{
					yield return current;
				}
			}
		}
	}

	public void ModTabDidOpen(UIPanelBuilder builder)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0209: Unknown result type (might be due to invalid IL or missing references)
		//IL_0254: Unknown result type (might be due to invalid IL or missing references)
		//IL_0294: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0314: Unknown result type (might be due to invalid IL or missing references)
		if (Failures.Count > 0)
		{
			((UIPanelBuilder)(ref builder)).AddLabel("One or more mods failed to load properly.", (Action<TMP_Text>)delegate(TMP_Text tm)
			{
				//IL_0010: Unknown result type (might be due to invalid IL or missing references)
				((Graphic)tm).color = new Color(0.8f, 0f, 0f);
			});
			foreach (KeyValuePair<(string, string), string> failure in Failures)
			{
				((UIPanelBuilder)(ref builder)).AddField(failure.Key.Item1, failure.Key.Item2 + ": " + failure.Value);
			}
			((UIPanelBuilder)(ref builder)).Spacer(3f);
		}
		((UIPanelBuilder)(ref builder)).AddField("Independent Sliders", UIPanelBuilderCompatibility.AddToggleCompat(builder, (Func<bool>)(() => Settings.DecoupleConditionLimits), (Action<bool>)delegate(bool v)
		{
			Settings.DecoupleConditionLimits = v;
		}, true));
		AddHelp("If checked, allows to make cars appear healthier than they are.");
		((UIPanelBuilder)(ref builder)).AddField("Randomize Visual", UIPanelBuilderCompatibility.AddToggleCompat(builder, (Func<bool>)(() => Settings.RandomizeVisualCondition), (Action<bool>)delegate(bool v)
		{
			Settings.RandomizeVisualCondition = v;
			((UIPanelBuilder)(ref builder)).Rebuild();
		}, true));
		if (Settings.RandomizeVisualCondition)
		{
			((UIPanelBuilder)(ref builder)).AddField("Min Condition", ((UIPanelBuilder)(ref builder)).AddSlider((Func<float>)(() => Settings.RandomMinimumValue), (Func<string>)(() => $"{Settings.RandomMinimumValue * 100f:0}%"), (Action<float>)delegate(float s)
			{
				Settings.RandomMinimumValue = Math.Min(s, Settings.RandomMaximumValue);
			}, 0f, 1f, false, (Action<float>)null));
			((UIPanelBuilder)(ref builder)).AddField("Max Condition", ((UIPanelBuilder)(ref builder)).AddSlider((Func<float>)(() => Settings.RandomMaximumValue), (Func<string>)(() => $"{Settings.RandomMaximumValue * 100f:0}%"), (Action<float>)delegate(float s)
			{
				Settings.RandomMaximumValue = Math.Max(s, Settings.RandomMinimumValue);
			}, 0f, 1f, false, (Action<float>)null));
		}
		AddHelp("If checked, freshly spawned cars will have a randomized visual condition.");
		MagicButton(builder);
		((UIPanelBuilder)(ref builder)).AddField("Show Dev Settings", UIPanelBuilderCompatibility.AddToggleCompat(builder, (Func<bool>)(() => devMode), (Action<bool>)delegate(bool s)
		{
			devMode = s;
			((UIPanelBuilder)(ref builder)).Rebuild();
		}, true));
		AddHelp("If checked, shows some dev-only-settings for graph editing that will require a game restart after setting them.");
		if (devMode)
		{
			((UIPanelBuilder)(ref builder)).AddField("Dump Initial Graph", UIPanelBuilderCompatibility.AddToggleCompat(builder, (Func<bool>)(() => Settings.InitialGraphDumpPath != null), (Action<bool>)delegate(bool s)
			{
				Settings.InitialGraphDumpPath = (s ? "graph-data.json" : null);
			}, true));
			AddHelp("Dump the game's data graph to the graph-data.json");
			((UIPanelBuilder)(ref builder)).AddField("Dump Final Graph", UIPanelBuilderCompatibility.AddToggleCompat(builder, (Func<bool>)(() => Settings.FinalGraphDumpPath != null), (Action<bool>)delegate(bool s)
			{
				Settings.FinalGraphDumpPath = (s ? "graph-modded.json" : null);
			}, true));
			AddHelp("Dump the Strange Custom's final graph, if successful, graph to the graph-modded.json");
			((UIPanelBuilder)(ref builder)).AddField("Auto-Reload", UIPanelBuilderCompatibility.AddToggleCompat(builder, (Func<bool>)(() => Settings.AllowTrackAutoReload), (Action<bool>)delegate(bool s)
			{
				Settings.AllowTrackAutoReload = s;
			}, true));
			AddHelp("Automatically re-load track patches when a change is detected on-disk. Also enables smarter query tool.");
			((UIPanelBuilder)(ref builder)).AddField("Tracker", UIPanelBuilderCompatibility.AddToggleCompat(builder, (Func<bool>)(() => Settings.DisplayTracker), (Action<bool>)delegate(bool s)
			{
				Settings.DisplayTracker = s;
			}, true));
			AddHelp("Display graph information in the world view. Requires a graph or game reload to take effect.");
		}
		void AddHelp(string help)
		{
			((UIPanelBuilder)(ref builder)).AddField(string.Empty, ((UIPanelBuilder)(ref builder)).AddLabel(help, (Action<TMP_Text>)delegate(TMP_Text t)
			{
				t.fontSize *= 0.7f;
			}));
			((UIPanelBuilder)(ref builder)).Spacer(3f);
		}
	}

	public void ModTabDidClose()
	{
		ModdingContext.SaveSettingsData<Settings>(self.Id, Settings);
	}
}
