using System.Linq;
using Audio;
using UnityEngine;

namespace StrangeCustoms.Horns;

internal class CustomHornProfile
{
	public string Name { get; set; }

	public CustomHornLayer[] Layers { get; set; }

	public HornProfile? Profile { get; private set; }

	internal void LoadFor(HornController controller)
	{
		if ((Object)(object)Profile != (Object)null)
		{
			controller.ApplyProfile(this);
			return;
		}
		FileCache instance = FileCache.Instance;
		for (int i = 0; i < Layers.Length; i++)
		{
			int j = i;
			instance.LoadAudioClip(Layers[i].File, delegate(AudioClip clip)
			{
				OnAudioClipLoaded(controller, clip, j);
			});
		}
	}

	private void OnAudioClipLoaded(HornController controller, AudioClip clip, int layerIndex)
	{
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected O, but got Unknown
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Expected O, but got Unknown
		//IL_00e1: Expected O, but got Unknown
		if ((Object)(object)Profile != (Object)null)
		{
			controller.ApplyProfile(this);
			return;
		}
		Layers[layerIndex].Clip = clip;
		if (!Layers.All((CustomHornLayer p) => (Object)(object)p.Clip != (Object)null))
		{
			return;
		}
		HornPlayer componentInChildren = ((Component)controller).GetComponentInChildren<HornPlayer>();
		Profile = Object.Instantiate<HornProfile>(componentInChildren.profile);
		Profile.layers = (Layer[])(object)new Layer[Layers.Length];
		for (int num = 0; num < Layers.Length; num++)
		{
			AnimationCurve val = new AnimationCurve();
			CustomKeyFrame[] keyframes = Layers[num].Keyframes;
			for (int num2 = 0; num2 < keyframes.Length; num2++)
			{
				CustomKeyFrame customKeyFrame = keyframes[num2];
				val.AddKey(customKeyFrame.T, customKeyFrame.Value);
			}
			Layer[] layers = Profile.layers;
			int num3 = num;
			Layer val2 = new Layer();
			Layer val3 = val2;
			layers[num3] = val2;
			val3.clip = Layers[num].Clip;
			val3.volumeCurve = val;
		}
		controller.ApplyProfile(this);
	}
}
