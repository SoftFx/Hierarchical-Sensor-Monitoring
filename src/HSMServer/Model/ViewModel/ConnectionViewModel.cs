namespace HSMServer.Model.ViewModel
{
    public class ConnectionViewModel
    {
        public string Url { get; set; }
     
        public int Port { get; set; }

        public string SelectedPath { get; set; }

        public TreeViewModel Tree { get; set; }
    }
}
