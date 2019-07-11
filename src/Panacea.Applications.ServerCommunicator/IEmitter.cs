using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerCommunicator
{
    public interface IEmitter
    {
        void Emit(string eventName, object data);
    }
}
