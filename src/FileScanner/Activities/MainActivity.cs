using System;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using FileSync.Android.Model;
using FileSync.Common;
using FileSync.Common.Config;
using Extensions = FileSync.Common.Extensions;
using Task = System.Threading.Tasks.Task;

namespace FileSync.Android.Activities
{
    [Activity(Label = "FileScanner", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private const int PermissionsTimeout = 10000;
        private const long DoublePressIntervalMs = 2000;

        private DateTime _lastPressTime = DateTime.Now.AddMilliseconds(-DoublePressIntervalMs);

        private readonly string[] _permissions =
        {
            Manifest.Permission.ReadExternalStorage,
            Manifest.Permission.WriteExternalStorage,
        };

        private TextView _logTextView;

        private const int RequestId = 244;

        private TaskCompletionSource<bool> _requestPermissionsTaskCompletionSource;
        private Button _syncBtn;

        private readonly ServerCollectionPing _servers;

        private Button _testBtn;
        private ListView _serverListView;
        private ServerListAdapter _serverListAdapter;

        public MainActivity()
        {
            _servers = new ServerCollectionPing();
            _servers.SetServerListFromConfig(FileSyncApp.Instance.Config.Servers);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.Main);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            //_logTextView = FindViewById<TextView>(Resource.Id.editText1);
            //_logTextView.TextAlignment = TextAlignment.Gravity;
            //_logTextView.MovementMethod = new ScrollingMovementMethod();

            _syncBtn = FindViewById<Button>(Resource.Id.button1);
            _syncBtn.Click += SyncBtn_OnClick;

            _testBtn = FindViewById<Button>(Resource.Id.button3);
            _testBtn.Click += ServersActivityBtn_OnClick;

            _serverListView = FindViewById<ListView>(Resource.Id.serverList);
            _serverListAdapter = new ServerListAdapter(this, _servers);
            _serverListView.Adapter = _serverListAdapter;
            _serverListView.ItemClick += ServerListViewOnItemClick;
        }

        protected override void Dispose(bool disposing)
        {
            _serverListAdapter?.Dispose();
            _serverListAdapter = null;

            _serverListView.ItemClick -= ServerListViewOnItemClick;

            base.Dispose(disposing);
        }

        public override void OnBackPressed()
        {
            var pressTime = DateTime.Now;
            if ((pressTime - _lastPressTime).TotalMilliseconds <= DoublePressIntervalMs)
            {
                FileSyncApp.Instance.Stop();
                System.Threading.Thread.Sleep(500);
                Java.Lang.JavaSystem.Exit(0);
            }

            _lastPressTime = pressTime;

            Toast.MakeText(this, "Press BACK again to exit", ToastLength.Short).Show();
        }

        private void ServerListViewOnItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            var item = _servers.Items[e.Position];
            Toast.MakeText(this, $"Server {item.Address}", ToastLength.Short).Show();
        }

        private void ServersActivityBtn_OnClick(object sender, EventArgs e)
        {
            StartActivity(typeof(DiscoverServerActivity));
        }

        private const int ServersActivityRequest = 114;

        protected override void OnResume()
        {
            _servers.SetServerListFromConfig(FileSyncApp.Instance.Config.Servers);
            _servers.RunPing();

            base.OnResume();
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);

            
        }

        /*protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == ServersActivityRequest)
            {
                if (resultCode == Result.Ok)
                {
                    AppendLog(data.Extras.GetString("server"));
                }
            }
        }*/

        private void AppendLog(string msg)
        {
            //RunOnUiThread(() => { _logTextView.Text = $"{msg}\r\n{_logTextView.Text}"; });
            System.Diagnostics.Debugger.Log(0, "", $">>>>>>> {msg}\r\n");
        }

        private async Task<bool> TryGetPermissionsAsync()
        {
            if ((int) Build.VERSION.SdkInt < 23)
            {
                return false;
            }

            if (CheckSelfPermission(_permissions[0]) == (int) Permission.Granted)
            {
                return true;
            }

            _requestPermissionsTaskCompletionSource = new TaskCompletionSource<bool>();

            await Task.Run(() => { RequestPermissions(_permissions, RequestId); });

            var requestPermissionsTask = _requestPermissionsTaskCompletionSource.Task;

            var timedOut = await Task.WhenAny(requestPermissionsTask, Task.Delay(PermissionsTimeout)) == requestPermissionsTask;

            return timedOut;
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            if (requestCode == RequestId)
            {
                _requestPermissionsTaskCompletionSource.SetResult(grantResults[0] == Permission.Granted);
            }
        }

        private async void SyncBtn_OnClick(object sender, EventArgs e)
        {
            _syncBtn.Enabled = false;

            try
            {
                var res = await TryGetPermissionsAsync();
                if (!res)
                {
                    Toast.MakeText(this, "Storage permission denied.", ToastLength.Short).Show();

                    return;
                }

                /*var config = _configStore.ReadClientOrDefault();
                if (config.Pairs == null || config.Pairs.Count == 0)
                {
                    Toast.MakeText(this, "No sync pair found.", ToastLength.Short).Show();

                    return;
                }
*/

                //SyncPairConfigModel pair = config.Pairs[0];

                //const string dir = @"/mnt/sdcard";
                //const string dir = @"/storage/emulated/0/stest/";
                const string dir = @"/storage/emulated/0/Download/";
                //const string dir = @"/storage/emulated/0/DCIM/";

                var pair = new SyncPairConfigModel
                {
                    BaseDir = dir,
                    DbDir = dir + ".sync",
                    ServerAddress = "10.0.2.2:9211",
                    SyncMode = SyncMode.TwoWay,
                };

                var client = SyncClientFactory.GetTwoWay(Extensions.ParseEndpoint(pair.ServerAddress), pair.BaseDir, pair.DbDir);

                client.Log += AppendLog;

                await client.Sync();
            }
            finally
            {
                _syncBtn.Enabled = true;
            }
        }
    }
}