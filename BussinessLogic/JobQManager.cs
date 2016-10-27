using System;
using System.Diagnostics;
using System.Text.RegularExpressions; 
using System.Management;
using System.Text;
using System.IO;
using System.Net;
//using Microsoft.Practices.EnterpriseLibrary.ExceptionHandling; 
//using Microsoft.Practices.EnterpriseLibrary.ExceptionHandling.Logging;
using System.Xml;
using Humana.H1.Common.Caching;
using Humana.H1.JobService.Common.DataTransferObjects;
using Humana.H1.JobService.DataAccessLayer;

namespace Humana.H1.JobService.Core.BussinessLogic
{
	/// <summary>
	/// Summary description for JobQManager.
	/// </summary>
	public class JobQManager
	{
        private static readonly JobQManager _JobQManager = new JobQManager();
		//private static object syncRoot = new Object();
        public int SleepInterval = 15000;
        public bool ServiceRunning = false;
        public int JobInProgress = 0;
        private bool ? _CanCancelJob = null;

        public JobQManager()
		{
			
		}

		public static JobQManager Instance
		{
			get 
			{
                //if (_JobQManager == null) 
                //{
                //    lock (syncRoot) 
                //    {
                //        if (_JobQManager == null) 
                //            _JobQManager = new JobQManager();
                //    }
                //}
				return _JobQManager;
			}
		}

