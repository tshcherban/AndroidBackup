using System;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using FileSync.Android.Model;
using Task = System.Threading.Tasks.Task;

namespace FileSync.Android.Activities
{
    [Activity(Label = "Server list", MainLauncher = true)]
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

        private const int RequestId = 244;

        private TaskCompletionSource<bool> _requestPermissionsTaskCompletionSource;

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

            _testBtn = FindViewById<Button>(Resource.Id.button3);
            _testBtn.Click += ServersActivityBtn_OnClick;

            _serverListView = FindViewById<ListView>(Resource.Id.serverList);
            _serverListAdapter = new ServerListAdapter(this, _servers, true);
            _serverListView.Adapter = _serverListAdapter;
            _serverListView.ItemClick += ServerListViewOnItemClick;

            Task.Delay(1000).ContinueWith(t => TryGetPermissionsAsync());
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
            var intent = new Intent(this, typeof(ServerViewActivity));
            intent.PutExtra("server", item.Address);
            StartActivity(intent);
            //Toast.MakeText(this, $"Server {item.Address}", ToastLength.Short).Show();
        }

        private void ServersActivityBtn_OnClick(object sender, EventArgs e)
        {
            StartActivity(typeof(DiscoverServerActivity));
        }

        protected override void OnStop()
        {
            System.Diagnostics.Debugger.Log(0, "", ">>>>>> OnStop\r\n");

            _servers.StopPing();

            base.OnStop();
        }

        protected override void OnResume()
        {
            System.Diagnostics.Debugger.Log(0, "", ">>>>>> OnResume\r\n");
            _servers.SetServerListFromConfig(FileSyncApp.Instance.Config.Servers);
            _servers.RunPing();

            base.OnResume();
        }

        public override bool MoveTaskToBack(bool nonRoot)
        {
            System.Diagnostics.Debugger.Log(0, "", ">>>>>> MoveTaskToBack\r\n");
            return base.MoveTaskToBack(nonRoot);
        }

        protected override void OnPause()
        {
            System.Diagnostics.Debugger.Log(0, "", ">>>>>> OnPause\r\n");
            base.OnPause();
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
    }
}