using HSMCommon.Model;


namespace HSMDatabase.AccessManager.Formatters
{
    internal interface IFormatter
    {
        byte[] Serialize(BaseValue value);

        BaseValue Deserialize(byte[] data);
    }
}