        public void ProcessOutStandingJobs(string serviceName, string includedJobIdents, string excludedJobIdents)
		{
            try
            {

                Trace.WriteLine("Starting function checkForNewReport() was called");
                Trace.Indent();

                //int numberJobsProcessed = 0;
                string runCommand;
                string url;
                string jobError = "";
                string jobMessages;
                int nMaxLength = 7500;
                string strOut = "";
                string strError = "";
                string resultOutput = "";


                JobQDAL jobqDAL = JobQDAL.Instance;

                //while (ServiceRunning)
                //{
                    try
                    {
                        JobQDTO jobInfo = jobqDAL.GetNextJobDTO(includedJobIdents, excludedJobIdents);

                        //if (!jobInfo.JobQID.Equals(0))
                        while (jobInfo != null)
                        {
                            //numberJobsProcessed++;
                            runCommand = "";
                            url = "";
                            strOut = "";
                            strError = "";
                            resultOutput = "";
                            jobMessages = "";
                            jobError = "";

                            JobQDefDTO jobDefInfo = new JobQDefDTO();
                            jobDefInfo = jobqDAL.GetJobDefDTO(jobInfo.JobDefIdent);

                            strOut = " Job Id  = " + jobInfo.JobQID.ToString() + ", Service Name = " + serviceName
                                    + ", Machine Name = " + System.Environment.MachineName + ", User Name = " + System.Environment.UserDomainName + "\\" + System.Environment.UserName
                                    + ", PID = " + System.Diagnostics.Process.GetCurrentProcess().Id.ToString() + ", Assembly Version = " + Humana.H1.Common.Utilities.GetAssemblyVersionInfo() + ".     "
                                    + System.Environment.NewLine;

                            try
                            {
                                if (jobDefInfo.WebServiceURL.Equals(""))
                                {
                                    string arguments = string.Empty;
                                    runCommand = string.Empty;
                                    BuildRunCommand(jobInfo, jobDefInfo, ref runCommand, ref arguments);
                                    RunCmd(runCommand, jobDefInfo.MaxTimeToCompletion, ref strOut, ref strError, serviceName, jobInfo.JobQID, arguments);
                                }
                                else
                                {
                                    url = BuildWebServiceCall(jobInfo, jobDefInfo);
                                    CallWebService(url, ref strError, serviceName);
                                    strOut += url;
                                }
                            }
                            catch (Exception e)
                            {
                                strError += e.ToString();
                            }

                            // sql has trouble with embedded ' char -- fix by replacing with ''
                            jobError = strError.Replace("\0", "");
                            jobError = jobError.Replace("'", "''");

                            //if (jobError.Length > nMaxLength) jobError = jobError.Substring(0, nMaxLength);
                            jobMessages = Regex.Replace(strOut, "'", "''");
                            if (jobMessages.Length > nMaxLength) jobMessages = jobMessages.Substring(0, nMaxLength);

                            //Add the Job ID to the error message
                            if (jobError.Length > 0)
                            {
                                jobError = " Job Id  = " + jobInfo.JobQID.ToString() + System.Environment.NewLine
                                    + " Service Name = " + serviceName + System.Environment.NewLine
                                    + " Machine Name = " + System.Environment.MachineName + System.Environment.NewLine
                                    + " User Name = " + System.Environment.UserDomainName + "\\" + System.Environment.UserName + System.Environment.NewLine
                                    + " PID = " + System.Diagnostics.Process.GetCurrentProcess().Id.ToString() + System.Environment.NewLine
                                    + " Assembly Version = " + Humana.H1.Common.Utilities.GetAssemblyVersionInfo() + ".     "
                                    + System.Environment.NewLine
                                    + jobError;

                            }

                            jobqDAL.UpdateJobStatus(jobInfo.JobQID, jobMessages, jobError, jobDefInfo.JobOwnsStatuses);

                            //Send an email if all retrys failed 
                            if (jobError.Length > 0 && jobDefInfo.JobOwnsErrorEmail == false && jobInfo.RetryCount >= jobInfo.RetryMax)
                            {
                                string subject = H1Properties.GetProperty("Adapt.Reports.CoreMetrics.Environment") + ": " + jobInfo.JobDefIdent + " Encountered An Error";
                                jobqDAL.SendEMail(jobError, subject, jobDefInfo.OnErrorEmail);
                            }

                            EventLog.WriteEntry(serviceName + ".Run", string.Format("Ran job {0} using definition for {1}.\n\nSuccess? {2}", jobInfo.JobQID, jobInfo.JobDefIdent, jobError.Length.Equals(0) ? "Yes" : "No"), EventLogEntryType.Information);
        
                            if (ServiceRunning)
                                jobInfo = jobqDAL.GetNextJobDTO(includedJobIdents, excludedJobIdents);
                            else
                                jobInfo = null;
                        }
                        //else
                        //{
                        //    if (numberJobsProcessed == 0)
                        //    {
                        //        resultOutput += "No Jobs found.";
                        //        Trace.WriteLine (resultOutput);
                        //    }
                        //    else
                        //    {
                        //        resultOutput += String.Format("Processed {0} Jobs.", numberJobsProcessed);
                        //    }
                        //    //break; not needed this function will be called only once
                        //}
                    }
                    catch (Exception e)
                    {
                        EventLog.WriteEntry(serviceName + ".Run", "Error found: " + e.Message.ToString(), EventLogEntryType.Error);
                        Trace.Unindent();
                    }
                    //System.Threading.Thread.Sleep(SleepInterval);
                //}

            }
            catch (Exception e)
            {
                EventLog.WriteEntry(serviceName + ".Run", "Error found: " + e.Message.ToString(), EventLogEntryType.Error);
                Trace.Unindent();
            }
			Trace.Unindent();
		}
		
		/// <summary>
		/// Calls a web service using the HTTP-GET on the url constructed by BuildWebServiceCall
		/// </summary>
		/// <param name="url">the url that will be called (HTTP-GET query string attached)</param>
		/// <param name="error">a string to hold errors</param>
		/// <param name="serviceName">name of this service</param>
		/// <returns>a response code denoting the success or failure of the job</returns>
		private int CallWebService(string url,ref string error, string serviceName)
		{
			int	response_code = -1;				

			HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);        		
			webRequest.Credentials	  = CredentialCache.DefaultCredentials;
			webRequest.ContentType	  = "text/xml;charset=\"utf-8\"";        
			webRequest.Accept		  = "text/xml";        
			webRequest.Method		  = "GET";  
			
			webRequest.Headers.Add("SOAPAction",url);
			
