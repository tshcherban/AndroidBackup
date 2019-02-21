using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace FileSync.Android.Activities
{
    [Activity(Label = "FolderAddActivity")]
    public sealed class FolderAddActivity : Activity
    {
        private EditText _nameEdit;
        private EditText _pathEdit;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.FolderAdd);

            _nameEdit = FindViewById<EditText>(Resource.Id.folderAddViewNameEdit);
            _nameEdit.Text = Intent.GetStringExtra("name");

            _pathEdit = FindViewById<EditText>(Resource.Id.folderAddViewPathEdit);

            var addBtn = FindViewById<Button>(Resource.Id.folderAddViewAddFolderBtn);
            addBtn.Click += AddBtnOnClick;
        }

        private void AddBtnOnClick(object sender, EventArgs e)
        {
            var server = Intent.GetStringExtra("server");
            var srv = FileSyncApp.Instance.Config.Servers.Single(x => x.Url == server);
            var folderId = Guid.ParseExact(Intent.GetStringExtra("folderId"), "D");
            if (srv.Folders.Any(x => x.Id == folderId))
            {
                Toast.MakeText(this, "Folder already added", ToastLength.Short).Show();
                return;
            }
            srv.Folders.Add(new FolderConfigItem(folderId, _nameEdit.Text, _pathEdit.Text));
            FileSyncApp.Instance.Config.Store();
            Finish();
        }
    }
}