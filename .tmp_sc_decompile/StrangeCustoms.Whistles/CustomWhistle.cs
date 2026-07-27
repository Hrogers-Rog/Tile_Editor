using Model.Definition.Data;

namespace StrangeCustoms.Whistles;

internal class CustomWhistle
{
	public string Name { get; set; }

	public AssetReference? Model { get; set; }

	public string Clip { get; set; }
}
