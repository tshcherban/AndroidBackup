using System;
using Android.App;
using Android.Views;
using Android.Widget;

namespace FileSync.Android.Model
{
    public class ServerListAdapter : BaseAdapter<string>
    {
        private readonly ServerCollection _servers;
        private readonly bool _showStatus;
        private readonly Activity _context;

        public ServerListAdapter(Activity context, ServerCollection servers, bool showStatus)
        {
            _context = context;
            _servers = servers;
            _showStatus = showStatus;
            _servers.DataUpdated += ServersOnDataUpdated;
        }

        protected override void Dispose(bool disposing)
        {
            _servers.DataUpdated -= ServersOnDataUpdated;

            base.Dispose(disposing);
        }

        private void ServersOnDataUpdated()
        {
            _context.RunOnUiThread(NotifyDataSetChanged);
        }

        public override long GetItemId(int position)
        {
            return position;
        }

        public override string this[int position] => _servers.Items[position].Address;

        public override int Count => _servers.Items.Count;

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            var dataItem = _servers.Items[position];

            var view = convertView ?? _context.LayoutInflater.Inflate(Resource.Layout.ServerListItem, null);

            var text = dataItem.Address;
            if (_showStatus)
            {
                var stateName = dataItem.State ? "OK" : "Error";
                text += $"\t\t\t{stateName}";
            }

            view.FindViewById<TextView>(Resource.Id.Tv11).Text = text;

            return view;
        }
    }

    public class FolderListAdapter : BaseAdapter<FolderListDataItem>
    {
        private readonly FolderCollection _folders;
        private readonly Activity _context;
        
        public FolderListAdapter(FolderCollection folders, Activity context)
        {
            _folders = folders;
            _context = context;
            folders.DataUpdated += FoldersOnDataUpdated;
        }

        private void FoldersOnDataUpdated()
        {
            _context.RunOnUiThread(NotifyDataSetChanged);
        }

        public override long GetItemId(int position)
        {
            return position;
        }

        public override View GetView(int position, View convertView, ViewGroup parent)
        {
            var dataItem = _folders.Items[position];

            var view = convertView ?? _context.LayoutInflater.Inflate(Resource.Layout.FolderListItem, null);

            var text = dataItem.DisplayName;

            try
            {
                var textView = view.FindViewById<TextView>(Resource.Id.folderListItemFolderNameTxtView);
                if (textView != null)
                    textView.Text = text;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return view;
        }

        public override int Count => _folders.Items.Count;

        public override FolderListDataItem this[int position] => _folders.Items[position];
    }
}