
using System;
using System.Data;
using System.Diagnostics;
using System.IO;

    class WebInformation
    {

        public const int MAX_WEB_REQUESTS = 300;
        public const int MAX_APP_POOLS = 100;
        public const int MAX_SITES = 100;

        //public ServiceConfigurations.AppConfigs.SYSTEM_INFO SysInfo = new ServiceConfigurations.AppConfigs.SYSTEM_INFO();
        public SharedData g_SharedData = new SharedData();

        /*
        
    public struct stWPList
    {
        public string strInstanceName;
        public string strPID;
        public string strAppPoolDesc;
    }

    public stWPList[] arrWPList = new stWPList[MAX_APP_POOLS];

    public struct stSiteList
    {
        string strSiteID;
        string strSiteDesc;
    }

    public stSiteList[] arrSiteList = new stSiteList[MAX_SITES];

    public struct stJavaWPList
    {
        public string strInstanceName;
        public string strPID;
    }
    
    public stJavaWPList[] arrJavaWPList = new stJavaWPList[MAX_APP_POOLS];

    public struct stWebRequests
    {
        public string strURL;
        public int iTimeTaken;
        public string strClientIP;
    }

    public stWebRequests[] arrWebRequests = new stWebRequests[MAX_WEB_REQUESTS];

    public struct stWebInfo
    {
        public int iNumberOfRequests;
        public int iNumberOfAppPools;
        public int iNumberOfJavaPools;
        public int iNumberOfSites;
    }

         * 
         */
        //private int iNumberOfRequests;

        EventRecorder WSPEvent = new EventRecorder();


        public void GetRunningWebApplications()
        {
            int iRequests = 0;
            DataTable dtWebApps = new DataTable();

            dtWebApps.Columns.Add(new DataColumn("URL", typeof(string)));
            dtWebApps.Columns.Add(new DataColumn("TIME_TAKEN", typeof(int)));
            dtWebApps.Columns.Add(new DataColumn("CLIENT", typeof(string)));

            if (g_SharedData.SYSINFO.iWinVer < 2008)
            {
                g_SharedData.WEBINFO.iNumberOfRequests = 0;
                return;
            }

            int iIndex = 0;
            string strCommand = "";
            string strOutput = "";

            string[] astrURLs = new string[MAX_WEB_REQUESTS];
            string[] astrClients = new string[MAX_WEB_REQUESTS];
            string[] astsrTimeTaken = new string[MAX_WEB_REQUESTS];

            try
            {
                string strCommandPath = "c:\\Windows\\System32\\inetsrv\\appcmd.exe";

                if (File.Exists(strCommandPath))
//                if (System.IO.Directory.Exists(strCommandPath))
                    strCommand = LaunchEXE.Run("c:\\windows\\system32\\inetsrv\\appcmd", "LIST REQUEST", 10);
                else
                {
                    g_SharedData.WEBINFO.iNumberOfRequests = 0;
                    return;
                }
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "E", 1109);
            }

            strOutput = strCommand.ToUpper();

            if (!strOutput.Contains("REQUEST"))
            {
                g_SharedData.WEBINFO.iNumberOfRequests = 0;
                return;
            }

            int i = 0;
            iIndex = strCommand.Length;
            strOutput = strCommand;

            do
            {
                iIndex = strOutput.IndexOf("/");
                if (iIndex > 1)
                {
                    strOutput = strOutput.Substring(iIndex);
                    iIndex = strOutput.IndexOf(',', 0);
                    if (iIndex > 0)
                    {
                        astrURLs[i] = strOutput.Substring(0, iIndex);
                        strOutput = strOutput.Substring(iIndex + 1);
                        i = i + 1;
                    }
                }
            } while (iIndex > 0 && i < MAX_WEB_REQUESTS);
            iRequests = i;

            i = 0;
            iIndex = 0;
            strOutput = strCommand.ToLower();

            do
            {
                iIndex = strOutput.IndexOf("time:");
                if (iIndex > 1)
                {
                    strOutput = strOutput.Substring(iIndex + 5);
                    iIndex = strOutput.IndexOf(' ', 0);
                    if (iIndex > 0)
                    {
                        astsrTimeTaken[i] = strOutput.Substring(0, iIndex);
                        strOutput = strOutput.Substring(iIndex + 1);
                        i = i + 1;
                    }
                }
            } while (iIndex > 0 && i < MAX_WEB_REQUESTS);

            i = 0;
            iIndex = 0;
            strOutput = strCommand.ToLower();

            do
            {
                iIndex = strOutput.IndexOf("client:");
                if (iIndex > 1)
                {
                    strOutput = strOutput.Substring(iIndex + 7);
                    iIndex = strOutput.IndexOf(',', 0);
                    if (iIndex > 0)
                    {
                        astrClients[i] = strOutput.Substring(0, iIndex);
                        strOutput = strOutput.Substring(iIndex + 1);
                        i = i + 1;
                    }
                }
            } while (iIndex > 0 && i < MAX_WEB_REQUESTS);

            for (i = 0; i < iRequests; i++)
            {
                //dtWebApps.Rows.Add(astrURLs[i], Convert.ToInt32(astsrTimeTaken[i]), astrClients[i]);
                g_SharedData.arrWebRequests[i].strURL = astrURLs[i];
                g_SharedData.arrWebRequests[i].iTimeTaken = Convert.ToInt32(astsrTimeTaken[i]);
                g_SharedData.arrWebRequests[i].strClientIP = astrClients[i];
            }
            
            g_SharedData.WEBINFO.iNumberOfRequests = iRequests;

            return;
        }


        public void GetIIS6SiteList()
        {

            string[] astrSiteID = new string[40];
            string[] astrSiteDesc = new string[40];
            string strCommand = LaunchEXE.Run("cscript", "c:\\windows\\system32\\iisweb.vbs /query", 10);
            string strOutput = strCommand;
            int iIndex = strCommand.Length;
            int i = 0;

            if (iIndex > 1)
            {
                do
                {
                    iIndex = strOutput.IndexOf("(W3SVC/");
                    if (iIndex > 1)
                    {
                        strOutput = strOutput.Substring(iIndex + 7);
                        iIndex = strOutput.IndexOf(")");
                        if (iIndex > 0)
                        {
                            astrSiteID[i] = strOutput.Substring(0, iIndex);
                            strOutput = strOutput.Substring(iIndex + 1);
                            i = i + 1;
                        }
                    }
                } while (iIndex > 0 && i < MAX_SITES);
                g_SharedData.WEBINFO.iNumberOfSites = i;
            }

            i = 0;
            strOutput = strCommand;

            iIndex = strOutput.IndexOf("==\r\n");
            strOutput = strOutput.Substring(iIndex);

            if (iIndex > 1)
            {
                do
                {
                    iIndex = strOutput.IndexOf("\r\n");
                    if (iIndex > 1)
                    {
                        strOutput = strOutput.Substring(iIndex + 2);
                        iIndex = strOutput.IndexOf(" (");
                        if (iIndex > 0)
                        {
                            astrSiteDesc[i] = strOutput.Substring(0, iIndex);
                            strOutput = strOutput.Substring(iIndex + 1);
                            i = i + 1;
                        }
                    }
                } while (iIndex > 0 && i < MAX_SITES);
            }

            //dtSiteList.Rows.Clear();

            for (i = 0; i < g_SharedData.WEBINFO.iNumberOfSites; i++)
            {
                g_SharedData.arrSiteList[i].strSiteDesc = astrSiteDesc[i];
                g_SharedData.arrSiteList[i].strSiteID = astrSiteID[i];
                g_SharedData.WEBINFO.iNumberOfSites = i + 1;
                //dtSiteList.Rows.Add(astrSiteID[i], astrSiteDesc[i]);
            }
        }

        public bool GetWebSiteList()
        {
            try
            {
                if (GetSiteList())
                    return true;
                else
                    return false;
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "W", 1147);
            }
            return false;
        }

        private bool GetSiteList()
        {
            //          SITE "myCMS" (id:2,bindings:http/*:80:mycms,state:Started)

            if (g_SharedData.SYSINFO.iWinVer == 0)
                return false;

            if (g_SharedData.SYSINFO.iWinVer == 2003)
            {
                GetIIS6SiteList();
                return true;
            }

            string strCommand = LaunchEXE.Run("c:\\windows\\system32\\inetsrv\\appcmd.exe", "list site", 10);
            string strOutput = strCommand;
            int iIndex = strCommand.Length;
            string[] astrSiteID = new string[40];
            string[] astrSiteDesc = new string[40];

            int i = 0;
            if (iIndex > 1)
            {
                do
                {
                    iIndex = strOutput.IndexOf('"', 0);
                    if (iIndex > 1)
                    {
                        strOutput = strOutput.Substring(iIndex + 1);
                        iIndex = strOutput.IndexOf('"', 0);

                        if (iIndex > 1)
                        {
                            astrSiteDesc[i] = strOutput.Substring(0, iIndex);
                            strOutput = strOutput.Substring(iIndex + 1);
                            i = i + 1;
                        }
                    }
                } while (iIndex > 0 && i < 40);

                g_SharedData.WEBINFO.iNumberOfSites = i;
            }

            i = 0;
            iIndex = strCommand.Length;
            strOutput = strCommand;

            if (iIndex > 1)
            {
                do
                {
                    iIndex = strOutput.IndexOf("id:");
                    if (iIndex > 1)
                    {
                        strOutput = strOutput.Substring(iIndex + 3);
                        iIndex = strOutput.IndexOf(",");
                        if (iIndex > 0)
                        {
                            astrSiteID[i] = strOutput.Substring(0, iIndex);
                            strOutput = strOutput.Substring(iIndex + 1);
                            i = i + 1;
                        }
                    }
                } while (iIndex > 0 && i < 40);
            }

            //dtSiteList.Rows.Clear();

            //for (i = 0; i < g_SharedData.WEBINFO.iNumberOfSites; i++)
              //  dtSiteList.Rows.Add(astrSiteID[i], astrSiteDesc[i]);

            for (i = 0; i < g_SharedData.WEBINFO.iNumberOfSites; i++)
            {
                g_SharedData.arrSiteList[i].strSiteDesc = astrSiteDesc[i];
                g_SharedData.arrSiteList[i].strSiteID = astrSiteID[i];
                g_SharedData.WEBINFO.iNumberOfSites = i + 1;
            }

            return true;
        }

        public void GetIIS6WPList()
        {
            int iIndex = 0;
            int i = 0;
            string strCommand;
            string strOutput;
            string[] astrPID = new string[40];
            string[] astrAppPoolDesc = new string[40];
            //g_SharedData.WEBINFO.iNumberOfAppPools = 0;

            strCommand = LaunchEXE.Run("cscript", "c:\\windows\\system32\\iisapp.vbs", 10);
            //W3WP.exe PID: 5268   AppPoolId: DefaultAppPool
            strOutput = strCommand;
            iIndex = strOutput.Length;

            if (iIndex > 1)
            {
                do
                {
                    iIndex = strOutput.IndexOf("PID: ");
                    if (iIndex > 1)
                    {
                        strOutput = strOutput.Substring(iIndex + 5);
                        iIndex = strOutput.IndexOf(' ', 0);
                        if (iIndex > 0)
                        {
                            astrPID[i] = strOutput.Substring(0, iIndex);
                            strOutput = strOutput.Substring(iIndex + 1);
                            i = i + 1;
                        }
                    }
                } while (iIndex > 0 && i < MAX_APP_POOLS);
                g_SharedData.WEBINFO.iNumberOfAppPools = i;
            }

            strOutput = strCommand;
            iIndex = strOutput.Length;
            i = 0;

            if (iIndex > 1)
            {
                do
                {
                    iIndex = strOutput.IndexOf("AppPoolId: ");
                    if (iIndex > 1)
                    {
                        strOutput = strOutput.Substring(iIndex + 11);
                        iIndex = strOutput.IndexOf('\r', 0);
                        if (iIndex > 0)
                        {
                            astrAppPoolDesc[i] = strOutput.Substring(0, iIndex);
                            strOutput = strOutput.Substring(iIndex + 1);
                            i = i + 1;
                        }
                    }
                } while (iIndex > 0 && i < MAX_APP_POOLS);
            }

            if (g_SharedData.WEBINFO.iNumberOfAppPools > 0)
            {
                g_SharedData.SYSINFO.bIsIIS = true;          // Set IIS is true to avoid java process checking every 5 seconds.
            }

            string[] arrInstanceNames;
            PerformanceCounterCategory pcCat = new PerformanceCounterCategory();
            pcCat.CategoryName = "Process";

            arrInstanceNames = pcCat.GetInstanceNames();
            int iCount = 0;

            foreach (string strProcess in arrInstanceNames)
            {
                if (strProcess == "w3wp" || strProcess.Contains("w3wp#"))
                {
                    PerformanceCounter PC = new PerformanceCounter();

                    PC.CategoryName = "Process";
                    PC.CounterName = "ID Process";
                    PC.InstanceName = strProcess;
                    PC.NextValue();
                    System.Threading.Thread.Sleep(100);

                    string strPValue = PC.NextValue().ToString();

                    for (i = 0; i < g_SharedData.WEBINFO.iNumberOfAppPools; i++)
                    {
                        if (astrPID[i] == strPValue)
                        {
                            //dtWPList.Rows.Add(strProcess, strPValue, astrAppPoolDesc[i]);
                            g_SharedData.arrWPList[iCount].strAppPoolDesc = astrAppPoolDesc[i];
                            g_SharedData.arrWPList[iCount].strInstanceName = strProcess;
                            g_SharedData.arrWPList[iCount].strPID = strPValue;
                        }
                    }
                }
            }
        }

        public void GetWorkerProcessList()
        {
            try
            {
                GetWPList();
                if (!g_SharedData.SYSINFO.bIsIIS)
                    GetJavaWPList();
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "I", 1116);
            }
        }

        private void GetJavaWPList()
        {
            string[] astrPID = new string[MAX_APP_POOLS];

            //if (dtJavaWP.Rows.Count > 0)
            //    dtJavaWP.Rows.Clear();

            string[] arrInstanceNames;
            PerformanceCounterCategory pcCat = new PerformanceCounterCategory();
            pcCat.CategoryName = "Process";

            arrInstanceNames = pcCat.GetInstanceNames();

            int i = 0;

            foreach (string strProcess in arrInstanceNames)
            {
                if (strProcess == "java" || strProcess.Contains("java#") || strProcess == "javaw" || strProcess.Contains("javaw#"))
                {
                    PerformanceCounter PC = new PerformanceCounter();

                    PC.CategoryName = "Process";
                    PC.CounterName = "ID Process";
                    PC.InstanceName = strProcess;
                    PC.NextValue();
                    System.Threading.Thread.Sleep(100);

                    string strPValue = PC.NextValue().ToString();

                    //dtJavaWP.Rows.Add(strPValue, strProcess);
                    g_SharedData.arrJavaWPList[i].strInstanceName = strProcess;
                    g_SharedData.arrJavaWPList[i].strPID = strPValue;
                    g_SharedData.WEBINFO.iNumberOfJavaPools = i + 1;
                    i++;
                    if (i >= MAX_APP_POOLS)
                        break;
                }
            }
        }

        private void GetWPList()
        {

            if (g_SharedData.SYSINFO.iWinVer == 2003)
            {
                GetIIS6WPList();
                return;
            }

            int iIndex = 0;
            string strCommand;
            string strOutput;
            string[] astrPID = new string[MAX_APP_POOLS];
            string[] astrAppPoolDesc = new string[MAX_APP_POOLS];
            //g_SharedData.WEBINFO.iNumberOfAppPools = 0;

            string strCommandPath = "c:\\Windows\\System32\\inetsrv\\appcmd.exe";
            if (File.Exists(strCommandPath))
                strCommand = LaunchEXE.Run("c:\\windows\\system32\\inetsrv\\appcmd.exe", "list wp", 10);
            else
                return;

            //WP "3676" (applicationPool:DefaultAppPool)

            strOutput = strCommand;

            ////test code
            //if(g_SharedData.WSP_AGENT_SETTING.strWS_URL.Contains("juliabook"))
            //    WSPEvent.WriteEvent(strOutput, "I", 4001);

            iIndex = strOutput.Length;

            int i = 0;
            if (iIndex > 1)
            {
                do
                {
                    iIndex = strOutput.IndexOf('"', 0);
                    if (iIndex > 1)
                    {
                        strOutput = strOutput.Substring(iIndex + 1);
                        iIndex = strOutput.IndexOf('"', 0);

                        if (iIndex > 0)
                        {
                            astrPID[i] = strOutput.Substring(0, iIndex);
                            strOutput = strOutput.Substring(iIndex + 1);
                            i = i + 1;
                        }
                    }
                } while (iIndex > 0 && i < MAX_APP_POOLS);

                g_SharedData.WEBINFO.iNumberOfAppPools = i;
            }

            i = 0;
            iIndex = strCommand.Length;
            strOutput = strCommand;

            if (iIndex > 1)
            {
                do
                {
                    iIndex = strOutput.IndexOf(':', 0);
                    if (iIndex > 1)
                    {
                        strOutput = strOutput.Substring(iIndex + 1);
                        iIndex = strOutput.IndexOf(')', 0);
                        if (iIndex > 0)
                        {
                            astrAppPoolDesc[i] = strOutput.Substring(0, iIndex);
                            strOutput = strOutput.Substring(iIndex + 1);
                            i = i + 1;
                        }
                    }
                } while (iIndex > 0 && i < MAX_APP_POOLS);
            }

            if (g_SharedData.WEBINFO.iNumberOfAppPools > 0)
            {
                g_SharedData.SYSINFO.bIsIIS = true;          // Set IIS is true to avoid java process checking every 5 seconds.
            }

            string[] arrInstanceNames;
            PerformanceCounterCategory pcCat = new PerformanceCounterCategory();
            pcCat.CategoryName = "Process";

            arrInstanceNames = pcCat.GetInstanceNames();
            
            int iCount = 0;
            
            foreach (string strProcess in arrInstanceNames)
            {
                if (strProcess == "w3wp" || strProcess.Contains("w3wp#"))
                {
                    PerformanceCounter PC = new PerformanceCounter();

                    PC.CategoryName = "Process";
                    PC.CounterName = "ID Process";
                    PC.InstanceName = strProcess;
                    PC.NextValue();
                    System.Threading.Thread.Sleep(100);

                    string strPValue = PC.NextValue().ToString();

                    for (i = 0; i < g_SharedData.WEBINFO.iNumberOfAppPools; i++)
                        if (astrPID[i] == strPValue)
                        {
                            //dtWPList.Rows.Add(strProcess, strPValue, astrAppPoolDesc[i]);
                            g_SharedData.arrWPList[iCount].strPID = strPValue;
                            g_SharedData.arrWPList[iCount].strAppPoolDesc = astrAppPoolDesc[i];
                            g_SharedData.arrWPList[iCount].strInstanceName = strProcess;
                            iCount++;
                        }
                }

            }

        }
    }

/*
C:\Windows\System32\inetsrv>appcmd list request
REQUEST "f600000080000006" (url:GET /test_pages/longtime.aspx, time:4071 msec, client:localhost, stage:ExecuteRequestHandler, module:ManagedPipelineHandler)
REQUEST "f500000080000002" (url:GET /test_pages/longtime.aspx, time:2823 msec, client:localhost, stage:ExecuteRequestHandler, module:ManagedPipelineHandler)
*/