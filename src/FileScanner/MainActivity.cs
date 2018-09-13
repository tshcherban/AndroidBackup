using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Text.Method;
using Android.Views;
using Android.Widget;
using FileSync.Common;
using FileSync.Common.Config;

namespace FileSync.Android
{
    [Activity(Label = "FileScanner", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private const int PermissionsTimeout = 10000;

        private readonly string[] _permissions =
        {
            Manifest.Permission.ReadExternalStorage,
            Manifest.Permission.WriteExternalStorage,
        };

        private TextView _logTextView;

        private Task<IPEndPoint> _discoverTask;

        private const int RequestId = 1;

        private TaskCompletionSource<bool> _requestPermissionsTaskCompletionSource;
        private Button _syncBtn;

        private readonly string _dataDir;
        private readonly string _clientConfigPath;
        private SyncServiceConfigStore _configStore;

        public MainActivity()
        {
            _dataDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
            _clientConfigPath = "/sdcard/client.json";

            _configStore = new SyncServiceConfigStore(_clientConfigPath);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.Main);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            _logTextView = FindViewById<TextView>(Resource.Id.editText1);
            _logTextView.TextAlignment = TextAlignment.Gravity;
            _logTextView.MovementMethod = new ScrollingMovementMethod();

            _syncBtn = FindViewById<Button>(Resource.Id.button1);
            _syncBtn.Click += SyncBtn_OnClick;

            //Task.Run(WaitAndDiscover);
        }

        private async Task WaitAndDiscover()
        {
            await Task.Delay(2000);

            await ClientDiscover();
        }

        private async Task ClientDiscover()
        {
            var cts = new TaskCompletionSource<IPEndPoint>();
            _discoverTask = cts.Task;

            try
            {
                AppendLog("Discovering...");

                using (var client = new UdpClient())
                {
                    var requestData = Encoding.ASCII.GetBytes("SomeRequestData");

                    client.EnableBroadcast = true;
                    await client.SendAsync(requestData, requestData.Length, new IPEndPoint(IPAddress.Broadcast, 8888));

                    var serverResponseData = await client.ReceiveAsync();
                    var serverResponse = Encoding.ASCII.GetString(serverResponseData.Buffer);

                    var port = int.Parse(serverResponse.Replace("port:", null));

                    var ss = $"Discovered on {serverResponseData.RemoteEndPoint.Address}:{port}";

                    AppendLog(ss);

                    cts.SetResult(new IPEndPoint(serverResponseData.RemoteEndPoint.Address, port));

                    client.Close();
                }

                AppendLog("Discover done");
            }
            catch (Exception e)
            {
                AppendLog($"Error while discovering\r\n{e}");
            }
        }

        private void AppendLog(string msg)
        {
            RunOnUiThread(() => { _logTextView.Text = $"{msg}\r\n{_logTextView.Text}"; });
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

                var config = _configStore.ReadClientOrDefault();
                if (config.Pairs == null || config.Pairs.Count == 0)
                {
                    Toast.MakeText(this, "No sync pair found.", ToastLength.Short).Show();

                    return;
                }

                var pair = config.Pairs[0];

                AppendLog($"Starting sync with {pair.ServerAddress}:{pair.ServerPort}...");

                //const string dir = @"/mnt/sdcard";
                //const string dir = @"/storage/emulated/0/stest/";
                //const string dir = @"/storage/emulated/0/music/";
                //const string dir = @"/storage/emulated/0/DCIM/";

                var baseDir = pair.BaseDir;
                var dbDir = pair.DbDir;

                var client = SyncClientFactory.GetTwoWay(pair.ServerAddress, int.Parse(pair.ServerPort), baseDir, dbDir);

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