using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace StrangeCustoms;

internal class Patcher
{
	private Dictionary<string, string> touchers = new Dictionary<string, string>();

	public IReadOnlyDictionary<string, string> Touchers => touchers;

	public JObject Value { get; private set; }

	public Patcher(JObject rawJson)
	{
		Value = rawJson;
	}

	public JObject ApplyPatch(string patchSource, JObject patch)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Expected O, but got Unknown
		Value = (JObject)MergeObject(patchSource, Value, Value, patch, out var _, out var _);
		return Value;
	}

	private void MarkTouched(JToken token, string source)
	{
		touchers[token.Path] = source;
	}

	private void MarkDescendantsTouched(JToken token, string source)
	{
		JObject val = (JObject)(object)((token is JObject) ? token : null);
		if (val != null)
		{
			foreach (JToken item in ((JContainer)val).DescendantsAndSelf())
			{
				MarkTouched(item, source);
			}
			return;
		}
		JArray val2 = (JArray)(object)((token is JArray) ? token : null);
		if (val2 != null)
		{
			foreach (JToken item2 in ((JContainer)val2).DescendantsAndSelf())
			{
				MarkTouched(item2, source);
			}
			return;
		}
		MarkTouched(token, source);
	}

	private JToken MergeObject(string patchSource, JObject root, JObject target, JObject patch, out bool gone410, out bool markTouched)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Invalid comparison between Unknown and I4
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_0255: Unknown result type (might be due to invalid IL or missing references)
		//IL_025b: Invalid comparison between Unknown and I4
		//IL_0207: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_03af: Invalid comparison between Unknown and I4
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_0272: Unknown result type (might be due to invalid IL or missing references)
		//IL_0279: Invalid comparison between Unknown and I4
		//IL_0172: Unknown result type (might be due to invalid IL or missing references)
		//IL_03cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a7: Invalid comparison between Unknown and I4
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d8: Expected O, but got Unknown
		//IL_01e1: Expected O, but got Unknown
		//IL_01e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e8: Expected O, but got Unknown
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c0: Expected O, but got Unknown
		//IL_02f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ff: Invalid comparison between Unknown and I4
		//IL_02b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c9: Expected O, but got Unknown
		//IL_02c9: Expected O, but got Unknown
		//IL_03e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e9: Invalid comparison between Unknown and I4
		//IL_0441: Unknown result type (might be due to invalid IL or missing references)
		//IL_044b: Expected O, but got Unknown
		//IL_0446: Unknown result type (might be due to invalid IL or missing references)
		//IL_040f: Unknown result type (might be due to invalid IL or missing references)
		//IL_05d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_05db: Invalid comparison between Unknown and I4
		//IL_0460: Unknown result type (might be due to invalid IL or missing references)
		//IL_0467: Expected O, but got Unknown
		//IL_062f: Unknown result type (might be due to invalid IL or missing references)
		//IL_05df: Unknown result type (might be due to invalid IL or missing references)
		//IL_05e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_047c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0482: Invalid comparison between Unknown and I4
		//IL_05ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_060b: Expected O, but got Unknown
		//IL_0392: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f5: Unknown result type (might be due to invalid IL or missing references)
		gone410 = false;
		markTouched = false;
		if ((int)((JToken)patch).Type != 1)
		{
			throw new JsonException($"Expected object at {((JToken)patch).Path}, but got {((JToken)patch).Type}");
		}
		PatchInstructions patchInstructions = ((JToken)patch).ToObject<PatchInstructions>();
		if (patchInstructions?.IsValid ?? false)
		{
			if (patchInstructions.Find != null)
			{
				throw new JsonException("Searching on objects shouldn't happen - but did at " + ((JToken)patch).Path);
			}
			if (patchInstructions.Replace != null)
			{
				MarkDescendantsTouched((JToken)(object)target, patchSource);
				markTouched = true;
				return patchInstructions.Replace;
			}
			if (patchInstructions.MoveTo == null)
			{
				throw new JsonException("Cannot handle instructions at " + ((JToken)patch).Path);
			}
			JToken obj = ((JToken)root).SelectToken(patchInstructions.MoveTo);
			JObject val = (JObject)(object)((obj is JObject) ? obj : null);
			((JToken)patch.Property("$moveTo", StringComparison.OrdinalIgnoreCase)).Remove();
			if (val == null)
			{
				throw new JsonException("Cannot move '" + ((JToken)target).Path + "': Failed to find object at '" + patchInstructions.MoveTo + "' (at root '" + ((JToken)target).Root.Path + "').");
			}
			JContainer parent = ((JToken)target).Parent;
			JProperty val2 = (JProperty)(object)((parent is JProperty) ? parent : null);
			if (val2 == null)
			{
				throw new JsonException("Cannot move '" + ((JToken)target).Path + "' to '" + patchInstructions.MoveTo + "': Currently only support property moving");
			}
			MarkTouched((JToken)(object)val2, patchSource);
			((JToken)val2).Remove();
			gone410 = true;
			if (val[val2.Name] == null)
			{
				((JContainer)val).Add((object)val2);
				MarkTouched((JToken)(object)val2, patchSource);
				target = (JObject)val[val2.Name];
			}
			else
			{
				JObject val3 = new JObject();
				((JContainer)val3).Add((object)new JProperty(val2));
				target = (JObject)MergeObject(patchSource, root, val, val3, out var _, out var markTouched2);
				if (markTouched2)
				{
					MarkDescendantsTouched((JToken)(object)target, patchSource);
				}
			}
		}
		foreach (JProperty item in patch.Properties())
		{
			string name = item.Name;
			JToken value = item.Value;
			string text = name;
			if (text.Length > 0 && text[0] == '$')
			{
				continue;
			}
			if ((int)value.Type == 1)
			{
				if (target[text] == null || (int)target[text].Type == 10)
				{
					target[text] = value;
					MarkDescendantsTouched(target[text], patchSource);
					continue;
				}
				if ((int)target[text].Type == 1)
				{
					bool gone412;
					bool markTouched3;
					JToken val4 = MergeObject(patchSource, root, (JObject)target[text], (JObject)value, out gone412, out markTouched3);
					if (!gone412)
					{
						target[text] = val4;
						if (markTouched3)
						{
							MarkDescendantsTouched(target[text], patchSource);
						}
					}
					continue;
				}
				patchInstructions = (((int)value.Type == 1) ? value.ToObject<PatchInstructions>() : null);
				if (patchInstructions?.Replace != null)
				{
					target[text] = patchInstructions.Replace;
					MarkTouched(target[text], patchSource);
					continue;
				}
				if ((!(patchInstructions?.Remove)) ?? true)
				{
					throw new NotImplementedException($"Cannot replace {target[text].Type} with an object");
				}
				MarkTouched(target[text], patchSource);
				target.Remove(text);
			}
			else if ((int)value.Type == 2)
			{
				JToken obj2 = target[text];
				JTokenType? val5 = ((obj2 != null) ? new JTokenType?(obj2.Type) : ((JTokenType?)null));
				if (val5.HasValue && (int)val5.GetValueOrDefault() != 2)
				{
					string path = value.Path;
					JToken obj3 = target[text];
					throw new ArgumentException($"Could not set {path}: Target is not an array, but {((obj3 != null) ? new JTokenType?(obj3.Type) : ((JTokenType?)null))}");
				}
				JToken val6 = (target[text] = (JToken)((target[text] == null) ? ((JArray)null) : new JArray((JArray)target[text])));
				JToken val8 = val6;
				if (val8 == null)
				{
					JArray val9 = (JArray)value;
					for (int i = 0; i < ((JContainer)val9).Count; i++)
					{
						JToken val10 = val9[i];
						if ((int)val10.Type != 1)
						{
							continue;
						}
						PatchInstructions patchInstructions2 = val10.ToObject<PatchInstructions>();
						if (patchInstructions2 == null || !patchInstructions2.IsValid)
						{
							continue;
						}
						int? num;
						if (patchInstructions2.Add != null)
						{
							num = patchInstructions2.Append?.Length;
							if (num.HasValue && num.GetValueOrDefault() > 0)
							{
								throw new JsonException("Error adding elements to " + val10.Path + ": Cannot set $add and $append simultaneously");
							}
							val9[i] = patchInstructions2.Add;
							continue;
						}
						num = patchInstructions2.Append?.Length;
						if (num.HasValue && num.GetValueOrDefault() > 0)
						{
							JToken[] append = patchInstructions2.Append;
							foreach (JToken val11 in append)
							{
								val9.Add(val11);
							}
						}
						else if (patchInstructions2.Find == null || !patchInstructions2.Optional)
						{
							throw new NotSupportedException($"Unsupported instructions for {((JToken)val9).Path}[{i}] for non-existing target array");
						}
					}
					target[text] = value;
					MarkTouched(target[text], patchSource);
					continue;
				}
				if ((int)val8.Type != 2)
				{
					throw new NotImplementedException($"Cannot replace {target[text].Type} with an array");
				}
				foreach (JToken item2 in value.Children())
				{
					MergeArrayItem(patchSource, root, (JArray)val8, item2);
				}
			}
			else
			{
				target[text] = value;
				MarkTouched(target[text], patchSource);
			}
		}
		return (JToken)(object)target;
	}

	private void MergeArrayItem(string patchSource, JObject root, JArray sourceArray, JToken patch)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Invalid comparison between Unknown and I4
		//IL_05be: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_051c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_0389: Unknown result type (might be due to invalid IL or missing references)
		//IL_0390: Unknown result type (might be due to invalid IL or missing references)
		//IL_039e: Expected O, but got Unknown
		//IL_039e: Expected O, but got Unknown
		//IL_0407: Unknown result type (might be due to invalid IL or missing references)
		//IL_040d: Invalid comparison between Unknown and I4
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Invalid comparison between Unknown and I4
		//IL_0214: Unknown result type (might be due to invalid IL or missing references)
		//IL_021a: Invalid comparison between Unknown and I4
		//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_02aa: Invalid comparison between Unknown and I4
		//IL_044b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0413: Unknown result type (might be due to invalid IL or missing references)
		//IL_041a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0428: Expected O, but got Unknown
		//IL_0428: Expected O, but got Unknown
		//IL_0340: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a0: Invalid comparison between Unknown and I4
		//IL_022a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0230: Invalid comparison between Unknown and I4
		//IL_02ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c0: Invalid comparison between Unknown and I4
		//IL_01ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_027a: Unknown result type (might be due to invalid IL or missing references)
		//IL_030a: Unknown result type (might be due to invalid IL or missing references)
		ILogger val = Log.ForContext<Patcher>();
		PatchInstructions patchInstructions = (((int)patch.Type == 1) ? patch.ToObject<PatchInstructions>() : null);
		if (patchInstructions != null && patchInstructions.IsValid)
		{
			if (patchInstructions.Find != null)
			{
				((JObject)patch).Remove("$find");
				for (int i = 0; i < ((JContainer)sourceArray).Count; i++)
				{
					JToken val2 = sourceArray[i];
					PatchFind[] find = patchInstructions.Find;
					int num = 0;
					while (true)
					{
						if (num < find.Length)
						{
							PatchFind patchFind = find[num];
							if (patchFind == null)
							{
								throw new JsonException("Condition is null in $find at " + patch.Path);
							}
							JToken val3 = val2.SelectToken(patchFind.Path ?? string.Empty, true);
							if (val3 == null)
							{
								val.Verbose<string, string>("Token at {JsonPath} does not feature {Path}", val2.Path, patchFind.Path);
								break;
							}
							switch (patchFind.Comparison)
							{
							case Comparison.Equals:
							case Comparison.NotEquals:
							{
								bool flag = JToken.DeepEquals(val3, patchFind.Value);
								bool flag2 = patchFind.Comparison == Comparison.Equals;
								val.Verbose("Compare {Path}: {Value} {Comparison:l} {ConditionValue}? => {Result}", new object[5]
								{
									patchFind.Path?.ToString(),
									((object)val3)?.ToString(),
									patchFind.Comparison,
									((object)patchFind.Value)?.ToString(),
									flag == flag2
								});
								if (flag2 == flag)
								{
									goto IL_0346;
								}
								break;
							}
							case Comparison.StartsWith:
							{
								if ((int)val3.Type == 8)
								{
									JToken? value3 = patchFind.Value;
									if (value3 != null && (int)value3.Type == 8)
									{
										if (Extensions.Value<string>((IEnumerable<JToken>)val3).StartsWith(Extensions.Value<string>((IEnumerable<JToken>)patchFind.Value)))
										{
											goto IL_0346;
										}
										break;
									}
								}
								string[] obj2 = new string[5] { "Value at ", val3.Path, " or condition at ", null, null };
								JToken? value4 = patchFind.Value;
								obj2[3] = ((value4 != null) ? value4.Path : null);
								obj2[4] = " is not a string";
								throw new JsonException(string.Concat(obj2));
							}
							case Comparison.EndsWith:
							{
								if ((int)val3.Type == 8)
								{
									JToken? value5 = patchFind.Value;
									if (value5 != null && (int)value5.Type == 8)
									{
										if (Extensions.Value<string>((IEnumerable<JToken>)val3).EndsWith(Extensions.Value<string>((IEnumerable<JToken>)patchFind.Value)))
										{
											goto IL_0346;
										}
										break;
									}
								}
								string[] obj3 = new string[5] { "Value at ", val3.Path, " or condition at ", null, null };
								JToken? value6 = patchFind.Value;
								obj3[3] = ((value6 != null) ? value6.Path : null);
								obj3[4] = " is not a string";
								throw new JsonException(string.Concat(obj3));
							}
							case Comparison.Contains:
							{
								if ((int)val3.Type == 8)
								{
									JToken? value = patchFind.Value;
									if (value != null && (int)value.Type == 8)
									{
										if (Extensions.Value<string>((IEnumerable<JToken>)val3).Contains(Extensions.Value<string>((IEnumerable<JToken>)patchFind.Value)))
										{
											goto IL_0346;
										}
										break;
									}
								}
								string[] obj = new string[5] { "Value at ", val3.Path, " or condition at ", null, null };
								JToken? value2 = patchFind.Value;
								obj[3] = ((value2 != null) ? value2.Path : null);
								obj[4] = " is not a string";
								throw new JsonException(string.Concat(obj));
							}
							default:
								throw new JsonException("Undefined comparison in $find at " + patch.Path);
							}
							break;
						}
						if (patchInstructions.Clone)
						{
							val.Verbose<string>("Cloning {Path}...", sourceArray[i].Path);
							JToken val4 = sourceArray[i].DeepClone();
							bool gone;
							bool markTouched;
							JToken val5 = MergeObject(patchSource, root, (JObject)val4, (JObject)patch, out gone, out markTouched);
							if (!gone)
							{
								sourceArray.Add(val5);
								if (markTouched)
								{
									MarkDescendantsTouched(val5, patchSource);
								}
								else
								{
									MarkTouched(val5, patchSource);
								}
							}
							return;
						}
						if (patchInstructions.Replace != null)
						{
							sourceArray[i] = patchInstructions.Replace;
							MarkTouched(sourceArray[i], patchSource);
							return;
						}
						if (patchInstructions.Remove)
						{
							MarkTouched(sourceArray[i], patchSource);
							sourceArray.RemoveAt(i);
							return;
						}
						if ((int)val2.Type == 1)
						{
							bool gone2;
							bool markTouched2;
							JToken val6 = MergeObject(patchSource, root, (JObject)val2, (JObject)patch, out gone2, out markTouched2);
							if (!gone2)
							{
								sourceArray[i] = val6;
								if (markTouched2)
								{
									MarkDescendantsTouched(val6, patchSource);
								}
							}
							return;
						}
						throw new NotImplementedException($"Found item for array, but not sure what to do with an {val2.Type} and patch {patch}");
						IL_0346:
						num++;
					}
				}
				if (!patchInstructions.Optional)
				{
					throw new JsonException("Patch " + patch.Path + " could not be applied to array: No matches found for " + string.Join(" && ", patchInstructions.Find.Select((PatchFind s) => s.ToString())));
				}
			}
			else if (patchInstructions.Add != null)
			{
				int? num2 = patchInstructions.Append?.Length;
				if (num2.HasValue && num2.GetValueOrDefault() > 0)
				{
					throw new JsonException("Error adding elements to " + ((JToken)sourceArray).Path + ": Cannot set $add and $append simultaneously");
				}
				JToken add = patchInstructions.Add;
				sourceArray.Add(add);
				MarkTouched(add, patchSource);
			}
			else
			{
				int? num2 = patchInstructions.Append?.Length;
				if (!num2.HasValue || num2.GetValueOrDefault() <= 0)
				{
					throw new NotImplementedException();
				}
				JToken[] append = patchInstructions.Append;
				foreach (JToken val7 in append)
				{
					sourceArray.Add(val7);
					MarkTouched(val7, patchSource);
				}
			}
			return;
		}
		throw new JsonException("Patch " + patch.Path + " does not specify what to do with the array. If you want to replace the entire array, use { \"$replace\": [ ... ] }. If you want to add items, use $add or $append. If you want to edit individual items, make sure each item has a $find.");
	}
}
