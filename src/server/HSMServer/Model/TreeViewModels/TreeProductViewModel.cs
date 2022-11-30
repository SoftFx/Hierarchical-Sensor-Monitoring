namespace HSMServer.Model.TreeViewModels
{
    public sealed class TreeProductViewModel : TreeNodeViewModel
    {
        public string Id { get; private set; }

        public bool HasGroupNotifications { get; private set; }

        public int AllSensorsCount { get; private set; }


        internal TreeProductViewModel(string encodedId) : base(encodedId) { }


        internal void Update(ProductNodeViewModel viewModel)
        {
            base.Update(viewModel);

            Id = viewModel.Id;
            HasGroupNotifications = viewModel.TelegramSettings.Chats.Count > 0;
            AllSensorsCount = viewModel.AllSensorsCount;
        }
    }
}
