﻿using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Cache.UpdateEntities
{
    /// <summary>
    /// If properties are null - there's no updates for that properties
    /// </summary>
    public record SensorUpdate : BaseNodeUpdate
    {
        public List<PolicyUpdate> Policies { get; init; }


        public DateTime? EndOfMutingPeriod { get; init; }

        public bool? SaveOnlyUniqueValues { get; init; }

        public Integration? Integration { get; init; }

        public SensorState? State { get; init; }

        public Unit? SelectedUnit { get; init; }
    }


    public sealed record PolicyUpdate
    {
        public List<PolicyConditionUpdate> Conditions { get; init; }

        public PolicyDestinationUpdate Destination { get; init; }

        public TimeIntervalModel Sensitivity { get; init; }


        public Guid Id { get; init; }

        public SensorStatus Status { get; init; }

        public string Template { get; init; }

        public bool IsDisabled { get; init; }

        public string Icon { get; init; }


        public string Initiator { get; init; } = TreeValuesCache.System;
    }


    public sealed record PolicyConditionUpdate(
        PolicyOperation Operation,
        PolicyProperty Property,
        TargetValue Target,
        PolicyCombination Combination = PolicyCombination.And);


    public sealed record PolicyDestinationUpdate
    {
        public Dictionary<Guid, string> Chats { get; } = new();

        public bool AllChats { get; }


        public PolicyDestinationUpdate()
        {
            AllChats = true;
        }

        public PolicyDestinationUpdate(Dictionary<Guid, string> chats)
        {
            Chats = chats;
        }

        public PolicyDestinationUpdate(bool allChats, Dictionary<Guid, string> chats) //TODO should be removed after Destination megration
        {
            AllChats = allChats;
            Chats = chats;
        }
    }
}