using Polly;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace HSMDataCollector.Client.HttpsClient.Polly.Strategy
{
    public class ConnectionStrategyOptions<TResult> : ResilienceStrategyOptions
    {
        public Func<ConnectionPredicateArguments<TResult>, ValueTask<bool>> ShouldHandle { get; set; } = args => PredicateResult.True();
        
        [Required]
        public Func<OnConnectionResultArguments<TResult>, ValueTask> OnConnectionResult { get; set; }
        
        
        public ConnectionStrategyOptions()
        {
            Name = "Connection";
        }
    }
}
