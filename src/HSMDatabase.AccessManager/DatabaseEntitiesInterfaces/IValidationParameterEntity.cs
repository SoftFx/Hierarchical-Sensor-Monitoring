namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public interface IValidationParameterEntity
    {
        public int ParameterType { get; set; }
        public string ValidationValue { get; set; }
    }
}
