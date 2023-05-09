namespace HSMDataCollector.Options
{
    internal abstract class Prototype<T> where T : SensorOptions, new()
    {
        protected abstract string NodePath { get; }

        protected T DefaultOptions { get; }


        protected Prototype()
        {
            DefaultOptions = new T()
            {
                NodePath = NodePath,
            };
        }


        internal T Get(T options) => options ?? DefaultOptions;

        internal virtual T GetAndFill(T options)
        {
            options = Get(options);

            if (options.NodePath == null)
                options.NodePath = NodePath;

            return options;
        }
    }
}
