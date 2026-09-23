using System;
using HSMCommon.Model;

namespace HSMServer.Core.Model.Policies
{
    // The evaluation context of a sensor-expiry transition: the decision
    // (did any TTL policy time out), the instant it was taken at, and the
    // value it judged. A record struct so the names travel with the
    // positional arguments and the fan-out allocates once.
    internal readonly record struct ExpiryEvaluation(bool Timeout, DateTime At, BaseValue Value);
}
