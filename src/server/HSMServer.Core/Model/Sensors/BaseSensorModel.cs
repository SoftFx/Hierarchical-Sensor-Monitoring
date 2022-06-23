using System;
using System.Collections.Generic;

namespace HSMServer.Core.Model
{
    public enum SensorState : byte
    {
        Available,
        Freezed,
        Blocked = byte.MaxValue,
    }


    public abstract class BaseSensorModel
    {
        public Guid Id { get; }

        public string AuthorId { get; }

        public string ProductId { get; }

        public DateTime CreationDate{ get; }

        public string DisplayName { get; private set; }

        public string Description { get; private set; }

        //public SensorType Type { get; private set; }

        public SensorState State { get; private set; }

        // TODO: Status & DataError -> ValidationResult
        //public SensorStatus Status { get; private set; }
        //public string DataError { get; private set; }

        // TODO: move to policies logic
        //public TimeSpan ExpectedUpdateInterval { get; private set; }

        public string Unit { get; private set; }

        // TODO: maybe store in Storage
        public DateTime LastUpdateTime { get; private set; }
    }


    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue
    {
        private readonly List<Policy<T>> _policies = new();

        public abstract ValuesStorage<T> Storage { get; }
    }
}