			try
			{
				//get the response object from the web service
				using (WebResponse serviceResponse = webRequest.GetResponse())
				{
					//extract the stream from the response object
					using (StreamReader reader = new StreamReader(serviceResponse.GetResponseStream()))
					{												
						XmlDocument temp = new XmlDocument();

						//load the stream contents into temp
						temp.LoadXml(reader.ReadToEnd());

						//pull the response_code out of the XML
						response_code = Convert.ToInt32(temp.SelectSingleNode("jobinfo").SelectSingleNode("response_code").InnerXml);

						//if the web service failed internally
						if(!response_code.Equals(0))
						{
							//get the contents of the exception tag
							error = Convert.ToString(temp.SelectSingleNode("jobinfo").SelectSingleNode("exception").InnerXml);
						}
					} 
				}
			}
			catch (WebException e)
			{
				error = e.Message;
				EventLog.WriteEntry(serviceName + ".Run", "Error found: " + e.Message.ToString(), EventLogEntryType.Error );
				Trace.Unindent();
			}
			catch(Exception e)
			{
				error = e.Message;
				EventLog.WriteEntry(serviceName + ".Run", "Error found: " + e.Message.ToString(), EventLogEntryType.Error );
				Trace.Unindent();
			}

			return response_code;

		}

		/// <summary>
		/// Builds a HTTP-GET url to be used for calling the web service using parmaters defined by jobInfo
		/// </summary>
		/// <param name="jobInfo">a JobQDTO that gives us information about the specific execution of the job</param>
		/// <param name="jobDefInfo">a JobQDefDTO that gives us general information about the execution of a job of this type</param>
		/// <returns>a url that calls the web service requested by the jobQDTO </returns>
		private string BuildWebServiceCall (JobQDTO jobInfo,JobQDefDTO jobDefInfo)
		{
			string [] strJobDefParams     = new string [10];
			string [] strJobParams		  = new string [10];
			string [] strJobDefTypeParams = new string [10];
			string [] strParams			  = new string [10]; 
			StringBuilder webServiceCall  = new StringBuilder(string.Concat(jobDefInfo.WebServiceURL,"?"),1024);


			//setup params from DTO 
			strJobParams[0] = jobInfo.Param1;
			strJobParams[1] = jobInfo.Param2;
			strJobParams[2] = jobInfo.Param3;
			strJobParams[3] = jobInfo.Param4;
			strJobParams[4] = jobInfo.Param5;
			strJobParams[5] = jobInfo.Param6;
			strJobParams[6] = jobInfo.Param7;
			strJobParams[7] = jobInfo.Param8;
			strJobParams[8] = jobInfo.Param9;
			strJobParams[9] = jobInfo.Param10;

			strJobDefParams[0] = jobDefInfo.Param1;
			strJobDefParams[1] = jobDefInfo.Param2;
			strJobDefParams[2] = jobDefInfo.Param3;
			strJobDefParams[3] = jobDefInfo.Param4;
			strJobDefParams[4] = jobDefInfo.Param5;
			strJobDefParams[5] = jobDefInfo.Param6;
			strJobDefParams[6] = jobDefInfo.Param7;
			strJobDefParams[7] = jobDefInfo.Param8;
			strJobDefParams[8] = jobDefInfo.Param9;
			strJobDefParams[9] = jobDefInfo.Param10;

			strJobDefTypeParams[0] = jobDefInfo.Param1Type;
			strJobDefTypeParams[1] = jobDefInfo.Param2Type;
			strJobDefTypeParams[2] = jobDefInfo.Param3Type;
			strJobDefTypeParams[3] = jobDefInfo.Param4Type;
			strJobDefTypeParams[4] = jobDefInfo.Param5Type;
			strJobDefTypeParams[5] = jobDefInfo.Param6Type;
			strJobDefTypeParams[6] = jobDefInfo.Param7Type;
			strJobDefTypeParams[7] = jobDefInfo.Param8Type;
			strJobDefTypeParams[8] = jobDefInfo.Param9Type;
			strJobDefTypeParams[9] = jobDefInfo.Param10Type;

			if(jobDefInfo.UseJobQIDasOnlyParam.Equals(false))
			{
				//loop over the params
				for(int i = 0; i < strParams.GetUpperBound(0); i++)
				{				
					//if there is a jobdeftype param and a jobparam that are both populated
					if(!(strJobDefTypeParams[i].Equals("") || strJobParams[i].Equals("")) || string.Compare(strJobDefTypeParams[i],"JobQID",true).Equals(0))
					{
						if(string.Compare(strJobDefTypeParams[i],"JobQID",true).Equals(0))
						{
							//get jobQ's value from the DTO since it can't be known at insert time of the JobQ record (thus wouldn't be in the appropriate parameter spot)
							webServiceCall.Append(string.Concat(strJobDefTypeParams[i],"=",jobInfo.JobQID,"&"));
						}
						else
						{
							//add it on to the query string in a "<type_param>=<job_param>&" fashion
							webServiceCall.Append(string.Concat(strJobDefTypeParams[i],"=",strJobParams[i],"&"));
						}
					}
				}
			}
			else
			{
				webServiceCall.Append(string.Concat("JobQID=",jobInfo.JobQID,"&"));
			}

		//return the url with the last & trimmed
		return  webServiceCall.ToString().Substring(0,webServiceCall.ToString().Length-1);

		}

