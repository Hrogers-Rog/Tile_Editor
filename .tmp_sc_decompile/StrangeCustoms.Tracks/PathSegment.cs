using System.Collections.Generic;
using System.Text;

namespace StrangeCustoms.Tracks;

internal struct PathSegment
{
	public readonly string[] Parts;

	private static List<string> stringCache = new List<string>();

	internal PathSegment(string[] parts)
	{
		Parts = parts;
	}

	public bool IsSubsetOf(params string[] parts)
	{
		for (int i = 0; i < parts.Length && i < Parts.Length; i++)
		{
			if (parts[i] != Parts[i])
			{
				return false;
			}
		}
		return true;
	}

	public static PathSegment Create(string input)
	{
		stringCache.Clear();
		stringCache.AddRange(SplitJsonPathEnum(input));
		return new PathSegment(stringCache.ToArray());
	}

	private static IEnumerable<string> SplitJsonPathEnum(string input)
	{
		bool inside = false;
		StringBuilder sb = new StringBuilder();
		int num;
		for (int i = 0; i < input.Length; num = i + 1, i = num)
		{
			char c = input[i];
			if (!inside)
			{
				switch (c)
				{
				case '.':
					if (sb.Length > 0)
					{
						yield return sb.ToString();
					}
					sb.Clear();
					continue;
				case '[':
					if (i < input.Length + 1 && input[i + 1] == '\'')
					{
						if (sb.Length > 0)
						{
							yield return sb.ToString();
						}
						i++;
						sb.Clear();
						inside = true;
						continue;
					}
					break;
				}
				sb.Append(c);
				continue;
			}
			switch (c)
			{
			case '\\':
				num = i + 1;
				i = num;
				sb.Append(input[i]);
				continue;
			case '\'':
				if (i < input.Length + 1 && input[i + 1] == ']')
				{
					yield return sb.ToString();
					i++;
					sb.Clear();
					inside = false;
					continue;
				}
				break;
			}
			sb.Append(c);
		}
		if (sb.Length > 0)
		{
			yield return sb.ToString();
		}
	}
}
