using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;

namespace HSMServer.Core.Model.Sensors;

public class TableSettingsModel
{
    public bool IsHideEnabled { get; set; } = true;

    public int MaxCommentHideSize { get; set; } = 1024;


    public TableSettingsModel()
    {
        
    }
    
    public TableSettingsModel(TableSettingEntity entity)
    {
        IsHideEnabled = entity.IsHideEnabled;
        MaxCommentHideSize = entity.MaxCommentHideSize;
    }

    public TableSettingEntity ToEntity() => new()
    {
        IsHideEnabled = IsHideEnabled,
        MaxCommentHideSize = MaxCommentHideSize
    };
}