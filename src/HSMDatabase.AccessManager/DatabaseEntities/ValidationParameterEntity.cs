using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.Entity
{
    public class ValidationParameterEntity : IValidationParameterEntity
    {
        public int ParameterType { get; set; }
        public string ValidationValue { get; set; }
    }
}
