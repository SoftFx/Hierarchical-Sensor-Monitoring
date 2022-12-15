using System.Collections.Generic;

namespace HSMServer.Model.AccessKeysViewModels;

public sealed class AccessKeyTableViewModel
{
    public bool FullTable { get; init; }

    public List<AccessKeyViewModel> Keys { get; init; }
}