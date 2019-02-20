 using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Widget;
using FileSync.Android.Model;

namespace FileSync.Android.Activities
{
    [Activity(Label = "ServerViewActivity")]
    public sealed class FolderListScanActivity : Activity
    {
        private string _serverUrl;
        private ServerConfigItem _serverItem;
        private FolderListAdapter _foldersAdapter;
        private FolderCollection _folders;
        private ListView _foldersListView;

        public FolderListScanActivity()
        {
            _folders = new FolderCollection();
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.FolderListScan);

            _serverUrl = Intent.GetStringExtra("server");
            _serverItem = FileSyncApp.Instance.Config.Servers.Single(x => x.Url == _serverUrl);

            _foldersAdapter = new FolderListAdapter(_folders, this);

            _foldersListView = FindViewById<ListView>(Resource.Id.folderListScanViewFolderListView);
            _foldersListView.Adapter = _foldersAdapter;

            Task.Run(ScanServerFolders);
        }

        private async Task ScanServerFolders()
        {
            var ep = Common.Extensions.ParseEndpoint(_serverUrl);

            var comm = new ServerCommunicator();
            var folders = await comm.GetFolders(FileSyncApp.Instance.Config.ClientId, ep.Address, ep.Port);
            
            _folders.SetList(folders);
        }
    }
}