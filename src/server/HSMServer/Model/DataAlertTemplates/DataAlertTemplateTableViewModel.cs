using System.Collections.Generic;


namespace HSMServer.Model.DataAlertTemplates;

public sealed class DataAlertTemplateTableViewModel
{
    public bool FullTable { get; init; }

    public List<DataAlertTemplateViewModel> Keys { get; init; }
}