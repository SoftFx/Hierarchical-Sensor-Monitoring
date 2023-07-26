﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public enum SensorState : byte
    {
        Available,
        Muted,
        Blocked = byte.MaxValue,
    }

    [Flags]
    public enum Integration : int
    {
        Grafana = 1,
    }


    public interface IBarSensor
    {
        BarBaseValue LocalLastValue { get; }
    }


    public abstract class BaseSensorModel : BaseNodeModel
    {
        private static readonly SensorResult _muteResult = new(SensorStatus.OffTime, "Muted");
        private readonly PolicyEntity _ttlEntity;


        public override SensorPolicyCollection Policies { get; }


        internal abstract ValuesStorage Storage { get; }

        public abstract SensorType Type { get; }


        public Integration Integration { get; private set; }

        public DateTime? EndOfMuting { get; private set; }

        public SensorState State { get; private set; }


        public SensorResult? Status
        {
            get
            {
                if (State == SensorState.Muted)
                    return _muteResult;

                if (!Policies.SensorResult.IsOk)
                    return Policies.SensorResult;

                return Storage.Result;
            }
        }

        public PolicyResult PolicyResult => Policies.PolicyResult;

        public bool ShouldDestroy => Settings.SelfDestroy.Value?.TimeIsUp(LastUpdate) ?? false;


        public DateTime LastUpdate => Storage.LastValue?.ReceivingTime ?? DateTime.MinValue;

        public BaseValue LastDbValue => Storage.LastDbValue;

        public BaseValue LastValue => Storage.LastValue;

        public bool HasData => Storage.HasData;


        public Action<BaseValue> ReceivedNewValue;


        public BaseSensorModel(SensorEntity entity) : base(entity)
        {
            _ttlEntity = entity.TTLPolicy;

            State = (SensorState)entity.State;
            Integration = (Integration)entity.Integration;
            EndOfMuting = entity.EndOfMuting > 0L ? new DateTime(entity.EndOfMuting) : null;

            Policies.Attach(this);
        }


        internal abstract bool TryAddValue(BaseValue value);

        internal abstract void AddDbValue(byte[] bytes);

        internal abstract void RecalculatePolicy();

        internal abstract List<BaseValue> ConvertValues(List<byte[]> valuesBytes);


        internal override BaseNodeModel AddParent(ProductModel parent)
        {
            base.AddParent(parent);

            Policies.BuildDefault(this, _ttlEntity); //need for correct calculating $product and $path properties

            return this;
        }

        internal void Update(SensorUpdate update)
        {
            base.Update(update);

            State = update?.State ?? State;
            Integration = update?.Integration ?? Integration;
            EndOfMuting = update?.EndOfMutingPeriod ?? EndOfMuting;

            if (State == SensorState.Available)
                EndOfMuting = null;

            if (update.Policies != null)
                Policies.Update(update.Policies);
        }

        internal void ResetSensor()
        {
            Policies.Reset();
            Storage.Clear();
        }

        internal SensorEntity ToEntity() => new()
        {
            Id = Id.ToString(),
            AuthorId = AuthorId.ToString(),
            ProductId = Parent.Id.ToString(),
            DisplayName = DisplayName,
            Description = Description,
            CreationDate = CreationDate.Ticks,
            Type = (byte)Type,
            State = (byte)State,
            Integration = (int)Integration,
            Policies = Policies.Ids.Select(u => u.ToString()).ToList(),
            EndOfMuting = EndOfMuting?.Ticks ?? 0L,
            Settings = Settings.ToEntity(),
            TTLPolicy = Policies.TimeToLive?.ToEntity(),
        };
    }
}