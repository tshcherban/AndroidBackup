using System;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Text.Method;
using Android.Views;
using Android.Widget;
using FileSync.Android.Model;
using FileSync.Common;

namespace FileSync.Android.Activities
{
    [Activity(Label = "Folder sync status")]
    public class FolderViewActivity : Activity
    {
        private string _serverUrl;
        private ServerConfigItem _serverItem;
        private Guid _folderId;
        private FolderConfigItem _folderItem;
        private TextView _logTxtView;
        private Button _syncBtn;
        private ISyncClient _client;

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

            if (FileSyncApp.Instance.ActiveClients.TryGetValue(_folderId, out var client))
            {
                _syncBtn.Enabled = false;

                _client = client;
                _client.Log += AppendLog;
                _client.SyncTask.ContinueWith(x =>
                {
                    RunOnUiThread(() => { _syncBtn.Enabled = true; });
                    FileSyncApp.Instance.ActiveClients.Remove(_folderId);
                    _client.Log -= AppendLog;
                    _client = null;
                });
            }
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
                if (FileSyncApp.Instance.ActiveClients.ContainsKey(_folderId))
                    throw new Exception("WTF, sync in progress");

                _client = SyncClientFactory.GetTwoWay(Common.Extensions.ParseEndpoint(_serverUrl), _folderItem.LocalPath, null, FileSyncApp.Instance.Config.ClientId, _folderItem.Id);

                FileSyncApp.Instance.ActiveClients[_folderId] = _client; // TODO

                _client.Log += AppendLog;

                await Task.Run(_client.Sync);

                FileSyncApp.Instance.ActiveClients.Remove(_folderId);

                _client.Log -= AppendLog;
                _client = null;
            }
            finally
            {
                _syncBtn.Enabled = true;
                _client = null;
            }
        }

        protected override void OnStop()
        {
            base.OnPause();

            if (_client != null)
                _client.Log -= AppendLog;
        }

        protected override void OnResume()
        {
            base.OnResume();

            if (_client != null)
                _client.Log += AppendLog;
        }

        private void AppendLog(string msg)
        {
            RunOnUiThread(() => _logTxtView.Text = $"{msg}\r\n{_logTxtView.Text}");
        }
    }
}