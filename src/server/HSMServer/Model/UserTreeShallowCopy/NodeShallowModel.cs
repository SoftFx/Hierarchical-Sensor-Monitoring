﻿using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class NodeShallowModel : BaseNodeShallowModel<ProductNodeViewModel>
    {
        private const int MaxRenderWidth = 100;

        private readonly Dictionary<Guid, NodeShallowModel> _subNodes = new(1 << 2);
        private readonly Dictionary<Guid, SensorShallowModel> _sensors = new(1 << 2);

        private readonly Predicate<NodeShallowModel> _nodeFilter;
        private readonly Predicate<SensorShallowModel> _sensorFilter;


        public List<NodeShallowModel> RenderedNodes { get; } = new(1 << 4);

        public List<SensorShallowModel> RenderedSensors { get; } = new(1 << 4);


        public IntegrationState GrafanaState { get; } = new();

        public UserNotificationsState AccountState { get; } = new();


        public override bool IsGrafanaEnabled => GrafanaState.IsAllEnabled;

        public override bool IsAccountsEnable => AccountState.IsAllEnabled;

        public override bool IsAccountsIgnore => AccountState.IsAllIgnored;


        public int RenderWidthDifference { get; private set; }

        public int VisibleSubtreeSensorsCount { get; private set; }


        public string SensorsCountString
        {
            get
            {
                var sensorsCount = VisibleSubtreeSensorsCount == Data.AllSensorsCount
                    ? $"{Data.AllSensorsCount}"
                    : $"{VisibleSubtreeSensorsCount}/{Data.AllSensorsCount}";

                return $"({sensorsCount} sensors)";
            }
        }

        private bool CanAddToRender => RenderedNodes.Count + RenderedSensors.Count < MaxRenderWidth;


        internal NodeShallowModel(ProductNodeViewModel data, User user, Predicate<NodeShallowModel> nodeFilter, Predicate<SensorShallowModel> sensorFilter) : base(data, user)
        {
            _nodeFilter = nodeFilter;
            _sensorFilter = sensorFilter;
        }


        internal SensorShallowModel AddChild(SensorShallowModel shallowSensor, User user)
        {
            shallowSensor.Parent = this;

            _sensors.Add(shallowSensor.Id, shallowSensor);

            var sensor = shallowSensor.Data;

            if (sensor.State != SensorState.Muted)
            {
                AccountState.CalculateState(user.Notifications, sensor.Id);
                UpdateGroupsState(shallowSensor);
            }

            var isSensorMuted = sensor.State == SensorState.Muted;

            _mutedValue = !_mutedValue.HasValue ? isSensorMuted : _mutedValue & isSensorMuted;

            GrafanaState.CalculateState(shallowSensor);

            if (user.IsSensorVisible(sensor))
                VisibleSubtreeSensorsCount++;

            return shallowSensor;
        }

        internal NodeShallowModel AddChild(NodeShallowModel node)
        {
            node.Parent = this;

            _subNodes.Add(node.Data.Id, node);

            if (node._mutedValue.HasValue)
            {
                if (!node._mutedValue.Value)
                {
                    AccountState.CalculateState(node.AccountState);
                    UpdateGroupsState(node);
                }

                _mutedValue = !_mutedValue.HasValue ? node._mutedValue : _mutedValue & node._mutedValue;
            }

            GrafanaState.CalculateState(node.GrafanaState);

            VisibleSubtreeSensorsCount += node.VisibleSubtreeSensorsCount;

            return node;
        }

        internal void ToRenderNode(Guid nodeId)
        {
            void Recalculate<T>(Dictionary<Guid, T> total, List<T> render, Predicate<T> filter)
            {
                if (total.TryGetValue(nodeId, out var item) && filter(item))
                {
                    if (CanAddToRender)
                    {
                        render.Add(item);
                        total.Remove(nodeId);
                    }
                    else
                        RenderWidthDifference++;
                }
            }

            Recalculate(_subNodes, RenderedNodes, _nodeFilter);
            Recalculate(_sensors, RenderedSensors, _sensorFilter);
        }

        internal void LoadRenderingNodes()
        {
            foreach (var id in _subNodes.Keys)
                ToRenderNode(id);

            foreach (var id in _sensors.Keys)
                ToRenderNode(id);
        }
    }
}
