﻿using HSMCommon;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using System;
using System.Drawing;

namespace HSMServer.Core.Tests.Infrastructure
{
    public static class EntitiesFactory
    {
        public static ProductEntity BuildProductEntity(string name = null, string parent = "") =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                ParentProductId = parent?.Length == 0 ? Guid.NewGuid().ToString() : parent,
                State = (int)ProductState.FullAccess,
                DisplayName = name ?? RandomGenerator.GetRandomString(),
                Description = RandomGenerator.GetRandomString(),
                CreationDate = DateTime.UtcNow.Ticks,
            };


        internal static AccessKeyEntity BuildAccessKeyEntity(string id = null, string name = null, string productId = null,
            KeyPermissions permissions = KeyPermissions.CanSendSensorData | KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors | KeyPermissions.CanReadSensorData) =>
            new()
            {
                Id = id ?? Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                ProductId = productId ?? Guid.NewGuid().ToString(),
                State = (byte)KeyState.Active,
                Permissions = (long)permissions,
                DisplayName = name ?? RandomGenerator.GetRandomString(),
                CreationTime = DateTime.UtcNow.Ticks,
                ExpirationTime = DateTime.MaxValue.Ticks
            };


        public static SensorEntity BuildSensorEntity(string name = null, string parent = "", byte? type = null) =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                ProductId = parent?.Length == 0 ? Guid.NewGuid().ToString() : parent,
                DisplayName = name ?? RandomGenerator.GetRandomString(),
                Description = RandomGenerator.GetRandomString(),
                Type = type ?? RandomGenerator.GetRandomByte(),
                CreationDate = DateTime.UtcNow.Ticks,
            };


        internal static UserEntity BuildUser() =>
            new()
            {
                Id = Guid.NewGuid(),
                UserName = RandomGenerator.GetRandomString(),
                Password = HashComputer.ComputePasswordHash(RandomGenerator.GetRandomString()),
                IsAdmin = false,
                ProductsRoles = new(),
            };


        internal static FolderEntity BuildFolderEntity() =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
                DisplayName = RandomGenerator.GetRandomString(),
                Description = RandomGenerator.GetRandomString(),
                Color = Color.Red.ToArgb(),
            };
    }
}