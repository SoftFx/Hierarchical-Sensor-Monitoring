using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Journal;

/// <summary>
/// The entity-kind tags of unified-journal records (#1588): every
/// configuration change carries one, the global Journal page and the
/// journal API filter by them. The rollbackable subset names the kinds
/// whose writes carry undo groups (the internal change-set store); the
/// rest are journal-visible only.
/// </summary>
public static class JournalKinds
{
    public const string SensorDataPolicies = "sensorDataPolicies";
    public const string SensorTtlPolicies = "sensorTtlPolicies";
    public const string ProductTtlPolicies = "productTtlPolicies";
    public const string AlertTemplate = "alertTemplate";
    public const string AlertSchedule = "alertSchedule";

    public const string NodeSettings = "nodeSettings";
    public const string TreeCrud = "treeCrud";
    public const string Folder = "folder";

    // Journal-visible only, never rollbackable: a chat holds webhooks and the
    // bot settings hold the token, and a secret cannot sit in a rollback's
    // before/after images (#1828).
    public const string Chat = "chat";
    public const string ServerSettings = "serverSettings";

    public static readonly IReadOnlyList<string> Rollbackable =
    [
        SensorDataPolicies, SensorTtlPolicies, ProductTtlPolicies, AlertTemplate, AlertSchedule,
    ];

    public static readonly IReadOnlyList<string> All =
    [
        .. Rollbackable, NodeSettings, TreeCrud, Folder, Chat, ServerSettings,
    ];

    public static bool IsRollbackable(string kind) => Rollbackable.Contains(kind);
}
