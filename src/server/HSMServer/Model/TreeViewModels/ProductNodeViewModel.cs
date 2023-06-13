using HSMCommon.Extensions;
using HSMServer.Core.Model;
using HSMServer.Helpers;
using HSMServer.Model.AccessKeysViewModels;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Notification.Settings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.TreeViewModel
{
    public class ProductNodeViewModel : NodeViewModel, INotificatable
    {
        public ConcurrentDictionary<Guid, ProductNodeViewModel> Nodes { get; set; } = new();

        public ConcurrentDictionary<Guid, SensorNodeViewModel> Sensors { get; set; } = new();

        public ConcurrentDictionary<Guid, AccessKeyViewModel> AccessKeys { get; } = new();


        public ClientNotifications Notifications { get; }

        public int AllSensorsCount { get; private set; }


        public override bool HasData => Sensors.Values.Any(s => s.HasData) || Nodes.Values.Any(n => n.HasData);

        public bool IsEmpty => AllSensorsCount == 0;

        public Guid? FolderId => Parent is FolderModel folder ? folder?.Id : null;


        public ProductNodeViewModel(ProductModel model, ProductNodeViewModel parent, FolderModel folder) : base(model)
        {
            Notifications = new(model.NotificationsSettings, () => Parent is FolderModel folder ? folder.Notifications : (Parent as ProductNodeViewModel)?.Notifications);

            Update(model);

            parent?.AddSubNode(this);

            if (folder != null)
                AddFolder(folder);
        }

        public ProductNodeViewModel GetPaginated(int pageNumber, int pageSize)
        {
            GridSensors.InitializeItems(Sensors.Values).TurnOnPagination();
            GridNodes.InitializeItems(Nodes.Values);
          
            return this;
        }


        public bool IsChangingAccessKeysAvailable(User user) =>
            user.IsAdmin || ProductRoleHelper.IsManager(Id, user.ProductsRoles);

        internal void AddSubNode(ProductNodeViewModel node)
        {
            node.Parent = this;
            Nodes.TryAdd(node.Id, node);
        }

        internal void AddSensor(SensorNodeViewModel sensor)
        {
            sensor.Parent = this;
            Sensors.TryAdd(sensor.Id, sensor);
        }

        internal void AddFolder(FolderModel folder)
        {
            folder.Products.Add(Id, this);
            UpdateFolder(folder);
        }

        internal void UpdateFolder(FolderModel folder) => Parent = folder;

        internal void AddAccessKey(AccessKeyViewModel key) => AccessKeys.TryAdd(key.Id, key);

        internal List<AccessKeyViewModel> GetAccessKeys() => AccessKeys.Values.ToList();

        internal ProductNodeViewModel RecalculateCharacteristics()
        {
            int allSensorsCount = 0;

            if (Nodes != null && !Nodes.IsEmpty)
            {
                foreach (var (_, node) in Nodes)
                {
                    node.RecalculateCharacteristics();
                    allSensorsCount += node.AllSensorsCount;
                }
            }
            
            AllSensorsCount = allSensorsCount + Sensors.Count;
            
            ModifyUpdateTime();
            ModifyStatus();

            return this;
        }

        private void ModifyUpdateTime()
        {
            var sensorMaxTime = Sensors.Values.MaxOrDefault(x => x.UpdateTime);
            var nodeMaxTime = Nodes.Values.MaxOrDefault(x => x.UpdateTime);

            UpdateTime = sensorMaxTime > nodeMaxTime ? sensorMaxTime : nodeMaxTime;
        }

        private void ModifyStatus()
        {
            var nodesStatus = Sensors.Values.MaxOrDefault(s => s.Status);
            var sensorStatus = Nodes.Values.MaxOrDefault(n => n.Status);

            Status = sensorStatus > nodesStatus ? sensorStatus : nodesStatus;
        }
    }
}
