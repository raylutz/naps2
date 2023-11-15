using Common.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAPS2.Logging
{
    public static class BatchLog
    {
        private readonly static ILog _batchLog = LogManager.GetLogger("batchlogfile");

        public static void Info(string message) => _batchLog.Info(message);
    }
}
