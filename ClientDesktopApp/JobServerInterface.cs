using Library_DLL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace ClientDesktopApp
{
    [ServiceContract]
    public interface JobServerInterface
    {
        [OperationContract]
        string Ping();

        [OperationContract]
        Job RequestJob();

        [OperationContract]
        void SubmitResult(int jobId, string result);

    }
}
