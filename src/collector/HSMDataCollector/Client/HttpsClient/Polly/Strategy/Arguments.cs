using Polly;

namespace HSMDataCollector.Client.HttpsClient.Polly.Strategy
{
    public readonly struct ConnectionPredicateArguments<TResult>
    {
        public ResilienceContext Context { get; }

        public Outcome<TResult> Outcome { get; }
        
        
        public ConnectionPredicateArguments(ResilienceContext context, Outcome<TResult> outcome)
        {
            Context = context;
            Outcome = outcome;
        }

    }
    
    public readonly struct OnConnectionResultArguments<TResult>
    {
        public ResilienceContext Context { get; }

        public Outcome<TResult> Outcome { get; }


        public OnConnectionResultArguments(ResilienceContext context, Outcome<TResult> outcome)
        {
            Context = context;
            Outcome = outcome;
        }
    }
}