		private string BuildRunCommand (JobQDTO jobInfo,JobQDefDTO jodDefInfo,ref string strProgram,ref string strParameters)
		{
			
			string [] strJobDefParams = new string [10];
			string [] strJobParams = new string [10];
			string [] strJobDefTypeParams = new string [10];
			string [] strParams = new string [10]; 
			StringBuilder RunCmd = new StringBuilder("", 512);


			//setup params from DTO 
			strJobParams[0] = jobInfo.Param1;
			strJobParams[1] = jobInfo.Param2;
			strJobParams[2] = jobInfo.Param3;
			strJobParams[3] = jobInfo.Param4;
			strJobParams[4] = jobInfo.Param5;
			strJobParams[5] = jobInfo.Param6;
            strJobParams[6] = jobInfo.Param7;
			strJobParams[7] = jobInfo.Param8;
			strJobParams[8] = jobInfo.Param9;
			strJobParams[9] = jobInfo.Param10;

			strJobDefParams[0] = jodDefInfo.Param1;
			strJobDefParams[1] = jodDefInfo.Param2;
			strJobDefParams[2] = jodDefInfo.Param3;
			strJobDefParams[3] = jodDefInfo.Param4;
			strJobDefParams[4] = jodDefInfo.Param5;
			strJobDefParams[5] = jodDefInfo.Param6;
			strJobDefParams[6] = jodDefInfo.Param7;
			strJobDefParams[7] = jodDefInfo.Param8;
			strJobDefParams[8] = jodDefInfo.Param9;
			strJobDefParams[9] = jodDefInfo.Param10;

			strJobDefTypeParams[0] = jodDefInfo.Param1Type;
			strJobDefTypeParams[1] = jodDefInfo.Param2Type;
			strJobDefTypeParams[2] = jodDefInfo.Param3Type;
			strJobDefTypeParams[3] = jodDefInfo.Param4Type;
			strJobDefTypeParams[4] = jodDefInfo.Param5Type;
			strJobDefTypeParams[5] = jodDefInfo.Param6Type;
			strJobDefTypeParams[6] = jodDefInfo.Param7Type;
			strJobDefTypeParams[7] = jodDefInfo.Param8Type;
			strJobDefTypeParams[8] = jodDefInfo.Param9Type;
			strJobDefTypeParams[9] = jodDefInfo.Param10Type;




			
			for (int i=0; i < 10; i++)
			{
				if(strJobDefTypeParams[i].Equals("FILESPEC"))
				{
					
					if (strJobDefTypeParams[i].EndsWith("\\"))
					{
						strJobDefTypeParams[i] += '\\';
					}

					if(strJobParams.Length.Equals(0) && strJobDefParams[i].Length > 0 )
					{
						strParams[i] = strJobDefParams[i];
					}
					else if (!Regex.IsMatch(strJobParams[i], ":|\\\\", RegexOptions.IgnoreCase) && strJobDefParams[i].Length > 0)
					{
						strParams[i] = strJobDefParams[i] + strJobParams[i];
					}
				}
				else if(strJobParams.Length.Equals(0) && strJobDefParams[i].Length > 0)
				{
					strParams[i] = strJobDefParams[i];
				}
				else
				{
					strParams[i] = strJobParams[i];
				}

			}
		
			RunCmd.Length = 0;
			RunCmd.Append("\"");
			RunCmd.Append(jodDefInfo.ScriptToCall);
			RunCmd.Append("\"");
            strProgram = RunCmd.ToString();
			if (jodDefInfo.UseJobQIDasOnlyParam) 
			{
				RunCmd.Append(" \"");
				RunCmd.Append(jobInfo.JobQID.ToString());
				RunCmd.Append("\"");
                strParameters = " \"" + jobInfo.JobQID.ToString() + "\"";
			}
			else
			{
				for (int i=0; i< 10 && !strParams[i].Length.Equals(0); i++) 
				{
					RunCmd.Append(" \"");
					RunCmd.Append(strParams[i]);
					RunCmd.Append("\"");
                    strParameters += " \"" + strParams[i] + "\"";
				}
			}
			
			return RunCmd.ToString();								

		}

