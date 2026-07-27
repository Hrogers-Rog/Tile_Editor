using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Serilog;
using UnityEngine;
using UnityEngine.Networking;

namespace StrangeCustoms;

public class FileCache : MonoBehaviour
{
	public class CacheEntry
	{
		public string FileName { get; }

		public DateTime LastUpdate { get; protected set; }

		public bool IsValid { get; protected set; }

		public bool IsExpired => File.GetLastWriteTime(FileName) >= LastUpdate;

		public bool IsLoading { get; set; }

		public CacheEntry(string fileName)
		{
			FileName = fileName;
		}

		public virtual void Invalidate()
		{
			IsValid = false;
		}
	}

	public class CacheEntry<T> : CacheEntry
	{
		public T? Value { get; private set; }

		public event Action<T>? Loaded;

		public CacheEntry(string fileName)
			: base(fileName)
		{
		}

		public void Set(T value)
		{
			CleanUp();
			Value = value;
			base.LastUpdate = DateTime.Now;
			base.IsValid = true;
			base.IsLoading = false;
			this.Loaded?.Invoke(value);
			this.Loaded = null;
		}

		public void Set(Func<T> deferredSet)
		{
			CleanUp();
			base.IsValid = false;
			base.IsLoading = true;
			Value = deferredSet();
			base.LastUpdate = DateTime.Now;
			base.IsValid = true;
			base.IsLoading = false;
			this.Loaded?.Invoke(Value);
			this.Loaded = null;
		}

		public override void Invalidate()
		{
			base.Invalidate();
			base.IsLoading = false;
			CleanUp();
			Value = default(T);
		}

		public void Register(Action<T> callback)
		{
			if (base.IsValid)
			{
				callback?.Invoke(Value);
			}
			else
			{
				Loaded += callback;
			}
		}

		private void CleanUp()
		{
			if (Value is IDisposable disposable)
			{
				disposable.Dispose();
			}
		}

		public override string ToString()
		{
			return $"Cache[{typeof(T).FullName}]; Valid: {base.IsValid}; Expired: {base.IsExpired}; Loading: {base.IsLoading} <{base.FileName}>";
		}
	}

	private readonly ILogger logger = Log.ForContext<FileCache>();

	private Dictionary<string, CacheEntry> caches = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

	public static FileCache? Instance { get; private set; }

	private void Awake()
	{
		if ((Object)(object)Instance != (Object)null)
		{
			throw new InvalidOperationException($"Cannot initialize instance: {Instance} is already there.");
		}
		Instance = this;
	}

	public void LoadAudioClip(string fileName, Action<AudioClip> callback)
	{
		CheckScheme(fileName);
		if (TryGetValue(fileName, out CacheEntry<AudioClip> value) && value.IsValid && !value.IsExpired)
		{
			logger.Debug($"Return {value} from cache");
			callback(value.Value);
			return;
		}
		value.Register(callback);
		if (value.IsLoading)
		{
			logger.Debug($"{value} is already loading; defer our command");
			return;
		}
		logger.Debug($"Load {value}...");
		((MonoBehaviour)this).StartCoroutine(LoadAudioClip(fileName, value));
	}

	public Texture2D? LoadTexture(string fileName, out bool wasCached)
	{
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Expected O, but got Unknown
		CheckScheme(fileName);
		if (TryGetValue(fileName, out CacheEntry<Texture2D> value) && value.IsValid && !value.IsExpired)
		{
			logger.Debug<string>("Found texture {FileName}", fileName);
			wasCached = true;
			return value.Value;
		}
		wasCached = false;
		string text = Path.GetExtension(fileName).ToLower();
		if (text != ".png" && text != ".jpg" && text != ".jpeg")
		{
			logger.Debug("Only .png and .jpg/.jpeg can be loaded at the moment. " + fileName + " not loaded.");
			return null;
		}
		if (!File.Exists(fileName))
		{
			logger.Error<string>("Could not find {FileName}.", fileName);
			return null;
		}
		Texture2D val = new Texture2D(2, 2);
		((Object)val).name = Path.GetFileNameWithoutExtension(fileName);
		ImageConversion.LoadImage(val, File.ReadAllBytes(fileName));
		value.Set(val);
		return value.Value;
	}

	public bool TryGetValue<T>(string fileName, out CacheEntry<T> value)
	{
		string text = typeof(T).FullName + "//" + fileName;
		logger.Debug("Check existence of " + text);
		if (caches.TryGetValue(text, out CacheEntry value2) && value2 is CacheEntry<T> cacheEntry)
		{
			logger.Debug($"Exists: {cacheEntry}");
			value = cacheEntry;
			return true;
		}
		logger.Debug("Miss; create it.");
		value = new CacheEntry<T>(fileName);
		caches[text] = value;
		return false;
	}

	private IEnumerator LoadAudioClip(string fileName, CacheEntry<AudioClip> cacheEntry)
	{
		cacheEntry.Invalidate();
		cacheEntry.IsLoading = true;
		logger.Debug("Get " + fileName + " from disk...");
		UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(new Uri(fileName), (AudioType)0);
		try
		{
			yield return www.SendWebRequest();
			logger.Debug(fileName + " fetched; error if any: " + www.error);
			if (!string.IsNullOrEmpty(www.error))
			{
				logger.Error("While fetching audio clip " + fileName + ", an error occurred: " + www.error);
				cacheEntry.Invalidate();
				yield break;
			}
			cacheEntry.Set(DownloadHandlerAudioClip.GetContent(www));
		}
		finally
		{
			((IDisposable)www)?.Dispose();
		}
	}

	private void CheckScheme(string fileName)
	{
		if (!Uri.TryCreate(fileName, UriKind.Absolute, out var result))
		{
			throw new ArgumentException("Invalid URI");
		}
		if (result.Scheme != "file")
		{
			throw new ArgumentException("Only files can be loaded");
		}
	}
}
