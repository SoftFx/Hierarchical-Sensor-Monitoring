namespace HSMServer.Model.TreeViewModels;

public record ChildrenPageRequest(string TypeId, int CurrentPage, int PageSize)
{
    public bool IsNodes => TypeId.Replace("grid", string.Empty).Replace("list", string.Empty) == "Nodes";
}