namespace HSMServer.DTOs.Sensors;

public sealed record TableSettingsUpdateDto
{
    public bool? IsHideEnabled { get; set; }

    public int? MaxCommentHideSize { get; set; }
}