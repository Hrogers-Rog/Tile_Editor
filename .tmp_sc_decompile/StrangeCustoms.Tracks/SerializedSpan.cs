using HarmonyLib;
using Newtonsoft.Json;
using Track;

namespace StrangeCustoms.Tracks;

public class SerializedSpan
{
	private static readonly FieldRef<TrackSpan, SerializableLocation> _upper = AccessTools.FieldRefAccess<TrackSpan, SerializableLocation>("_upper");

	private static readonly FieldRef<TrackSpan, SerializableLocation> _lower = AccessTools.FieldRefAccess<TrackSpan, SerializableLocation>("_lower");

	public SerializedLocation Upper { get; set; }

	public SerializedLocation Lower { get; set; }

	[JsonProperty(/*Could not decode attribute arguments.*/)]
	public bool Normalize { get; set; }

	public SerializedSpan()
	{
	}

	public SerializedSpan(TrackSpan trackSpan)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		Upper = new SerializedLocation(_upper.Invoke(trackSpan));
		Lower = new SerializedLocation(_lower.Invoke(trackSpan));
	}

	internal void ApplyTo(string id, PatchingContext ctx, TrackSpan trackSpan)
	{
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		(Upper ?? throw new SCPatchingException(id + " is missing upper location", trackSpan.id + ".upper")).Validate("Upper", id, ctx);
		(Lower ?? throw new SCPatchingException(id + " is missing lower location", trackSpan.id + ".lower")).Validate("Lower", id, ctx);
		_upper.Invoke(trackSpan) = Upper;
		_lower.Invoke(trackSpan) = Lower;
		if (Normalize)
		{
			trackSpan.NormalizeUpperLower();
		}
	}
}
