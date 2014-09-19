using System;

namespace FoxyProxy
{
    public interface IClient : IDisposable
    {
        void StartHandshake();
    }
}
