using System;
using System.Linq;
using Android.App;
using Android.OS;
using Android.Text.Method;
using Android.Views;
using Android.Widget;
using FileSync.Common;

namespace FileSync.Android.Activities
{
    [Activity(Label = "FolderViewActivity")]
    public class FolderViewActivity : Activity
    {
        private string _serverUrl;
        private ServerConfigItem _serverItem;
        private Guid _folderId;
        private FolderConfigItem _folderItem;
        private TextView _logTxtView;
        private Button _syncBtn;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.FolderView);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            _serverUrl = Intent.GetStringExtra("server");
            _serverItem = FileSyncApp.Instance.Config.Servers.Single(x => x.Url == _serverUrl);
            _folderId = Guid.ParseExact(Intent.GetStringExtra("folderId"), "D");
            _folderItem = _serverItem.Folders.Single(x => x.Id == _folderId);

            FindViewById<TextView>(Resource.Id.folderViewNameView).Text = _folderItem.DisplayName;
            FindViewById<TextView>(Resource.Id.folderViewPathView).Text = _folderItem.LocalPath;

            _logTxtView = FindViewById<TextView>(Resource.Id.folderViewLogTxtView);
            _logTxtView.MovementMethod = new ScrollingMovementMethod();

            _syncBtn = FindViewById<Button>(Resource.Id.folderViewStartSyncBtn);
            _syncBtn.Click += SyncBtnOnClick;

            var btn = FindViewById<Button>(Resource.Id.folderViewDeleteFolderBtn);
            btn.Click += DeleteFolderBtn_Click;
        }

        private void DeleteFolderBtn_Click(object sender, EventArgs e)
        {
            _serverItem.Folders.Remove(_folderItem);
            FileSyncApp.Instance.Config.Store();
            Finish();
        }

        private async void SyncBtnOnClick(object sender, EventArgs e)
        {
            _syncBtn.Enabled = false;

            try
            {


                var client = SyncClientFactory.GetTwoWay(Common.Extensions.ParseEndpoint(_serverUrl), _folderItem.LocalPath, null, FileSyncApp.Instance.Config.ClientId, _folderItem.Id);

                client.Log += AppendLog;

                await client.Sync();
            }
            finally
            {
                _syncBtn.Enabled = true;
            }
        }

        private void AppendLog(string msg)
        {
            RunOnUiThread(() => _logTxtView.Text = $"{msg}\r\n{_logTxtView.Text}");
        }
    }
}