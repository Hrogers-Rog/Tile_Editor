using Model.Definition.Data;
using Model.Ops.Definition;

namespace StrangeCustoms.Tracks;

public class SerializedLoad
{
	public string Description { get; set; }

	public LoadUnits Units { get; set; }

	public float Density { get; set; }

	public float UnitWeightInPounds { get; set; }

	public bool Importable { get; set; }

	public float PayPerQuantity { get; set; }

	public float CostPerUnit { get; set; }

	public SerializedLoad()
	{
	}

	public SerializedLoad(Load load)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		Description = load.description;
		Units = load.units;
		Density = load.density;
		UnitWeightInPounds = load.unitWeightInPounds;
		Importable = load.importable;
		PayPerQuantity = load.payPerQuantity;
		CostPerUnit = load.costPerUnit;
	}

	internal void ApplyTo(Load gameLoad)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		gameLoad.description = Description;
		gameLoad.units = Units;
		gameLoad.density = Density;
		gameLoad.unitWeightInPounds = UnitWeightInPounds;
		gameLoad.importable = Importable;
		gameLoad.payPerQuantity = PayPerQuantity;
		gameLoad.costPerUnit = CostPerUnit;
	}
}
