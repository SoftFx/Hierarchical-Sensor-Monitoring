﻿using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;

namespace HSMServer.Model.DataAlerts
{
    public sealed class BarDataAlertViewModel<T, U> : DataAlertViewModelBase<T> where T : BarBaseValue<U>, new() where U : struct
    {
        public BarDataAlertViewModel(Guid entityId) : base(entityId)
        {
            Conditions.Add(new BarConditionViewModel<T, U>(true));
        }

        public BarDataAlertViewModel(Policy<T, U> policy, BaseSensorModel sensor) : base(policy, sensor) { }
    }
}
