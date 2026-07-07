using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.projectgenerator.Orchestration
{
    public class PortAllocator
    {
        private readonly HashSet<int> _usedPorts = new();
        private readonly object _lock = new object();

        // Standard ranges to fallback to if defaults are taken
        private const int PortRangeStart = 10000;
        private const int PortRangeEnd = 15000;
        private int _nextPort = PortRangeStart;

        public int Allocate(int defaultPort)
        {
            lock (_lock)
            {
                if (defaultPort > 0 && !_usedPorts.Contains(defaultPort))
                {
                    _usedPorts.Add(defaultPort);
                    return defaultPort;
                }

                // If default is taken (or 0), find next available in the fallback range
                while (_usedPorts.Contains(_nextPort))
                {
                    _nextPort++;
                    if (_nextPort > PortRangeEnd)
                    {
                        _nextPort = PortRangeStart; // Wrap around
                    }
                }

                _usedPorts.Add(_nextPort);
                return _nextPort++;
            }
        }

        public void Release(int port)
        {
            lock (_lock)
            {
                _usedPorts.Remove(port);
            }
        }
    }
}
