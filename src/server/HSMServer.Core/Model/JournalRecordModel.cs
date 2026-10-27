using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.TableOfChanges;
using System;

namespace HSMServer.Core.Model;

public sealed class JournalRecordModel
{
    public JournalKey Key { get; }


    public string Enviroment { get; init; }

    public string Initiator { get; init; }

    public string PropertyName { get; set; }

    public string OldValue { get; set; }
    public string NewValue { get; set; }

    public string Path { get; set; }

    /// <summary>Entity kind of the unified journal (#1588); null on legacy rows and non-configuration records.</summary>
    public string Kind { get; init; }

    /// <summary>
    /// Root product id of the touched entity (display filter of the global
    /// journal); null when not applicable. Settable: the settings-collection
    /// records get it enriched at the subscription seam (#1588 review round
    /// 3) — the collection itself has no node reference.
    /// </summary>
    public string ProductId { get; set; }

    /// <summary>The undo group (internal change-set id) of the write; null when the record is not rollbackable.</summary>
    public string ChangeSetId { get; init; }


    public JournalRecordModel(byte[] key, JournalRecordEntity entity)
    {
        Key = JournalKey.FromBytes(key);

        Initiator = entity.Initiator;
        Enviroment = entity.Enviroment;

        PropertyName = entity.PropertyName;
        NewValue = entity.NewValue;
        OldValue = entity.OldValue;
        Path = entity.Path;

        Kind = entity.Kind;
        ProductId = entity.ProductId;
        ChangeSetId = entity.ChangeSetId;
    }

    public JournalRecordModel(Guid id, InitiatorInfo initiator)
    {
        Key = new JournalKey(id, DateTime.UtcNow.Ticks);
        Initiator = (initiator ?? InitiatorInfo.System).ToString();
    }


    public JournalRecordEntity ToJournalEntity() => new()
    {
        PropertyName = PropertyName,
        Enviroment = Enviroment,
        Initiator = Initiator,
        OldValue = OldValue,
        NewValue = NewValue,
        Path = Path,
        Kind = Kind,
        ProductId = ProductId,
        ChangeSetId = ChangeSetId,
    };
}
