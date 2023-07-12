namespace HSMServer.Core.Model.Policies;

public interface IPolicyCondition
{
    public PolicyOperation Operation { get; set; }

    public TargetValue Target { get; set; }

    public string Property { get; set; }


    public PolicyCombination Combination { get; set; }
}