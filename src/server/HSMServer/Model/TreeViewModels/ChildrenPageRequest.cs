namespace HSMServer.Model.TreeViewModels;

public record ChildrenPageRequest(string TypeId, int CurrentPage, int PageSize)
{
    public string Id => TypeId.Replace("grid", string.Empty).Replace("list", string.Empty);

    public bool IsNodes => Id == "Nodes";
}