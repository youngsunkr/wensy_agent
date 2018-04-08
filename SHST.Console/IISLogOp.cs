
using System;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Xml;

using System.Text;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Collections.Generic;
using System.Web.Services;


namespace IISLogHandler
{
    class IISLogOperation
    {

        //get
        //public string strIISLogDirectory;
        //public int iWinVersion;
        //public string strConnectionString;
        //public string strHostName;
        //public string strHostHeader;

        //public DataTable dtAnalyzeHistory = new DataTable();
        public SharedData g_SharedData = new SharedData();

        //set

        string strLogTimeFrom;
        string strLogTimeTo;
        string strLogTimeDate;
        string strAnalyzedLogTime;    // 2012-09-22 16:00:00
        string strSavedLogTime;
        string strIISLogFileName;

        string strWinTempPath;

        int iRandom = -1;

        string logparser_path = "C:\\Program Files (x86)\\Log Parser 2.2\\logparser.exe";

        // To support turn-over log files by time
        public string strLastAnalyzedFile = "";
        string strLastLogFile = "";
        bool bIsFileBySize = false;
        DateTime[] tmFileCreateTime = new DateTime[5000];
        int iFileCount = 0;

        public DateTime g_dtTimeIn = new DateTime();
        public DateTime g_dtTimeIn_UTC = new DateTime();
        
        EventRecorder WSPEvent = new EventRecorder();

        DataTable dtIISLog = new DataTable();
        DataTable dtServiceStatus = new DataTable();
        DataTable dtRequestStatus = new DataTable();
        DataTable dtLogFileList = new DataTable();

        // Run once when this agent starts
        private void InitializeTables()
        {
            //if (dtAnalyzeHistory.Rows.Count > 0)
            //    dtAnalyzeHistory.Rows.Clear();

            //if (dtAnalyzeHistory.Columns.Count > 0)
            //    dtAnalyzeHistory.Columns.Clear();

            //dtAnalyzeHistory.Columns.Add(new DataColumn("TimeInLog", typeof(DateTime)));            // strLogTimeFrom 

            if (dtRequestStatus.Rows.Count > 0)
                dtRequestStatus.Rows.Clear();

            if (dtRequestStatus.Columns.Count > 0)
                dtRequestStatus.Columns.Clear();

            dtRequestStatus.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
            dtRequestStatus.Columns.Add(new DataColumn("ServerNumber", typeof(Int32)));
            dtRequestStatus.Columns.Add(new DataColumn("HostHeader", typeof(string)));
            dtRequestStatus.Columns.Add(new DataColumn("SiteName", typeof(string)));            
            dtRequestStatus.Columns.Add(new DataColumn("ValueDescription", typeof(string)));
            dtRequestStatus.Columns.Add(new DataColumn("TotalNumber", typeof(double)));
            dtRequestStatus.Columns.Add(new DataColumn("LogValue", typeof(string)));            

            if (dtServiceStatus.Rows.Count > 0)
                dtServiceStatus.Rows.Clear();

            if (dtServiceStatus.Columns.Count > 0)
                dtServiceStatus.Columns.Clear();

            dtServiceStatus.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
            dtServiceStatus.Columns.Add(new DataColumn("ServerNumber", typeof(Int32)));
            dtServiceStatus.Columns.Add(new DataColumn("HostHeader", typeof(string)));
            dtServiceStatus.Columns.Add(new DataColumn("SiteName", typeof(string)));            
            dtServiceStatus.Columns.Add(new DataColumn("TotalHits", typeof(int)));
            dtServiceStatus.Columns.Add(new DataColumn("TotalSCBytes", typeof(double)));
            dtServiceStatus.Columns.Add(new DataColumn("TotalCSBytes", typeof(double)));
            dtServiceStatus.Columns.Add(new DataColumn("TotalCIP", typeof(int)));
            dtServiceStatus.Columns.Add(new DataColumn("TotalErrors", typeof(int)));
            dtServiceStatus.Columns.Add(new DataColumn("AnalyzedLogTime", typeof(string)));

            if (dtIISLog.Rows.Count > 0)
                dtIISLog.Rows.Clear();

            if (dtIISLog.Columns.Count > 0)
                dtIISLog.Columns.Clear();

            dtIISLog.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
            dtIISLog.Columns.Add(new DataColumn("ServerNumber", typeof(Int32)));
            dtIISLog.Columns.Add(new DataColumn("SiteName", typeof(string)));
            dtIISLog.Columns.Add(new DataColumn("HostHeader", typeof(string)));                        
            dtIISLog.Columns.Add(new DataColumn("URI", typeof(string)));
            dtIISLog.Columns.Add(new DataColumn("Hits", typeof(int)));
            dtIISLog.Columns.Add(new DataColumn("MaxTimeTaken", typeof(int)));
            dtIISLog.Columns.Add(new DataColumn("AvgTimeTaken", typeof(int)));
            dtIISLog.Columns.Add(new DataColumn("TotalSCBytes", typeof(double)));
            dtIISLog.Columns.Add(new DataColumn("TotalCSBytes", typeof(double)));
            dtIISLog.Columns.Add(new DataColumn("StatusCode", typeof(int)));
            dtIISLog.Columns.Add(new DataColumn("Win32StatusCode", typeof(Int64)));

            if (dtLogFileList.Columns.Count > 0)
                dtLogFileList.Columns.Clear();

            dtLogFileList.Columns.Add(new DataColumn("FilePath", typeof(string)));
            dtLogFileList.Columns.Add(new DataColumn("CreationTime", typeof(DateTime)));
        }

