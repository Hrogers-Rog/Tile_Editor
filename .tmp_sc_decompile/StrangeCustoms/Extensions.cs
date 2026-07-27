using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StrangeCustoms;

internal static class Extensions
{
	public static List<Transform> GetAbsoluteAncestry(this Transform transform)
	{
		List<Transform> list = new List<Transform>();
		Transform val = transform;
		while ((Object)(object)val != (Object)null)
		{
			list.Add(val);
			val = val.parent;
		}
		list.Reverse();
		return list;
	}

	public static string GetAbsolutePath(this Transform transform)
	{
		StringBuilder stringBuilder = new StringBuilder();
		List<Transform> absoluteAncestry = transform.GetAbsoluteAncestry();
		for (int i = 0; i < absoluteAncestry.Count; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append("/");
			}
			stringBuilder.Append(((Object)absoluteAncestry[i]).name);
		}
		return stringBuilder.ToString();
	}
}
