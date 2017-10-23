using System;
using System.Collections.Generic;

namespace FileSync.Common
{
    public class SessionStorage
    {
        public static SessionStorage Instance = new SessionStorage();

        private readonly object _syncRoot = new object();

        private readonly List<Session> _sessions = new List<Session>();

        public Guid GetNewSession()
        {
            lock (_syncRoot)
            {
                var session = new Session {Id = Guid.NewGuid(), LastAccessTime = DateTime.Now};
                _sessions.Add(session);
                return session.Id;
            }
        }
    }
}