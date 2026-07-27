using System;

namespace StrangeCustoms.Tracks;

[Serializable]
public class SCPatchingException : Exception
{
	public string? JsonPath { get; }

	public SCPatchingException()
	{
	}

	public SCPatchingException(string message)
		: base(message)
	{
	}

	public SCPatchingException(string message, string jsonPath)
		: base(message)
	{
		JsonPath = jsonPath;
	}

	public SCPatchingException(string message, Exception inner)
		: base(message, inner)
	{
	}

	public SCPatchingException(SCPatchingException inner, string previousPath)
		: base(inner.Message, inner)
	{
		JsonPath = previousPath + "." + inner.JsonPath;
	}

	public override string ToString()
	{
		if (JsonPath != null)
		{
			string[] obj = new string[5] { "[", JsonPath, "]: ", Message, null };
			Exception innerException = base.InnerException;
			obj[4] = ((innerException != null && !(innerException is SCPatchingException)) ? ("\n" + base.InnerException.ToString()) : null);
			return string.Concat(obj);
		}
		return base.ToString();
	}
}
