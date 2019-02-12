using System;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Text.Method;
using Android.Views;
using Android.Widget;
using FileSync.Common;
using FileSync.Common.Config;
using Extensions = FileSync.Common.Extensions;
using Task = System.Threading.Tasks.Task;

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

        private const int RequestId = 244;

        private TaskCompletionSource<bool> _requestPermissionsTaskCompletionSource;
        private Button _syncBtn;

        private readonly string _dataDir;
        private readonly string _clientConfigPath;
        private SyncServiceConfigStore _configStore;
        private Button _testBtn;

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

            _testBtn = FindViewById<Button>(Resource.Id.button3);
            _testBtn.Click += TestBtn_OnClick;
        }

        private void TestBtn_OnClick(object sender, EventArgs e)
        {
            StartActivityForResult(typeof(DiscoverServerActivity), 0);
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (resultCode == Result.Ok)
            {
                AppendLog(data.Extras.GetString("server"));
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