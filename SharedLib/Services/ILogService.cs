using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLib.Services
{
    public interface ILogService
    {
        void Info(string message);
        void Error(Exception ex, string context);
    }

}
