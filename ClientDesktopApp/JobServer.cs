using ClientDesktopApp;
using Library_DLL;
using System.Linq;

namespace ClientDesktopApp
{
    public class JobServer : JobServerInterface
    {
        public string Ping()
        {
            return "Message from server";
        }

        // This method returns the next job that is "ToDo" and marks it as "InProgress"
        public Job RequestJob()
        {
            foreach (var job in JobList.Jobs)
            {
                if (job.Status.Equals(Job.JobStatus.ToDo))
                {
                    job.Status = Job.JobStatus.InProgress;
                    return job;
                }
            }
            return null;
        }

        // This method submits the result for a job, updating the job's result and status
        public void SubmitResult(int jobId, string result)
        {
            // Find the job by jobId
            var job = JobList.Jobs.FirstOrDefault(j => j.JobId == jobId);

            if (job != null)
            {
                job.Result = result;  
                job.Status = Job.JobStatus.Completed; 
            }
        }

    }
}
