using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Android.App;
using Android.Widget;
using Android.OS;
using Android.Views;
using Common.Protocol;

namespace FileScanner
{
    [Activity(Label = "FileScanner", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private TextView _text;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            _text = FindViewById<TextView>(Resource.Id.editText1);
            var btn = FindViewById<Button>(Resource.Id.button1);

            _text.TextAlignment = TextAlignment.Gravity;
            //_text.Text = string.Join("\r\n", DriveInfo.GetDrives().Select(d => d.Name));

            btn.Click += BtnOnClick;
            _communicator = new Communicator("10.0.2.2");
            return;
            using (var alg = SHA1.Create())
            {
                var files = Directory.GetFiles("/storage/sdcard/dcim", "*", SearchOption.AllDirectories)
                    .Select(fpath =>
                    {
                        var sha1 = alg.ComputeHash(File.OpenRead(fpath));
                        return $"{fpath}\r\n{string.Concat(sha1.Select(s => s.ToString("x")))}";
                    }).ToList();
                var res = string.Join("\r\n", files);
                _text.Text += "\r\n";
                _text.Text += res;
            }
        }
        Communicator _communicator;
        private async void BtnOnClick(object sender, EventArgs e)
        {
            try
            {
                var filePath = "/storage/sdcard/dcim/file1";
                var fileBytes = File.ReadAllBytes(filePath);
                var data = new SendFileCommandData {Data = fileBytes};
                var s = new Stopwatch();
                s.Restart();
                await Task.Run(() => _communicator.SendReceiveCommand(SendFileCommand.Instance, data));
                s.Stop();
                var str = $"{fileBytes.Length} bytes in {s.Elapsed.TotalMilliseconds} ms ({fileBytes.Length/ s.Elapsed.TotalSeconds :F2} bytes/s)";
                _text.Text = str;
            }
            catch (Exception exception)
            {
                _text.Text = exception.ToString();
            }
        }
    }
}