        public bool BuildAnalsysIISLog()
        {
            //strWinTempPath = "c:\\temp\\logs";
            strWinTempPath = "c:\\Windows\\Temp\\sp_iislogs";

            try
            {
                if (!System.IO.Directory.Exists(strWinTempPath))
                    System.IO.Directory.CreateDirectory(strWinTempPath);

                if (dtIISLog.Columns.Count < 1)
                    InitializeTables();

                IsTurnOverBySize();

                if (GetLogSettingsToAnalyze())
                {
                    if (CheckIfAnalyzied())       //check if data in this hour was analyzed already.
                    {
                        return false;
                    }
                }
                else
                    return false;                    // log files, or logparser doesn't exist.

            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "E", 1111);
            }

            string strPath = g_SharedData.WEB_SETTING.strLogFilesDirectory + "\\" + strIISLogFileName;
            string strFields = "date,time,c-ip,s-sitename,cs-uri-stem,sc-status,sc-win32-status,sc-bytes,cs-bytes,time-taken";
            string strQuery = " \"select " + strFields + " from '" + strPath + "' to " + strWinTempPath + "\\shst.iis.log where date = '" + strLogTimeDate + "' AND time >= '" + strLogTimeFrom + "' AND time <= '" + strLogTimeTo + "'\"";

            if(bIsFileBySize)
                strQuery = " \"select " + strFields + " from '" + strIISLogFileName + "' to " + strWinTempPath + "\\shst.iis.log " + "\"";

            try
            {
                string strCommand = LaunchEXE.Run(logparser_path, strQuery + " -o:W3C -recurse 3", 3600);
                Thread.Sleep(10000);
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "W", 1111);
            }

            if (File.Exists(strWinTempPath + "\\shst.iis.log"))
                return true;

