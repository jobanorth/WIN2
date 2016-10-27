using System;
using System.Collections.Generic;
using System.Text;

using Humana.H1.JobService.Common;
using Humana.H1.JobService.Common.DataTransferObjects;
using Humana.H1.JobService.DataAccessLayer;


namespace Humana.H1.JobService.Core.BussinessLogic
{
    public class JobHandler : STSMessages
    {
        private JobQDTO _jobQDDTO;

        public JobHandler(JobQDTO jobQDDTO)
        {
            _jobQDDTO = jobQDDTO;
        }
        public void StartJob()
		{
            job = new System.Threading.Thread(new System.Threading.ThreadStart(DoTheJob));
			job.Start();
		}
		public bool IsComplete
		{
			get
			{
				return complete;
			}
		}
		private bool complete = false;
		private System.Threading.Thread job;
		public void DoTheJob()
		{
			try
			{
                JobQDAL jobqDAL = JobQDAL.Instance;
                JobQDefDTO jobDefInfo = new JobQDefDTO();
                jobDefInfo = jobqDAL.GetJobDefDTO(jobInfo.JobDefIdent);

                if (jobDefInfo.WebServiceURL.Equals(""))
                {
                    runCommand = BuildRunCommand(jobInfo, jobDefInfo);
                    RunCmd(runCommand, jobDefInfo.MaxTimeToCompletion, ref strOut, ref strError, serviceName);
                }
                else
                {
                    url = BuildWebServiceCall(jobInfo, jobDefInfo);
                    CallWebService(url, ref strError, serviceName);
                    strOut = url;
                }
            }
            catch (Exception exc)
            {
                AddError(exc);
            }
            finally
            {
                complete = true;
            }
        }
    }
}
