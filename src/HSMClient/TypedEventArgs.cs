namespace HSMClient
{
    public class TypedEventArgs<T> : System.EventArgs
    {
        private readonly T _value;
        public TypedEventArgs(T value)
        {
            _value = value;
        }

        public T Value
        {
            get { return _value; }
        }

    }
}
