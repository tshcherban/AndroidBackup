using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        private EditText _phoneNumberText;

        protected override async void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            _phoneNumberText = FindViewById<EditText>(Resource.Id.editText1);
            var btn = FindViewById<Button>(Resource.Id.button1);

            _phoneNumberText.TextAlignment = TextAlignment.Gravity;
            _phoneNumberText.Text = string.Join("\r\n", DriveInfo.GetDrives().Select(d => d.Name));

            btn.Click += BtnOnClick;
            comm = new Communicator("10.0.2.2");
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
                _phoneNumberText.Text += "\r\n";
                _phoneNumberText.Text += res;
            }
        }
        Communicator comm;
        private void BtnOnClick(object sender, EventArgs e)
        {
            try
            {
                var arg = new GetFileListRequest { RequestData = DateTime.Now.ToString("G") };
                var response = comm.SendReceiveCommand(GetFileListCommand.Instance, arg);
                Console.WriteLine($"Received {response.Data}");

                /*var fl1 = connection.SendReceiveObject<string, FileList>("GetFileList", "FileList", 10000, null);
                _phoneNumberText.Text = fl1.Text;*/
            }
            catch (Exception exception)
            {
                _phoneNumberText.Text = exception.ToString();
            }
        }
    }
}