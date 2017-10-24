using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FileSync.Common
{
    public class SessionStorage
    {
        public const int CreateSessionTimeoutSeconds = 3;
        public const int SessionTimeoutMinutes = 2;

        public static readonly SessionStorage Instance = new SessionStorage();
        
        private static readonly TimeSpan CreateSessionTimeout = TimeSpan.FromSeconds(CreateSessionTimeoutSeconds);

        private readonly object _syncRoot = new object();
        private readonly List<Session> _sessions = new List<Session>();

        private DateTime _lastSessionCreated = DateTime.MinValue;

        public Session GetNewSession()
        {
            lock (_syncRoot)
            {
                var dif = DateTime.Now - _lastSessionCreated;
                if (dif < CreateSessionTimeout)
                {
                    var sleepSpan = CreateSessionTimeout - dif;
                    Console.WriteLine($"New session requests too frequent. Sleeping for {sleepSpan:g} ms");
                    Task.Delay(sleepSpan).Wait();
                }

                var time = DateTime.Now;
                var session = new Session {Id = Guid.NewGuid(), LastAccessTime = time, CreateTime = time};
                _sessions.Add(session);
                _lastSessionCreated = DateTime.Now;
                return session;
            }
        }

        public Session GetSession(Guid sessionId)
        {
            lock (_syncRoot)
            {
                return _sessions.SingleOrDefault(i => i.Id == sessionId);
            }
        }

        public void CloseSession(Session session)
        {
            lock (_syncRoot)
            {
                _sessions.Remove(session);
            }
        }
    }
}