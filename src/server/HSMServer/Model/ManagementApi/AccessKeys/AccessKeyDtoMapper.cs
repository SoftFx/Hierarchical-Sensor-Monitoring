using System;
using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.Extensions;

namespace HSMServer.Model.ManagementApi.AccessKeys
{
    /// <summary>
    /// The shared AccessKeyModel → <see cref="AccessKeyDto"/> mapping of the
    /// access-key surface — one mapping for the read service (#1787) and the
    /// write service (#1789), so an echoed write and a read of the same key
    /// can never diverge.
    /// </summary>
    internal static class AccessKeyDtoMapper
    {
        internal static AccessKeyDto ToDto(AccessKeyModel key) => new()
        {
            Id = key.Id,
            DisplayName = key.DisplayName,
            ProductId = key.ProductId,
            State = key.State switch
            {
                KeyState.Active => "active",
                KeyState.Expired => "expired",
                KeyState.Blocked => "blocked",
                // Unreachable while the enum has exactly the three members
                // above — but a future member must not silently read as
                // 'active' (a label admission decisions key on).
                _ => "unknown",
            },
            Permissions = PermissionsOf(key.Permissions),
            CreationTime = key.CreationTime.ToUtcInstant(),
            ExpirationTime = key.ExpirationTime.ToUtcInstant(),
            LastUseTime = key.LastUseTime == DateTime.MinValue ? null : key.LastUseTime.ToUtcInstant(),
            LastIp = key.LastIP,
            AuthorId = key.AuthorId,
        };

        // The stored flags are a long; the wire carries their names — the
        // agent reads permissions, not bits (the enum-table style of the
        // area's DTO remarks).
        internal static string[] PermissionsOf(KeyPermissions permissions)
        {
            List<string> names = new(4);

            if ((permissions & KeyPermissions.CanSendSensorData) != 0)
                names.Add("canSendSensorData");
            if ((permissions & KeyPermissions.CanAddNodes) != 0)
                names.Add("canAddNodes");
            if ((permissions & KeyPermissions.CanAddSensors) != 0)
                names.Add("canAddSensors");
            if ((permissions & KeyPermissions.CanReadSensorData) != 0)
                names.Add("canReadSensorData");

            return [.. names];
        }

        // The wire's permission names back to flags — the total-or-fail
        // inverse of PermissionsOf: an unknown name fails the whole parse
        // and the caller reports it field-keyed (nothing is silently
        // dropped).
        internal static bool TryParsePermissions(IEnumerable<string> names, out KeyPermissions permissions, out string unknownName)
        {
            permissions = 0;
            unknownName = null;

            foreach (var name in names)
            {
                switch (name)
                {
                    case "canSendSensorData":
                        permissions |= KeyPermissions.CanSendSensorData;
                        break;
                    case "canAddNodes":
                        permissions |= KeyPermissions.CanAddNodes;
                        break;
                    case "canAddSensors":
                        permissions |= KeyPermissions.CanAddSensors;
                        break;
                    case "canReadSensorData":
                        permissions |= KeyPermissions.CanReadSensorData;
                        break;
                    default:
                        unknownName = name;
                        return false;
                }
            }

            return true;
        }
    }
}
