namespace HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

public sealed record TableSettingEntity
{
    public bool IsHideEnabled { get; init; }
    
    public int MaxCommentHideSize { get; init; }
}