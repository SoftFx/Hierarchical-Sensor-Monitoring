using System.Runtime.CompilerServices;

// The tests assembly precedent; HSMServer itself reaches internal
// write-shape flags it must set faithfully — PolicyUpdate.PreserveChangeOwnership
// on the alert-administration TTL re-asserts (#1501 round-2).
[assembly: InternalsVisibleTo("HSMServer.Core.Tests")]
[assembly: InternalsVisibleTo("HSMServer")]