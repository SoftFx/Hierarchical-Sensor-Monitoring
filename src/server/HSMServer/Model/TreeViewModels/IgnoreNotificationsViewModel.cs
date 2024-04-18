using HSMServer.Extensions;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;


namespace HSMServer.Model
{
    public class IgnoreNotificationsViewModel
    {
        private const string NodeTreeElement = "node";
        private const string SensorTreeElement = "sensor";
        private const string ProductTreeElement = "product";
        private const string FolderTreeElement = "folder";


        public string[] Paths { get; private set; }

        public string TreeElement { get; }

        public string[] Ids { get; set;}

        public TimeIntervalViewModel IgnorePeriod { get; set; }

        public int Days { get; set; }

        public int Hours { get; set; }

        public int Minutes { get; set; }

        public DateTime DateTimeNow { get; set; }

        public DateTime EndOfIgnorePeriod => IgnorePeriod.TimeInterval is TimeInterval.Forever ?
                                             DateTime.MaxValue : DateTimeNow.AddDays(Days).AddHours(Hours).AddMinutes(Minutes);


        //private IgnoreNotificationsViewModel(BaseNodeViewModel node)
        //{
        //    TreeElement = node switch
        //    {
        //        SensorNodeViewModel => SensorTreeElement,
        //        ProductNodeViewModel => NodeTreeElement,
        //        FolderModel => FolderTreeElement,
        //        _ => null
        //    };

        //    IgnorePeriod = new(PredefinedIntervals.ForIgnore, useCustomTemplate: false);

        //    DateTimeNow = DateTime.UtcNow.RoundToMin();
        //}

        //// public constructor without parameters for action Home/IgnoreNotifications
        public IgnoreNotificationsViewModel() { }

        //public IgnoreNotificationsViewModel(NodeViewModel node) : this((BaseNodeViewModel)node)
        //{
        //    Ids.Add(node.EncodedId);
        //    Paths.Add(node.FullPath);

        //    if (node.Id == node.RootProduct.Id)
        //        TreeElement = ProductTreeElement;
        //}

        //public IgnoreNotificationsViewModel(FolderModel folder) : this((BaseNodeViewModel)folder)
        //{
        //    Ids.Add(folder.Id.ToString());
        //    Paths.Add(folder.Name);
        //}

        public IgnoreNotificationsViewModel(List<BaseNodeViewModel> items)
        {
            Ids   = new string[items.Count];
            Paths = new string[items.Count];

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is NodeViewModel node)
                {
                    Ids[i]   = node.EncodedId;
                    Paths[i] = node.FullPath;

                    if (node.Id == node.RootProduct.Id)
                        TreeElement = ProductTreeElement;
                }
                else if (items[i] is FolderModel folder)
                {
                    Ids[i]   = folder.Id.ToString();
                    Paths[i] = folder.Name;
                }
            }

            IgnorePeriod = new(PredefinedIntervals.ForIgnore, useCustomTemplate: false);

            DateTimeNow = DateTime.UtcNow.RoundToMin();

            if (items.Count == 1)
            {
                TreeElement = items[0] switch
                {
                    SensorNodeViewModel => SensorTreeElement,
                    ProductNodeViewModel => NodeTreeElement,
                    FolderModel => FolderTreeElement,
                    _ => null
                };
            }
            else
                TreeElement = "items";
        }
    }
}
