using System;
using System.Linq;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FileSync.Android.Helpers;
using FileSync.Android.Model;

namespace FileSync.Android.Activities
{
    [Activity]
    public sealed class ServerViewActivity : Activity
    {
        private readonly FolderCollection _folders;

        private string _serverUrl;
        private ListView _foldersListView;
        private FolderListAdapter _folderListAdapter;
        private ServerConfigItem _serverItem;

        public ServerViewActivity()
        {
            _folders = new FolderCollection();
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.ServerView);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            Title = $"{_serverUrl} sync folders";

            var removeServerBtn = FindViewById<Button>(Resource.Id.serverViewRemoveServerBtn);
            removeServerBtn.Click += RemoveServerBtn_Click;

            var addFolderBtn = FindViewById<Button>(Resource.Id.serverViewAddFolderBtn);
            addFolderBtn.Click += AddFolderBtn_Click;

            _serverUrl = Intent.GetStringExtra("server");
            _serverItem = FileSyncApp.Instance.Config.Servers.Single(x => x.Url == _serverUrl);

            _folders.SetServerListFromConfig(_serverItem.Folders);

            _folderListAdapter = new FolderListAdapter(_folders, this);

            _foldersListView = FindViewById<ListView>(Resource.Id.serverViewFolderListView);
            _foldersListView.Adapter = _folderListAdapter;
            _foldersListView.ItemClick += FolderListViewOnItemClick;
        }

        protected override void OnResume()
        {
            base.OnResume();

            _folders.SetServerListFromConfig(_serverItem.Folders);
        }

        private void AddFolderBtn_Click(object sender, EventArgs e)
        {
            var intent = new Intent(this, typeof(FolderListScanActivity));
            intent.PutExtra("server", _serverUrl);
            StartActivity(intent);
        }

        private void FolderListViewOnItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            var folder = _serverItem.Folders[e.Position];

            var intent = new Intent(this, typeof(FolderViewActivity));
            intent.PutExtra("server", _serverUrl);
            intent.PutExtra("folderId", folder.GetStrId());
            StartActivity(intent);
        }

        private async void RemoveServerBtn_Click(object sender, EventArgs e)
        {
            var d = new DialogHelper(this);
            var result = await d.ShowDialog("Confirm", "Delete server?", negativeButton: DialogHelper.MessageResult.No, positiveButton: DialogHelper.MessageResult.Yes);
            if (result != DialogHelper.MessageResult.Yes)
                return;

            FileSyncApp.Instance.Config.RemoveServer(_serverUrl);
            FileSyncApp.Instance.Config.Store();
            Finish();
        }
    }
}