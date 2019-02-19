using System;

namespace FileSync.Android.Model
{
    public class ServerListDataItem
    {
        public string Address { get; set; }

        public Guid Id { get; set; }
        
        public bool State { get; set; }
    }
}