        private bool CanCancelJob
        {   
            get 
            {
                if (_CanCancelJob == null)
                {
                    string configSetting = System.Configuration.ConfigurationSettings.AppSettings.Get("CanCancelJob");
                    if (configSetting != null)
                        _CanCancelJob = (System.Configuration.ConfigurationSettings.AppSettings.Get("CanCancelJob").ToLower() == "true");
                    else
                        _CanCancelJob = false;
                }
                return (bool)_CanCancelJob;
            }
        }
		private void RunCmd (string strRunCmd, int intMaxTimeToCompletion, ref string strStandardOut, ref string strError,string serviceName,int jobQID,string arguments)
		{
			int iWaitTimeInterval = 500; // ms
			int iTotalWaitTime = 0;
			bool boolReturn = false;
            bool jobCanceled = false;

			//  Mark Boyle - April 14th, 2002
			try
			{
                using (System.Diagnostics.Process MyProc = new Process())
                {
                    //StreamWriter sw;
                    StreamReader sr;
                    StreamReader err;

                    strStandardOut += strRunCmd + arguments + System.Environment.NewLine;
                    //ProcessStartInfo psInfo = new ProcessStartInfo("cmd");
                    ProcessStartInfo psInfo = new ProcessStartInfo(strRunCmd, arguments);

                    psInfo.UseShellExecute = false;		//probably set to true and not use cmd?
                    //psInfo.RedirectStandardInput = true;
                    psInfo.RedirectStandardInput = false; //no input is needed
                    psInfo.RedirectStandardOutput = true;
                    psInfo.RedirectStandardError = true;
                    psInfo.CreateNoWindow = true;
                    psInfo.ErrorDialog = false;

                    MyProc.StartInfo = psInfo;

                    MyProc.Start();
                    //sw = MyProc.StandardInput;
                    sr = MyProc.StandardOutput;

                    err = MyProc.StandardError;

                    //sw.AutoFlush = true;
                    //sw.WriteLine(strRunCmd);
                    strError = "";
                    //strStandardOut = "";
                    //not sure about this one - need to figure out what happens after reaching timeout (error?,status?)

                    // Put this in loop to prevent deadlock if we get too much
                    // output from called process
                    // WARNING: If you start a .cmd file that then uses the start
                    // command, this code is worthless. The .cmd file will exit
                    // normally and you have not waited for the actual process

                    do
                    {
                        // try 500 milliseconds first
                        MyProc.WaitForExit(iWaitTimeInterval);
                        boolReturn = MyProc.HasExited;

                        if (!boolReturn)
                        {
                            // these hopefully avoid an infinite read block
                            // .Net does not seem to have read timeout for streams... yet
                            // .NET V2.0 has Serial class coming though
                            //ReadAvailable(ref err,ref strError,serviceName );
                            //ReadAvailable(ref sr,ref strStandardOut,serviceName);
                            iTotalWaitTime += iWaitTimeInterval;
                            if (CanCancelJob && JobQDAL.Instance.IsJobCanceled(jobQID))
                            {
                                jobCanceled = true;
                                break;
                            }
                        }
                    } while (!boolReturn && (iTotalWaitTime < intMaxTimeToCompletion));


                    //sw.Close();
                    //ReadAvailable(ref err,ref strError,serviceName);
                    //err.Close();
                    //ReadAvailable(ref sr,ref strStandardOut,serviceName);
                    //sr.Close();

                    if (boolReturn == false)
                    {
                        if (jobCanceled)
                        {
                            strError += System.Environment.NewLine + "User Canceled the Job!";
                            strStandardOut += System.Environment.NewLine + "User Canceled the Job!";
                        }
                        else
                            strError += System.Environment.NewLine + "Aborted process Max Time To Completion Reached before an Exit!";
                        //MyProc.Kill();
                        killProcess(MyProc.Id, serviceName);
                        MyProc.WaitForExit(3000);
                    }

                    ReadAvailable(ref err, ref strError, serviceName);
                    err.Close();
                    ReadAvailable(ref sr, ref strStandardOut, serviceName);
                    sr.Close();

                    //ALWAYS CLOSE THIS
                    MyProc.Close();
                }
				
			}
			catch (Exception e)
			{
				EventLog.WriteEntry(serviceName + ".Run", "Process running your command CRASHED: error: " + e.Message.ToString(), EventLogEntryType.Error);
				strError += "Process running your command CRASHED: error: " + e.Message.ToString();
			
			}

		}
        const int READ_BUFF_SZ = 1024;
		private void ReadAvailable(ref StreamReader strm, ref string inStr,string serviceName)
		{
			
			StringBuilder AvailableString = new StringBuilder();
			
			try	{
                //if ((!strm.EndOfStream) && (strm.Peek() > 0))
                {
                    //int cInIdx = 0;
                    //char[] cInArr = new char[READ_BUFF_SZ]; // match default buffer size
                    //int readCount = 0;
                    //do
                    //{
                    //    cInArr[0] = '\0';
                    //    readCount = strm.Read(cInArr, cInIdx, READ_BUFF_SZ);
                    //    AvailableString.Append(cInArr);
                    //    cInIdx += readCount;
                    //}
                    //while (readCount >= READ_BUFF_SZ);
                    string line;
                    while ((line = strm.ReadLine()) != null)
                    {
                        AvailableString.Append(line);
                    }

                    inStr += AvailableString.ToString().Replace("\0", "");
                }
                //while (strm.Peek() >= 0) 
                //{
                //    strm.Read(cInArr, cInIdx++, 1);
                //    if(cInIdx == cInArr.GetLength(0))
                //    {
                //        AvailableString.Append(cInArr);
                //        inStr += AvailableString.ToString();
                //    }
                //}

			
				//AvailableString.Append(cInArr);
			}
			catch (Exception e)
			{
				EventLog.WriteEntry(serviceName + ".Run", "Error Reading Stream error: " + e.Message.ToString(), EventLogEntryType.Error);
				throw e;
			}

		}

