namespace HSMServer.Core.TreeStateSnapshot.States
{
    public interface ILastState<T>
    {
        bool IsDefault { get; }


        void FromEntity(T entity);

        T ToEntity();
    }
}