using Android.App;
using Android.Views;
using Android.Widget;

namespace FileSync.Android.Model
{
    public class ServerListAdapter : BaseAdapter<string>
    {
        private readonly ServerCollection _servers;
        private readonly Activity _context;

        public ServerListAdapter(Activity context, ServerCollection servers)
        {
            _context = context;
            _servers = servers;
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
            var stateName = dataItem.State ? "OK" : "Error";
            view.FindViewById<TextView>(Resource.Id.Tv11).Text = $"{dataItem.Address}\t\t\t{stateName}";

            return view;
        }
    }
}