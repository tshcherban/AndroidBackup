using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using FileSync.Android.Helpers;
using FileSync.Android.Model;

namespace FileSync.Android.Activities
{
    [Activity(Label = "Discover server folders")]
    public sealed class FolderListScanActivity : Activity
    {
        private readonly FolderCollection _folders;

        private string _serverUrl;
        private ServerConfigItem _serverItem;
        private FolderListAdapter _foldersAdapter;
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
            _foldersListView.ItemClick += FoldersListViewOnItemClick;

            Task.Run(ScanServerFolders);
        }

        private void FoldersListViewOnItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            var item = _folders.Items[e.Position];
            var intent = new Intent(this, typeof(FolderAddActivity));
            intent.PutExtra("name", item.DisplayName);
            intent.PutExtra("server", _serverUrl);
            intent.PutExtra("folderId", item.Id.ToString("D"));

            StartActivity(intent);
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