		public bool killProcess(int pid,string serviceName)
		{
			bool didIkillAnybody = false;
			try
			{
				Process[] procs = Process.GetProcesses();
				for (int i = 0; i < procs.Length; i++)
				{
					if (GetParentProcess(procs[i].Id) == pid)
						/*
						 * the good thing about this 
						 * is that we will also kill
						 * any child for the child process! ;o)
						 * will this be a memory hog?
						 */
						if (killProcess(procs[i].Id,serviceName) == true)
							didIkillAnybody = true;
				}
				try
				{
					/*
					 * basically, the fact that we killed all the child
					 * could have DEVASTED the parent which, sadly,
					 * could have commited suicide!
					 */
					Process myProc = Process.GetProcessById(pid);
                    if (myProc!=null)
					    myProc.Kill();

					return true;
				}
				catch { }
			}
			catch (Exception ex)
			{
				try
				{
					//write exception to log 
					EventLog.WriteEntry(serviceName + ".Run", "Error Killing processes" + ex.Message.ToString(), EventLogEntryType.Error);
				}
				catch { }
			}
			return didIkillAnybody;
		}

		/*
		 * get parent process for a given PID
		 */ 
		private int GetParentProcess(int Id)
		{
			int parentPid = 0;
			using (ManagementObject mo = new ManagementObject("win32_process.handle='" + Id.ToString() + "'"))
			{
				mo.Get();
				parentPid = Convert.ToInt32(mo["ParentProcessId"]);
			}
			return parentPid;
		}





	}
}