            return false;
        }

        private bool CheckIfAnalyzied()
        {
            if (bIsFileBySize)
            {
                if (strLastAnalyzedFile == strLastLogFile)
                {
                    //TestLogFiles("Turnover_by_size : Skip this turn, " + strLastAnalyzedFile);
                    return true;
                }
                else
                {
                    //TestLogFiles("Turnover_by_size : start analyze, " + strLastAnalyzedFile);
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(strSavedLogTime))
            {
                if (strSavedLogTime == strAnalyzedLogTime)
                {
                    //TestLogFiles("Skip analysis, " + strSavedLogTime);
                    return true;
                }
            }
            //TestLogFiles("Start analysis, " + strSavedLogTime);
            return false;
        }

        private bool IsTurnOverBySize()
        {
            iFileCount = 0;
            string dir = g_SharedData.WEB_SETTING.strLogFilesDirectory;
            string pattern = "u_extend";
            DateTime tmLastLogTime = new DateTime();

            if(dtLogFileList.Rows.Count > 0)
                dtLogFileList.Clear();

            FindFiles(dir, pattern);

            if (iFileCount > 0)
            {
                DateTime[] astrLocalList = new DateTime[iFileCount];
                for (int i = 0; i < iFileCount; i++)
                    astrLocalList[i] = tmFileCreateTime[i];

                Array.Sort(astrLocalList, StringComparer.Ordinal);

                int index = 0;
                foreach (DateTime tmRecord in astrLocalList)
                    index++;

                if (index > 0)      // if there's completed log file by size
                {
                    int iSeq = index - 1;       // get second last value (file name)
                    index = 0;

                    foreach (DateTime tmRecord in astrLocalList)            // get second last value of file creation time.
                    {
                        index++;
                        if (index == iSeq)
                            tmLastLogTime = tmRecord;                             
                    }

                    foreach (DataRow dr in dtLogFileList.Rows)              // get the file path of the creation time.
                    {
                        if (dr["CreationTime"].ToString() == tmLastLogTime.ToString())
                            strLastLogFile = dr["FilePath"].ToString();
                    }
                }
                else
                    return false;

                bIsFileBySize = true;

                return true;
            }
            return false;
        }

        private bool FindFiles(string path, string pattern)
        {
            DateTime tmCreationTime = new DateTime();

            foreach (string file in Directory.GetFiles(path))
            {
                if (file.Contains(pattern))
                {
                    tmCreationTime = Directory.GetCreationTime(file);
                    dtLogFileList.Rows.Add(file, tmCreationTime);
                    tmFileCreateTime[iFileCount] = tmCreationTime;

                    iFileCount++;
                    if (iFileCount >= 5000)
                    {
                        WSPEvent.WriteEvent("There're too many log files in IIS log folder, more than 5000 files.", "W", 1163);
                        break;
                    }
                }
            }
            foreach (string directory in Directory.GetDirectories(path))
            {
                FindFiles(directory, pattern);
            }
            return false;
        }

        private bool GetLogSettingsToAnalyze()
        {
            // File name format : ex05122409.log, or u_ex120921.log
            string path = "C:\\Program Files (x86)\\Log Parser 2.2\\LogParser.exe";

            if (!File.Exists(path))
            {
                WSPEvent.WriteEvent("Log Parsing tool is not installed", "E", 1112);
                return false;
            }

            if (bIsFileBySize)
            {
                strIISLogFileName = strLastLogFile;
                return true;
            }

            string strFileName;
            string strFileHeader;

            DateTime tmTarget = DateTime.Now.Subtract(TimeSpan.FromHours(1));
            tmTarget = tmTarget.ToUniversalTime();

            string strDate = "yyMMdd";
            string strHour = "yyMMddHH";

            strLogTimeFrom = tmTarget.ToString("HH") + ":00:00";
            strLogTimeTo = tmTarget.ToString("HH") + ":59:59";
            strLogTimeDate = tmTarget.ToString("yyyy-MM-dd");

            strAnalyzedLogTime = strLogTimeDate + " " + strLogTimeFrom;
            strDate = tmTarget.ToString("yyMMdd");
            strHour = tmTarget.ToString("HH");

            if (g_SharedData.SYSINFO.iWinVer >= 2008)
                strFileHeader = "u_ex";
            else
                strFileHeader = "ex";

            strFileName = strFileHeader + strDate + ".log";

            string[] strtemp = Directory.GetFiles(g_SharedData.WEB_SETTING.strLogFilesDirectory, strFileName, SearchOption.AllDirectories);

            int i = strtemp.Length;

            if (i > 0)
            {
                strIISLogFileName = strFileHeader + strDate + "*.log";
                return true;
            }
            else
            {
                strFileName = strFileHeader + strDate + strHour + ".log";
                string[] strtemp2 = Directory.GetFiles(g_SharedData.WEB_SETTING.strLogFilesDirectory, strFileName, SearchOption.AllDirectories);

                int i2 = strtemp.Length;

                if (i2 > 0)
                {
                    strIISLogFileName = strFileHeader + strDate + strHour + "*.log"; 
                    return true;
                }
            }

            return false;
        }

        public bool AnalyzeIISLogCommands()
        {

            bool bReturn = false;
            try
            {
                bReturn = AnalyzeIISLog();
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "W", 1112);
                return false;
            }

            return bReturn;

        }

        private bool AnalyzeIISLog()
        {
            
            string strPath = g_SharedData.WEB_SETTING.strLogFilesDirectory + "\\" + strIISLogFileName;
            
            // Creating temp log file
            string strQuery = "select cs-uri-stem, count(cs-uri-stem) as Hits, max(time-taken) as MaxTaken, avg(TO_INT(time-taken)) as AvgTaken, sum(TO_INT(sc-bytes)) as TotalSCBytes, sum(TO_INT(cs-bytes)) as TotalCSBytes from " + strWinTempPath + "\\shst.iis.log to " + strWinTempPath + "\\shst.temp.csv GROUP BY cs-uri-stem ORDER BY AvgTaken DESC";
            string strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:W3C -o:CSV", 3600);
            Thread.Sleep(1000);

            string path = strWinTempPath + "\\shst.temp.csv";
            if (!File.Exists(path))
            {
                WSPEvent.WriteEvent("Generating temperary file from IIS log has been failed.", "W", 1115);
                return false;
            }

            strQuery = "select top 20 cs-uri-stem, Hits from " + strWinTempPath + "\\shst.temp.csv to " + strWinTempPath + "\\top20.hits.csv ORDER BY Hits DESC";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:CSV -o:CSV", 3600);
            Thread.Sleep(1000);

            strQuery = "select top 20 cs-uri-stem, Hits from " + strWinTempPath + "\\shst.temp.csv to " + strWinTempPath + "\\top20.apphits.csv where cs-uri-stem like '%.asmx' or cs-uri-stem like '%.asp' or cs-uri-stem like '%.aspx' or cs-uri-stem like '%.jsp' or cs-uri-stem like '%.php' ORDER BY Hits DESC";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:CSV -o:CSV", 3600);
            Thread.Sleep(1000);

            strQuery = "select top 20 cs-uri-stem, Hits, MaxTaken, AvgTaken from " + strWinTempPath + "\\shst.temp.csv to " + strWinTempPath + "\\top20.loggest.csv ORDER BY AvgTaken DESC";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:CSV -o:CSV", 3600);
            Thread.Sleep(1000);


            strQuery = "select top 20 cs-uri-stem, Hits, TotalSCBytes from " + strWinTempPath + "\\shst.temp.csv to " + strWinTempPath + "\\top20.scbytes.csv ORDER BY TotalSCBytes DESC";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:CSV -o:CSV", 3600);
            Thread.Sleep(1000);

            strQuery = "select top 20 cs-uri-stem, Hits, TotalCSBytes from " + strWinTempPath + "\\shst.temp.csv to " + strWinTempPath + "\\top20.csbytes.csv ORDER BY TotalCSBytes DESC";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:CSV -o:CSV", 3600);
            Thread.Sleep(1000);


            strQuery = "select top 20 cs-uri-stem, count(cs-uri-stem) as ErrCount, sc-status, sc-win32-status from " + strWinTempPath + "\\shst.iis.log to " + strWinTempPath + "\\top20.errors.csv GROUP BY cs-uri-stem, sc-status,sc-win32-status ORDER BY ErrCount DESC";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:W3C -o:CSV", 3600);
            Thread.Sleep(1000);

            strQuery = "select top 20 c-ip, count(c-ip) as IP_Total from " + strWinTempPath + "\\shst.iis.log to " + strWinTempPath + "\\top20.ip.csv GROUP BY c-ip ORDER BY IP_Total DESC";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:W3C -o:CSV", 3600);
            Thread.Sleep(1000);


            strQuery = "select top 20 sc-status, count(status) as Status_Total from " + strWinTempPath + "\\shst.iis.log to " + strWinTempPath + "\\top20.status.csv GROUP BY sc-status ORDER BY Status_Total DESC";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:W3C -o:CSV", 3600);
            Thread.Sleep(1000);

            strQuery = "select top 20 EXTRACT_EXTENSION(cs-uri-stem) AS Extension, sum(TotalSCBytes) as Bytes_Sum from " + strWinTempPath + "\\shst.temp.csv to " + strWinTempPath + "\\top20.ext.csv GROUP BY Extension ORDER BY Bytes_Sum DESC";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:CSV -o:CSV", 3600);
            Thread.Sleep(1000);

            //added, service status
            strQuery = "select count(*) AS Hits, sum(TO_REAL(sc-bytes)) AS SCBytes, sum(TO_REAL(cs-bytes)) AS CSBytes from " + strWinTempPath + "\\shst.iis.log to " + strWinTempPath + "\\svc.total.csv";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:W3C -o:CSV", 3600);
            Thread.Sleep(1000);

            strQuery = "select count(distinct c-ip) AS ClientIPs from " + strWinTempPath + "\\shst.iis.log to " + strWinTempPath + "\\svc.ips.csv";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:W3C -o:CSV", 3600);
            Thread.Sleep(1000);

            strQuery = "select count(*) AS Errors from " + strWinTempPath + "\\shst.iis.log to " + strWinTempPath + "\\svc.errors.csv where sc-status >= 400";
            strCommand = LaunchEXE.Run(logparser_path, "\"" + strQuery + "\" -i:W3C -o:CSV", 3600);
            Thread.Sleep(1000);

            strSavedLogTime = strAnalyzedLogTime;      //record history for this log analysis

            return true;
        }

        public void LoadAnalysisFilesToDataTable()
        {
            
            string path;
            g_dtTimeIn = DateTime.Now;
            g_dtTimeIn_UTC = DateTime.UtcNow;

            string strValue;
            bool bHasTimeTaken = false;

            path = strWinTempPath + "\\top20.ext.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                string strExtension;
                double dblSCBytes;

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strExtension = dr1["Extension"].ToString();

                    strValue = dr1["Bytes_Sum"].ToString();
                    if (strValue.Length > 0)
                        dblSCBytes = Convert.ToDouble(strValue);
                    else
                        dblSCBytes = 0;
                    
                    dtRequestStatus.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, g_SharedData.WEB_SETTING.strHostHeader, "", "TOP 20 Bytes per Extension", dblSCBytes, strExtension);
                }
            }

            path = strWinTempPath + "\\top20.status.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                string strStatusCode;
                double dblSCTotal;

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strStatusCode = dr1["sc-status"].ToString();                    

                    strValue = dr1["Status_Total"].ToString();
                    if (strValue.Length > 0)
                        dblSCTotal = Convert.ToDouble(strValue);
                    else
                        dblSCTotal = 0;

                    dtRequestStatus.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, g_SharedData.WEB_SETTING.strHostHeader, "", "TOP 20 Status Codes", dblSCTotal, strStatusCode);
                }
            }

            path = strWinTempPath + "\\top20.ip.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                string strIP;
                double dblIPTotal;

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strIP = dr1["c-ip"].ToString();
                    
                    strValue = dr1["IP_Total"].ToString();
                    if (strValue.Length > 0)
                        dblIPTotal = Convert.ToDouble(strValue);
                    else
                        dblIPTotal = 0;

                    dtRequestStatus.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, g_SharedData.WEB_SETTING.strHostHeader, "", "TOP 20 Clients IP", dblIPTotal, strIP);
                }
            }
            //top20.apphits.csv, top20.hits.csv

            path = strWinTempPath + "\\top20.hits.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                string strURL;
                double dblHits;

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strURL = dr1["cs-uri-stem"].ToString();

                    strValue = dr1["Hits"].ToString();
                    if (strValue.Length > 0)
                        dblHits = Convert.ToDouble(strValue);
                    else
                        dblHits = 0;

                    dtRequestStatus.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, g_SharedData.WEB_SETTING.strHostHeader, "", "TOP 20 Hits", dblHits, strURL);
                }
            }

            path = strWinTempPath + "\\top20.apphits.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                string strApps;
                double dblAppHits;

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strApps = dr1["cs-uri-stem"].ToString();

                    strValue = dr1["Hits"].ToString();
                    if (strValue.Length > 0)
                        dblAppHits = Convert.ToDouble(strValue);
                    else
                        dblAppHits = 0;

                    dtRequestStatus.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, g_SharedData.WEB_SETTING.strHostHeader, "", "TOP 20 Application Hits", dblAppHits, strApps);
                }
            }


            path = strWinTempPath + "\\top20.errors.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                string strURI;
                int iErrCount;
                string strSCode;
                string strWin32Code;

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strURI = dr1["cs-uri-stem"].ToString();
                    strSCode = dr1["sc-status"].ToString();
                    strWin32Code = dr1["sc-win32-status"].ToString();

                    strValue = dr1["ErrCount"].ToString();
                    if (strValue.Length > 0)
                        iErrCount = Convert.ToInt32(strValue);
                    else
                        iErrCount = 0;
                    
                    if (strURI.Length >= 128)
                        strURI = strURI.Substring(0, 126);

                    dtIISLog.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, "", g_SharedData.WEB_SETTING.strHostHeader, strURI, iErrCount, 0, 0, 0, 0, Convert.ToInt32(strSCode), strWin32Code);
                }

            }

            path = strWinTempPath + "\\top20.csbytes.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                string strURI;
                int iHits;
                double dblCSBytes;

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strURI = dr1["cs-uri-stem"].ToString();

                    strValue = dr1["Hits"].ToString();
                    if (strValue.Length > 0)
                        iHits = Convert.ToInt32(strValue);
                    else
                        iHits = 0;

                    strValue = dr1["TotalCSBytes"].ToString();
                    if (strValue.Length > 0)
                        dblCSBytes = Convert.ToDouble(strValue);
                    else
                        dblCSBytes = 0;

                    dtIISLog.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, "", g_SharedData.WEB_SETTING.strHostHeader, strURI, iHits, 0, 0, 0, dblCSBytes, 0, 0);
                }

            }

            path = strWinTempPath + "\\top20.scbytes.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                string strURI;
                int iHits;
                double dblSCBytes;

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strURI = dr1["cs-uri-stem"].ToString();

                    strValue = dr1["TotalSCBytes"].ToString();
                    if (strValue.Length > 0)
                        dblSCBytes = Convert.ToDouble(strValue);
                    else
                        dblSCBytes = 0;

                    strValue = dr1["Hits"].ToString();
                    if (strValue.Length > 0)
                        iHits = Convert.ToInt32(strValue);
                    else
                        iHits = 0;

                    dtIISLog.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, "", g_SharedData.WEB_SETTING.strHostHeader, strURI, iHits, 0, 0, dblSCBytes, 0, 0, 0);
                }
            }

            path = strWinTempPath + "\\top20.loggest.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                string strURI;
                int iHits;
                int iAvgTimeTaken;
                int iMaxTimeTaken;

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strURI = dr1["cs-uri-stem"].ToString();

                    strValue = dr1["Hits"].ToString();
                    if (strValue.Length > 0)
                        iHits = Convert.ToInt32(strValue);
                    else
                        iHits = 0;

                    strValue = dr1["MaxTaken"].ToString();
                    if (strValue.Length > 0)
                        iMaxTimeTaken = Convert.ToInt32(strValue);
                    else
                        iMaxTimeTaken = 0;

                    strValue = dr1["AvgTaken"].ToString();
                    if (strValue.Length > 0)
                        iAvgTimeTaken = Convert.ToInt32(strValue);
                    else
                        iAvgTimeTaken = 0;

                    dtIISLog.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, "",g_SharedData.WEB_SETTING.strHostHeader, strURI, iHits, iMaxTimeTaken, iAvgTimeTaken, 0, 0, 0, 0);

                    if (iMaxTimeTaken > 0)
                        bHasTimeTaken = true;
                    else
                        bHasTimeTaken = false;

                }
            }

            int iSVCHits = 0;
            double dblSVCSCBytes = 0;
            double dblSVCCSBytes = 0;
            int iSVCIPs = 0;
            int iSVCErrors = 0;

            path = strWinTempPath + "\\svc.total.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strValue = dr1["Hits"].ToString();
                    if (strValue.Length > 0)
                        iSVCHits = Convert.ToInt32(strValue);
                    else
                        iSVCHits = 0;

                    strValue = dr1["SCBytes"].ToString();
                    if (strValue.Length > 0)
                        dblSVCSCBytes = Convert.ToDouble(strValue);
                    else
                        dblSVCSCBytes = 0;

                    strValue = dr1["CSBytes"].ToString();
                    if (strValue.Length > 0)
                        dblSVCCSBytes = Convert.ToDouble(strValue);
                    else
                        dblSVCCSBytes = 0;
                }
            }

            path = strWinTempPath + "\\svc.ips.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strValue = dr1["ClientIPs"].ToString();
                    if (strValue.Length > 0)
                        iSVCIPs = Convert.ToInt32(strValue);
                    else
                        iSVCIPs = 0;
                }
            }

            path = strWinTempPath + "\\svc.errors.csv";
            if (File.Exists(path))
            {
                DataTable dtCSV = new DataTable();
                dtCSV = LoadCSVIntoTable(path);

                foreach (DataRow dr1 in dtCSV.Rows)
                {
                    strValue = dr1["Errors"].ToString();
                    if (strValue.Length > 0)
                        iSVCErrors = Convert.ToInt32(strValue);
                    else
                        iSVCErrors = 0;
                }
            }

            dtServiceStatus.Rows.Add(g_dtTimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, g_SharedData.WEB_SETTING.strHostHeader, "", iSVCHits, dblSVCSCBytes, dblSVCCSBytes, iSVCIPs, iSVCErrors, strAnalyzedLogTime);
            if (dblSVCCSBytes == 0 || dblSVCSCBytes == 0 || iSVCIPs == 0 || !bHasTimeTaken)
            {
                WSPEvent.WriteEvent("IIS Server is missing one of required logging fields.", "W", 1120);
            }
        }

        private DataTable LoadCSVIntoTable(string strCSVFileName)
        {
            StreamReader oStreamReader = new StreamReader(strCSVFileName);

            DataTable oDataTable = null;
            int RowCount = 0;
            string[] ColumnNames = null;
            string[] oStreamDataValues = null;

            //using while loop read the stream data till end

            while (!oStreamReader.EndOfStream)
            {
                String oStreamRowData = oStreamReader.ReadLine().Trim();

                if (oStreamRowData.Length > 0)
                {
                    oStreamDataValues = oStreamRowData.Split(',');

                    if (RowCount == 0)
                    {
                        RowCount = 1;
                        ColumnNames = oStreamRowData.Split(',');
                        oDataTable = new DataTable();

                        //using foreach looping through all the column names 

                        foreach (string csvcolumn in ColumnNames)
                        {
                            DataColumn oDataColumn = new DataColumn(csvcolumn.ToUpper(), typeof(string));
                            oDataColumn.DefaultValue = string.Empty;
                            oDataTable.Columns.Add(oDataColumn);
                        }
                    }
                    else
                    {
                        //creates a new DataRow with the same schema as of the oDataTable             
                        DataRow oDataRow = oDataTable.NewRow();

                        //using foreach looping through all the column names

                        for (int i = 0; i < ColumnNames.Length; i++)
                        {
                            oDataRow[ColumnNames[i]] = oStreamDataValues[i] == null ? string.Empty : oStreamDataValues[i].ToString();
                        }

                        //adding the newly created row with data to the oDataTable        
                        oDataTable.Rows.Add(oDataRow);
                    }
                }
            }

            //close the oStreamReader object

            oStreamReader.Close();

            //release all the resources used by the oStreamReader object
            oStreamReader.Dispose();

            return oDataTable;

        }

        private void SaveAnalyzedLogFileName()
        {
            
            var dir = AppDomain.CurrentDomain.BaseDirectory;

            string strPath = dir.ToString();

            if (strPath.Substring(strPath.Length - 1).Contains("\\"))
                strPath = strPath + @"ServicePoint.Settings.xml";
            else
                strPath = strPath + @"\\ServicePoint.Settings.xml";
    
            try
            {
                XmlDocument xml = new XmlDocument();
                xml.Load(@strPath);
                xml.SelectSingleNode("ServicePoint/WEB/LastLogFile").InnerText = strLastAnalyzedFile;
                xml.Save(strPath);
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Service Point Agent failed to save settings to " + strPath + ex.Message, "I", 1162);
            }
        }

        public void TurnOffIISLogAnalysisOption()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;

            string strPath = dir.ToString();

            if (strPath.Substring(strPath.Length - 1).Contains("\\"))
                strPath = strPath + @"ServicePoint.Settings.xml";
            else
                strPath = strPath + @"\\ServicePoint.Settings.xml";            

            try
            {
                XmlDocument xml = new XmlDocument();
                xml.Load(@strPath);
                xml.SelectSingleNode("ServicePoint/WEB/IISLogAnalysis").InnerText = "FALSE";
                xml.Save(strPath);
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Service Point Agent failed to save settings to " + strPath + ex.Message, "I", 1162);
            }

        }

        public void InsertAllToDB()
        {
            
            InsertIISLogToDB();
            InsertRequestStatusToDB();
            InsertServiceStatusToDB();

            if (bIsFileBySize)
            {
                strLastAnalyzedFile = strLastLogFile;
                SaveAnalyzedLogFileName();
            }

            //WSPEvent.WriteEvent("IIS Log Analysis has been processed, for 1 Hour from " + strAnalyzedLogTime, "I", 1005);

            dtIISLog.Rows.Clear();
            dtServiceStatus.Rows.Clear();
            dtRequestStatus.Rows.Clear();

            try
            {
                if (File.Exists(strWinTempPath + "\\shst.iis.log"))
                    File.Delete(strWinTempPath + "\\shst.iis.log");

                if(File.Exists(strWinTempPath + "\\shst.temp.csv"))
                    File.Delete(strWinTempPath + "\\shst.temp.csv");

                if (File.Exists(strWinTempPath + "\\svc.errors.csv"))
                    File.Delete(strWinTempPath + "\\svc.errors.csv");

                if (File.Exists(strWinTempPath + "\\svc.ips.csv"))
                    File.Delete(strWinTempPath + "\\svc.ips.csv");

                if (File.Exists(strWinTempPath + "\\svc.total.csv"))
                    File.Delete(strWinTempPath + "\\svc.total.csv");

                if (File.Exists(strWinTempPath + "\\ top20.apphits.csv"))
                    File.Delete(strWinTempPath + "\\ top20.apphits.csv");

                if (File.Exists(strWinTempPath + "\\top20.csbytes.csv"))
                    File.Delete(strWinTempPath + "\\top20.csbytes.csv");

                if (File.Exists(strWinTempPath + "\\top20.errors.csv"))
                    File.Delete(strWinTempPath + "\\top20.errors.csv");

                if (File.Exists(strWinTempPath + "\\top20.ext.csv"))
                    File.Delete(strWinTempPath + "\\top20.ext.csv");

                if (File.Exists(strWinTempPath + "\\top20.hits.csv"))
                    File.Delete(strWinTempPath + "\\top20.hits.csv");

                if (File.Exists(strWinTempPath + "\\top20.ip.csv"))
                    File.Delete(strWinTempPath + "\\top20.ip.csv");

                if (File.Exists(strWinTempPath + "\\top20.loggest.csv"))
                    File.Delete(strWinTempPath + "\\top20.loggest.csv");

                if (File.Exists(strWinTempPath + "\\top20.scbytes.csv"))
                    File.Delete(strWinTempPath + "\\top20.scbytes.csv");

            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "W", 1113);
            }

        }


        private void InsertIISLogToDB()
        {

            WSP.Console.WS_DBOP.GeneralDBOp wsSend = new WSP.Console.WS_DBOP.GeneralDBOp();
            wsSend.Timeout = 10000;
            wsSend.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/GeneralDBOp.asmx";
            DataTable dtParams = SetParmeterTable();
            int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

            dtParams.Rows.Add(iServerNumber, "TimeIn", "DATETIME", "");
            dtParams.Rows.Add(iServerNumber, "ServerNum", "INT", "");
            dtParams.Rows.Add(iServerNumber, "SiteName", "STRING", "");
            dtParams.Rows.Add(iServerNumber, "HostHeader", "STRING", "");
            dtParams.Rows.Add(iServerNumber, "URI", "STRING", "");
            dtParams.Rows.Add(iServerNumber, "Hits", "INT", "");
            dtParams.Rows.Add(iServerNumber, "MaxTimeTaken", "INT", "");
            dtParams.Rows.Add(iServerNumber, "AvgTimeTaken", "INT", "");
            dtParams.Rows.Add(iServerNumber, "SCBytes", "FLOAT", "");
            dtParams.Rows.Add(iServerNumber, "CSBytes", "FLOAT", "");
            dtParams.Rows.Add(iServerNumber, "StatusCode", "INT", "");
            dtParams.Rows.Add(iServerNumber, "Win32StatusCode", "BIGINT", "");

            try
            {
                wsSend.IISLogInsert(DataTableToBytes(dtIISLog), iServerNumber, g_dtTimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_dtTimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Sending IIS Log information has been failed. - " + ex.Message, "W", 1114);
            }

        }


        private void InsertServiceStatusToDB()
        {

            WSP.Console.WS_DBOP.GeneralDBOp wsSend = new WSP.Console.WS_DBOP.GeneralDBOp();
            wsSend.Timeout = 10000;
            wsSend.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/GeneralDBOp.asmx";
            DataTable dtParams = SetParmeterTable();
            int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

            dtParams.Rows.Add(iServerNumber, "TimeIn", "DATETIME", "");          
            dtParams.Rows.Add(iServerNumber, "ServerNum", "INT", "");              
            dtParams.Rows.Add(iServerNumber, "HostHeader", "STRING", "");
            dtParams.Rows.Add(iServerNumber, "SiteName", "STRING", "");
            dtParams.Rows.Add(iServerNumber, "TotalHits", "INT", "");
            dtParams.Rows.Add(iServerNumber, "TotalSCBytes", "FLOAT", "");
            dtParams.Rows.Add(iServerNumber, "TotalCSBytes", "FLOAT", "");
            dtParams.Rows.Add(iServerNumber, "TotalCIP", "INT", "");
            dtParams.Rows.Add(iServerNumber, "TotalErrors", "INT", "");
            dtParams.Rows.Add(iServerNumber, "AnalyzedLogTime", "STRING", "");

            try
            {
                //wsSend.InsertSPValues("p_tbServiceStatus_Add", DataTableToBytes(dtParams), DataTableToBytes(dtServiceStatus));
                wsSend.ServiceStatusInsert(DataTableToBytes(dtServiceStatus), iServerNumber, g_dtTimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_dtTimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Sending IIS service status information has been failed. - " + ex.Message, "W", 1114);
                
            }

        }


        private void InsertRequestStatusToDB()
        {

            WSP.Console.WS_DBOP.GeneralDBOp wsSend = new WSP.Console.WS_DBOP.GeneralDBOp();
            wsSend.Timeout = 10000;
            wsSend.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/GeneralDBOp.asmx";
            DataTable dtParams = SetParmeterTable();
            int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

            dtParams.Rows.Add(iServerNumber, "TimeIn", "DATETIME", "");
            dtParams.Rows.Add(iServerNumber, "ServerNum", "INT", "");
            dtParams.Rows.Add(iServerNumber, "HostHeader", "STRING", "");
            dtParams.Rows.Add(iServerNumber, "SiteName", "STRING", "");
            dtParams.Rows.Add(iServerNumber, "ValueDescription", "STRING", "");
            dtParams.Rows.Add(iServerNumber, "TotalNumber", "FLOAT", "");
            dtParams.Rows.Add(iServerNumber, "LogValue", "STRING", "");            

            try
            {
                //wsSend.InsertSPValues("p_tbRequestStatus_Add", DataTableToBytes(dtParams), DataTableToBytes(dtRequestStatus));
                wsSend.RequestStatusInsert(DataTableToBytes(dtRequestStatus), iServerNumber, g_dtTimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_dtTimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Sending IIS requests information has been failed. - " + ex.Message, "W", 1114);
            }

        }

        private DataTable SetParmeterTable()
        {
            DataTable dtParms = new DataTable();

            dtParms.Columns.Add(new DataColumn("SERVER_NUMBER", typeof(int)));
            dtParms.Columns.Add(new DataColumn("PARM_NAME", typeof(string)));
            dtParms.Columns.Add(new DataColumn("DATA_TYPE", typeof(string)));
            dtParms.Columns.Add(new DataColumn("DATA_VALUE", typeof(string)));

            return dtParms;
        }


        private DataTable BytesToDataTable(Byte[] byteArray)
        {
            DataTable dtResult = new DataTable();

            BinaryFormatter bformatter = new BinaryFormatter();
            MemoryStream stream = new MemoryStream();

            try
            {
                if (byteArray != null)
                {
                    bformatter = new BinaryFormatter();
                    stream = new MemoryStream(byteArray);
                    dtResult = (DataTable)bformatter.Deserialize(stream);
                }
                else
                    dtResult = null;
            }
            catch (Exception ex)
            {
                return null;
            }

            return dtResult;
        }

        private Byte[] DataTableToBytes(DataTable dtInput)
        {
            BinaryFormatter bformatter = new BinaryFormatter();
            MemoryStream stream = new MemoryStream();

            try
            {
                if (dtInput != null)
                {
                    bformatter.Serialize(stream, dtInput);
                    byte[] b = stream.ToArray();
                    stream.Close();
                    return b;
                }
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }

        }

      
    }
}
