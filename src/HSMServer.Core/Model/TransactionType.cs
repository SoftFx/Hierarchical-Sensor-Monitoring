namespace HSMServer.Core.Model
{
    public enum TransactionType
    {
        Unknown = 0,
        Add = 1,
        Update = 2,
        Delete = 3,
        /// <summary>
        /// Use this type when the tree update is needed
        /// </summary>
        UpdateTree = 10
    }
}
