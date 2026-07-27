namespace StrangeCustoms.Tracks.Industries;

public interface ICustomIndustryComponent
{
	void SerializeComponent(SerializedComponent serializedComponent);

	void DeserializeComponent(SerializedComponent serializedComponent, PatchingContext ctx);
}
