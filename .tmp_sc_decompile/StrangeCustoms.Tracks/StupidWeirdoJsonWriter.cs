using System.IO;
using Newtonsoft.Json;

namespace StrangeCustoms.Tracks;

internal class StupidWeirdoJsonWriter : JsonTextWriter
{
	private bool isObject;

	public StupidWeirdoJsonWriter(TextWriter textWriter)
		: base(textWriter)
	{
	}

	public override void WritePropertyName(string name)
	{
		((JsonTextWriter)this).WritePropertyName(name);
		bool flag;
		switch (name)
		{
		case "position":
		case "rotation":
		case "tagColor":
		case "inputSpans":
		case "trackSpans":
		case "localPosition":
		case "outputSpans":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			isObject = true;
		}
	}

	protected override void WriteIndent()
	{
		if (isObject)
		{
			((JsonWriter)this).WriteRaw(" ");
		}
		else
		{
			((JsonTextWriter)this).WriteIndent();
		}
	}

	protected override void WriteIndentSpace()
	{
		if (isObject)
		{
			((JsonWriter)this).WriteRaw(" ");
		}
		else
		{
			((JsonTextWriter)this).WriteIndentSpace();
		}
	}

	public override void WriteEndObject()
	{
		((JsonWriter)this).WriteEndObject();
		isObject = false;
	}

	public override void WriteEndArray()
	{
		((JsonWriter)this).WriteEndArray();
		isObject = false;
	}
}
