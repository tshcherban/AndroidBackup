using System;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace FileSync.Android.Activities
{
    [Activity(Label = "ServerViewActivity")]
    public class ServerViewActivity : Activity
    {
        private string _serverAddress;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.ServerView);

            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            var btn = FindViewById<Button>(Resource.Id.serverViewRemoveServerBtn);
            btn.Click += RemoveServerBtn_Click;

            _serverAddress = Intent.GetStringExtra("server");
        }

        private async void RemoveServerBtn_Click(object sender, EventArgs e)
        {
            var d = new DialogHelper(this);
            var result = await d.ShowDialog("Confirm", "Delete server?", negativeButton: DialogHelper.MessageResult.No, positiveButton: DialogHelper.MessageResult.Yes);
            if (result != DialogHelper.MessageResult.Ok)
                return;

            FileSyncApp.Instance.Config.RemoveServer(_serverAddress);
            FileSyncApp.Instance.Config.Store();
            Finish();
        }
    }

    public class DialogHelper
    {
        public enum MessageResult
        {
            None = 0,
            Ok = 1,
            Cancel = 2,
            Abort = 3,
            Retry = 4,
            Ignore = 5,
            Yes = 6,
            No = 7
        }

        private readonly Activity _context;

        public DialogHelper(Activity activity)
        {
            _context = activity;
        }

        public Task<MessageResult> ShowDialog(string title, string message, bool setCancelable = false, bool setInverseBackgroundForced = false, MessageResult positiveButton = MessageResult.Ok, MessageResult negativeButton = MessageResult.None, MessageResult neutralButton = MessageResult.None)
        {
            var tcs = new TaskCompletionSource<MessageResult>();

            var builder = new AlertDialog.Builder(_context);
            //builder.SetIconAttribute(iconAttribute);
            builder.SetTitle(title);
            builder.SetMessage(message);
            //builder.SetInverseBackgroundForced(setInverseBackgroundForced);
            builder.SetCancelable(setCancelable);

            string GetBtnText(MessageResult res) => res != MessageResult.None ? res.ToString() : string.Empty;

            builder.SetPositiveButton(GetBtnText(positiveButton), delegate { tcs.SetResult(positiveButton); });
            builder.SetNegativeButton(GetBtnText(negativeButton), delegate { tcs.SetResult(negativeButton); });
            builder.SetNeutralButton(GetBtnText(neutralButton), delegate { tcs.SetResult(neutralButton); });

            builder.Show();

            // builder.Show();
            return tcs.Task;
        }
    }
}