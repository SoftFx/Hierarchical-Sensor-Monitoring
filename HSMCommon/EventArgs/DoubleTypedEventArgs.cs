namespace HSMCommon.EventArgs
{
    public class DoubleTypedEventArgs<T1, T2> : System.EventArgs
    {
        public DoubleTypedEventArgs(T1 value1, T2 value2)
        {
            Value1 = value1;
            Value2 = value2;
        }

        public T2 Value2 { get; }

        public T1 Value1 { get; }
    }
}
