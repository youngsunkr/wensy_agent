using System;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml;
using System.Data;
using System.Threading;
using System.IO;
using Newtonsoft.Json;

class PerfReader
{
    #region Declaration

    const int PCID_REFRESH_INTERVAL = 180;

    public DataTable dtPCID = new DataTable();
    public DataTable dtCategory = new DataTable();
    public DataTable dtCounters = new DataTable();
    //public DataTable dtWPList = new DataTable();
    public DataTable dtNI = new DataTable();
    //public DataTable dtSiteList = new DataTable();
    public DataTable dtInstanceList = new DataTable();
    public DataTable dtW3WPMemory = new DataTable();
    public DataTable g_dtSharedPValues = new DataTable();
    public SharedData g_SharedData = new SharedData();

    EventRecorder WSPEvent = new EventRecorder();

    Thread[] PerfReaderThreads = new Thread[100];
    Thread ObserverThread;

    private volatile bool _bPerfReaderThreadFlag = true;
    private volatile bool[] _bReaderTimerFlag = new bool[100];
    private volatile int iThreadID = 0;
    private volatile string g_strGUIDperTurn;
    public Guid g_guidPerfCollectingTurn = new Guid();
    private string g_strSavedGUID;
    public DateTime g_dtmNow = new DateTime();
    public DateTime g_dtTimeIn_UTC = new DateTime();
    
    private string strBatchReqPerSecond;

    private struct stShardPValues
    {
        public string strPCID;
        public float flPValue;
        public string strRValue;
        public string strInstanceName;
        public string strCategoryName;
        public string strCounterName;
    }

    // 50 - MAX THREAD, 100 - Max Perf Items per thread
    stShardPValues[,] stSharedPValueArray = new stShardPValues[50,500];
    private volatile int[] g_iItemsPerThread = new int[50];

    #endregion


    private void ReadPerformanceCategory()   // To compare Performance Object Names in DB with local objects (ASP.NET Versions).
    {

        //if (!g_SharedData.LOCAL_SQL_SETTING.bUseDefaultInstance && g_SharedData.LOCAL_SQL_SETTING.strCustomInstanceName.Length > 0)
        if (g_SharedData.LOCAL_SQL_SETTING.strInstanceName.Length > 0)
        {
            string strPObjectName = "";
            int index = 0;

            for (int i = 0; i < dtPCID.Rows.Count; i++)
            {
                strPObjectName = dtPCID.Rows[i]["PObjectName"].ToString();

                if (strPObjectName.Contains("SQLServer:"))
                {
                    dtPCID.Rows[i].BeginEdit();
                    index = strPObjectName.IndexOf(":", 0);
                    if (index > 0)
                        dtPCID.Rows[i]["PObjectName"] = "MSSQL$" + g_SharedData.LOCAL_SQL_SETTING.strInstanceName + strPObjectName.Substring(index);
                    dtPCID.Rows[i].EndEdit();
                    dtPCID.AcceptChanges();
                }
            }
        }

    }

    public void SetASPNETCategoryNames()
    {
        PerformanceCounterCategory[] arrCategories = PerformanceCounterCategory.GetCategories();
        //        DataTable dt1 = dtPCID.DefaultView.ToTable(true, "PObjectName");

        string strCategory;
        for (int index = 0; index < dtPCID.Rows.Count; index++)
        {
            strCategory = dtPCID.Rows[index]["PObjectName"].ToString();
            if (strCategory.Contains("ASP.NET Apps"))
            {
                dtPCID.Rows[index].BeginEdit();

                foreach (PerformanceCounterCategory pcy in arrCategories)
                {
                    if (pcy.CategoryName.Contains(strCategory))
                        dtPCID.Rows[index]["PObjectName"] = pcy.CategoryName;
                }

                dtPCID.Rows[index].EndEdit();
                dtPCID.AcceptChanges();
            }
        }

    }

    private void InitializeDataTables()
    {

        dtCounters.Columns.Clear();

        dtCounters.Columns.Add(new DataColumn("PObjectName", typeof(string)));
        dtCounters.Columns.Add(new DataColumn("PCounterName", typeof(string)));
        dtCounters.Columns.Add(new DataColumn("InstanceName", typeof(string)));
        dtCounters.Columns.Add(new DataColumn("HasInstance", typeof(bool)));
        dtCounters.Columns.Add(new DataColumn("PCID", typeof(string)));

        dtNI.Columns.Clear();

        dtNI.Columns.Add(new DataColumn("InstanceName", typeof(string)));
        dtNI.Columns.Add(new DataColumn("PValue", typeof(float)));

        dtW3WPMemory.Columns.Clear();
        dtW3WPMemory.Columns.Add(new DataColumn("PCID", typeof(string)));
        dtW3WPMemory.Columns.Add(new DataColumn("HOSTNAME", typeof(string)));
        dtW3WPMemory.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
        dtW3WPMemory.Columns.Add(new DataColumn("PValue", typeof(float)));
        dtW3WPMemory.Columns.Add(new DataColumn("RValue", typeof(string)));
        dtW3WPMemory.Columns.Add(new DataColumn("InstanceName", typeof(string)));

        SaveNetworkBandwidth();

    }

    private void SaveNetworkBandwidth()
    {
        string[] arrInstanceNames;

        dtNI.Rows.Clear();

        try
        {
            PerformanceCounterCategory pcCat = new PerformanceCounterCategory();

            if (PerformanceCounterCategory.Exists("Network Interface"))
            {
                pcCat.CategoryName = "Network Interface";
                arrInstanceNames = pcCat.GetInstanceNames();
                foreach (string strInterface in arrInstanceNames)
                {
                    PerformanceCounter PC = new PerformanceCounter();

                    PC.CategoryName = "Network Interface";
                    PC.CounterName = "Current bandwidth";
                    PC.InstanceName = strInterface;
                    PC.NextValue();

                    System.Threading.Thread.Sleep(50);

                    dtNI.Rows.Add(strInterface, PC.NextValue());
                }
            }
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent(ex.Message, "E", 1110);
        }
    }

    private string GetReferenceValue(string strCategory, string strCounterName, float flPValue, string strInstanceName, string strPCID)
    {
        string strReference = "";
        PerformanceCounter PC = new PerformanceCounter();
        float flRValue = 0;

        if (strCategory == "Processor")
            return g_SharedData.SYSINFO.iNumberOfProcessors.ToString();

        if (strCategory == "System")
        {
            if (strCounterName.Contains("Processor Queue"))
                return g_SharedData.SYSINFO.iNumberOfProcessors.ToString();
            else
                return (flPValue / g_SharedData.SYSINFO.iNumberOfProcessors).ToString();
        }

        if (strCategory == ".NET CLR Memory" || strCategory == "Process")
        {
            for (int i = 0; i < g_SharedData.WEBINFO.iNumberOfAppPools; i++)
            {
                if (strInstanceName == g_SharedData.arrWPList[i].strInstanceName)
                    return g_SharedData.arrWPList[i].strAppPoolDesc + "(" + g_SharedData.arrWPList[i].strPID + ")";
            }
        }

        if (strCategory == "Memory")
        {
            if (strCounterName.Contains("Available") || strCounterName.Contains("Committed"))
                return g_SharedData.SYSINFO.flRAMSize.ToString();
            else
                return g_SharedData.SYSINFO.bIs64bit.ToString();
        }

        if (strCategory == "Network Interface")
        {
            if (dtNI.Rows.Count > 0)
            {
                var NICList = dtNI.Select("InstanceName IN ('" + strInstanceName + "')");
                if (NICList.Length > 0)
                {
                    foreach (var row in NICList)
                        strReference = row[1].ToString();
                    return strReference;
                }
            }
            else
            {
                SaveNetworkBandwidth();
            }
        }

        if (strCategory == "Active Server Pages")
            return g_SharedData.WEB_SETTING.strHostHeader;

        if (strCategory == ".NET Data Provider for SqlServer")
            return GetSQLProviderReference(strInstanceName);

        if (strCategory.Contains("ASP.NET"))
            return GetSQLProviderReference(strInstanceName);
        //         Site Desc - Root/AppName	_LM_W3SVC_1_ROOT_sqltest

        if (strPCID == "P071" || strPCID == "P075" || strPCID == "P077" || strPCID == "P085" || strPCID == "P107" || strPCID == "P108" || strPCID == "P130")
        {
            if (g_strSavedGUID != g_strGUIDperTurn)          // To get this value once per turn.
            {
                strCategory = "SQLServer:SQL Statistics";
                if (PerformanceCounterCategory.Exists(strCategory))
                {
                    PC.InstanceName = "";
                    PC.CategoryName = strCategory;
                    PC.CounterName = "Batch Requests/sec";
                    PC.NextValue();
                    System.Threading.Thread.Sleep(50);
                    flRValue = PC.NextValue();

                    g_strSavedGUID = g_strGUIDperTurn;
                    strBatchReqPerSecond = flRValue.ToString();
                    return strBatchReqPerSecond;
                }
            }
            else
            {
                return strBatchReqPerSecond;
            }
        }

        if (strPCID == "P073")
        {
            strCategory = "SQLServer:Access Methods";
            if (PerformanceCounterCategory.Exists(strCategory))
            {
                PC.InstanceName = "";
                PC.CategoryName = strCategory;
                PC.CounterName = "Index Searches/sec";
                PC.NextValue();
                System.Threading.Thread.Sleep(50);
                flRValue = PC.NextValue();

                return flRValue.ToString();
            }
        }

        if (strPCID == "P074")
        {
            strCategory = "SQLServer:Access Methods";
            if (PerformanceCounterCategory.Exists(strCategory))
            {
                PC.InstanceName = "";
                PC.CategoryName = strCategory;
                PC.CounterName = "Full Scans/sec";
                PC.NextValue();
                System.Threading.Thread.Sleep(50);
                flRValue = PC.NextValue();

                return flRValue.ToString();
            }
        }

        if (strPCID == "P088")
        {
            strCategory = "SQLServer:Buffer Manager";
            if (PerformanceCounterCategory.Exists(strCategory))
            {
                PC.InstanceName = "";
                PC.CategoryName = strCategory;
                PC.CounterName = "Page reads/sec";
                PC.NextValue();
                System.Threading.Thread.Sleep(50);
                flRValue = PC.NextValue();

                return flRValue.ToString();
            }
        }

        if (strPCID == "P117")
        {
            strCategory = "SQLServer:Databases";
            if (PerformanceCounterCategory.Exists(strCategory))
            {
                PC.InstanceName = strInstanceName;
                PC.CategoryName = strCategory;
                PC.CounterName = "Log File(s) Size (KB)";
                PC.NextValue();
                System.Threading.Thread.Sleep(50);
                flRValue = PC.NextValue();

                return flRValue.ToString();
            }
        }

        if (strPCID == "P128")
        {
            strCategory = "SQLServer:Latches";
            if (PerformanceCounterCategory.Exists(strCategory))
            {
                PC.InstanceName = "";
                PC.CategoryName = strCategory;
                PC.CounterName = "Latch Waits/sec";
                PC.NextValue();
                System.Threading.Thread.Sleep(50);
                flRValue = PC.NextValue();

                return flRValue.ToString();
            }
        }

        // SQL Server CPU Patch. Use RVALUE.
        if (strPCID == "P006")
        {
            return (flPValue / g_SharedData.SYSINFO.iNumberOfProcessors).ToString();
        }


        return "";
    }

    //raw data : lm_w3svc_1_root_sqltest-1-12341[PID]
    //retrun : Site Desc. App Pool Desc(ROOT/AppName) [PID]



    private string GetSQLProviderReference(string strInstance)
    {
        int iIndex = 0;
        string strPID = "";
        string strOutput = "";
        string strSiteDesc = "";
        string strAppDesc = "";
        string strReturn = "";
        string strSiteID = "";

        strOutput = strInstance.ToUpper();
        iIndex = g_SharedData.WEBINFO.iNumberOfAppPools;

        if (strOutput.Contains("__Total__"))             //amended to get __Total__ (ASP.NET Apps v.x.)
            return strOutput;

        if (!strOutput.Contains("W3SVC"))
            return null;

        int i = 0;
        if (iIndex > 0)
        {
            iIndex = strOutput.IndexOf('[', 0);
            if (iIndex > 1)
            {
                strOutput = strOutput.Substring(iIndex + 1);
                iIndex = strOutput.IndexOf(']', 0);

                if (iIndex > 0)
                    strPID = strOutput.Substring(0, iIndex);
            }
        }

        if (strPID.Length > 0)
        {
            for (i = 0; i < g_SharedData.WEBINFO.iNumberOfAppPools; i++)
            {
                if (g_SharedData.arrWPList[i].strPID == strPID)
                {
                    strAppDesc = g_SharedData.arrWPList[i].strAppPoolDesc;
                    break;
                }
            }

        }

        i = 0;
        iIndex = g_SharedData.WEBINFO.iNumberOfSites;
        strOutput = strInstance.ToUpper();

        if (iIndex > 0)
        {
            iIndex = strOutput.IndexOf("W3SVC_");
            if (iIndex > 1)
            {
                strOutput = strOutput.Substring(iIndex + 6);
                iIndex = strOutput.IndexOf('_', 0);

                if (iIndex > 0)
                    strSiteID = strOutput.Substring(0, iIndex);
            }
        }

        if (strSiteID.Length > 0)
        {
          
            for (i = 0; i < g_SharedData.WEBINFO.iNumberOfSites; i++)
            {
                if (g_SharedData.arrSiteList[i].strSiteID == strSiteID)
                {
                    strSiteDesc = g_SharedData.arrSiteList[i].strSiteDesc;
                    break;
                }
            }
        }

        string strAppName = strInstance;

        strAppName = strAppName.ToUpper();
        iIndex = strAppName.IndexOf("_ROOT");

        if (iIndex > 0)
            strAppName = strAppName.Substring(iIndex + 1);
        iIndex = strAppName.IndexOf('-');

        if (iIndex > 0)
            strAppName = strAppName.Substring(0, iIndex);

        //        Console.WriteLine("SQL Reference :" + strAppName);

        if (strPID.Length > 0)
            strReturn = strSiteDesc + ". " + strAppDesc + "(" + strAppName + ") [" + strPID + "]";
        else
            strReturn = strSiteDesc + ". " + "(" + strAppName + ")";

        //      Console.WriteLine("Return :" + strReturn);
        return strReturn;

        //lm_w3svc_1_root_sqltest-1-12341[PID], Site Desc. App Pool Desc(ROOT/AppName) [PID]

    }

    public bool IsPerformanceCounterReady()
    {
        try
        {
            PerformanceCounter PC = new PerformanceCounter();
            string strCategoryName;

            strCategoryName = "Processor";

            if (PerformanceCounterCategory.Exists(strCategoryName))
                return true;
            else
                return false;
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent("Performace category can't be read in this system. - " + ex.Message, "E", 1133);
            return false;
        }

    }

    // called by void BuildInstanceCounterRecord(string strCategory, string strCounter, string strPCID)

    private bool DoesInstanceExist(string strCategory, string strPCID, string strInstanceName)
    {
        var InstanceList = dtInstanceList.Select("PCID = '" + strPCID + "'");
        strInstanceName = strInstanceName.ToUpper();

        if (InstanceList.Length < 1)            // if there's no instance in the list, then return true, to collect all instances
            return true;

        foreach (DataRow dr in InstanceList)
        {
            string strText = dr["InstanceName"].ToString().ToUpper();

            if (dr["IfContains"].ToString() == "1")          // if it contains the instance name 
            {
                if (strInstanceName.Contains(strText))
                    return true;
            }
            else
            {
                if (strText == strInstanceName)
                    return true;
            }

            if (strCategory == "Process")
            {
                strInstanceName = strInstanceName + "#";
                if (strInstanceName.Contains(strText))
                    return true;
            }
        }

        return false;
    }


    public void ReadW3WPMemory()
    {

        if (dtW3WPMemory.Rows.Count > 0)
        {
            DateTime tmNow = new DateTime();
            tmNow = DateTime.Now;

            DateTime tmDuration = new DateTime();
            tmDuration = tmNow.AddSeconds(-60);             // To save size of process memory duing 1 minute, to check process memory increasement with its new size

            try
            {
                var rows = dtW3WPMemory.Select("TimeIn < '" + tmDuration + "'");
                foreach (var row in rows)
                    row.Delete();

                dtW3WPMemory.AcceptChanges();
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "E", 1119);
            }
        }

        string[] arrInstanceNames;

        PerformanceCounterCategory pcCat = new PerformanceCounterCategory();
        PerformanceCounter PC = new PerformanceCounter();

        pcCat.CategoryName = "Process";
        DateTime dtNow = new DateTime();
        dtNow = DateTime.Now;
        float flPValue = 0;

        try
        {

            arrInstanceNames = pcCat.GetInstanceNames();

            foreach (string strProcess in arrInstanceNames)
            {
                if (strProcess == "w3wp" || strProcess.Contains("w3wp#") || strProcess == "java" || strProcess.Contains("java#") || strProcess == "javaw" || strProcess.Contains("javaw#"))
                {
                    PC.CategoryName = "Process";
                    PC.CounterName = "Private Bytes";
                    PC.InstanceName = strProcess;

                    PC.NextValue();
                    System.Threading.Thread.Sleep(100);
                    flPValue = PC.NextValue();

                    string strReference = GetReferenceValue("Process", PC.CounterName, flPValue, PC.InstanceName, "P013");
                    dtW3WPMemory.Rows.Add("P013", g_SharedData.WSP_AGENT_SETTING.strDisplayName, dtNow, flPValue, strReference, strProcess);
                }
            }
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent(ex.Message, "W", 1123);
        }
    }

    public bool CheckSPAgentCPUStatus()
    {
        PerformanceCounter PC = new PerformanceCounter();

        float flPValue = 0;
        int iCPU_UTIL = 0;

        try
        {
            PC.CategoryName = "Process";
            PC.CounterName = "% Processor time";

            //PC.InstanceName = "SP.Client.Agent"; 
            //test code for Console
            PC.InstanceName.Contains("WSP.Console");

            PC.NextValue();
            System.Threading.Thread.Sleep(100);
            flPValue = PC.NextValue();

            iCPU_UTIL = Convert.ToInt32(flPValue);
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent("Service Point Agent Process Instance has been missed. - " + ex.Message, "I", 1188);
            return false;
        }

        if (iCPU_UTIL >= 90)
            return true;

        try
        {
            PC.CategoryName = "Process";
            PC.CounterName = "% Processor time";

            //PC.InstanceName = "SP.Client.Agent"; 
            //test code for Console
            PC.InstanceName = "LogParser";
            PC.NextValue();
            System.Threading.Thread.Sleep(100);
            flPValue = PC.NextValue();

            iCPU_UTIL = Convert.ToInt32(flPValue);
        }
        catch (Exception ex)
        {
            return false;
        }

        if (iCPU_UTIL >= 90)
            return true;

        return false;

    }

    public bool ReadWSPAgentMemorySize()
    {

        PerformanceCounter PC = new PerformanceCounter();

        float flPValue = 0;

        try
        {
            PC.CategoryName = "Process";

            if (g_SharedData.SYSINFO.iWinVer > 2003)
                PC.CounterName = "Working Set - Private";
            else
                PC.CounterName = "Working Set";

            //PC.InstanceName = "SP.Client.Agent";
            //test code for Console
            PC.InstanceName = "WSP.Console.vshost";

            PC.NextValue();
            System.Threading.Thread.Sleep(100);
            flPValue = PC.NextValue();

        }
        catch (Exception ex)
        {
            //WSPEvent.WriteEvent("Reading a memory size of Service Point Agent Process has been missed. - " + ex.Message, "I", 1188);
            return false;
        }

        int iMemorySize = Convert.ToInt32(flPValue / 1000000);

        //Console.WriteLine("#MEM : " + iMaxAgentMemorySize.ToString() + ", Current : " + iMemorySize.ToString());

        if (iMemorySize > g_SharedData.WSP_AGENT_SETTING.iMaxAgentMemorySizeMB)
            return true;
        else
            return false;
    }

    public void StopPerformanceReaders()
    {
        _bPerfReaderThreadFlag = false;
        iThreadID = 0;

        Thread.Sleep(3000);     // To end all reader threads normally.
    }
    
    public void StartPerformanceReaders()
    {
        string[] astrThreadPram = new string[2];

        try
        {            

            InitializeDataTables();
            ReadPerformanceCategory();
            dtCounters.Rows.Clear();

            DataTable dtObjectList = new DataTable();
            dtObjectList = dtPCID.DefaultView.ToTable(true, "PObjectName");

            _bPerfReaderThreadFlag = true;
            iThreadID = 0; 

            foreach (DataRow dr1 in dtObjectList.Rows)
            {
                if (PerformanceCounterCategory.Exists(dr1["PObjectName"].ToString()))
                {
                    _bReaderTimerFlag[iThreadID] = true;
                    PerfReaderThreads[iThreadID] = new Thread(PerfReaderThreadFunction);

                    astrThreadPram[0] = dr1["PObjectName"].ToString();
                    astrThreadPram[1] = iThreadID.ToString();

                    PerfReaderThreads[iThreadID].Start(astrThreadPram);

                    while (_bReaderTimerFlag[iThreadID])
                        Thread.Sleep(20);

                    iThreadID++;                    
                }
                else
                {
                    WSPEvent.WriteEvent("Invalid Performance Objects found - " + dr1["PObjectName"].ToString(), "I", 1162);
                }
            }            

            //Start Syncer Thread for every Perf Readers
            ObserverThread = new Thread(PerfReaderSyncerThread);
            ObserverThread.Start(iThreadID);            
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent(ex.Message, "E", 1117);
        }

    }

    private void PerfReaderSyncerThread(object objThreadCount)
    {
        int iThreads = Convert.ToInt32(objThreadCount.ToString()) - 1;
        bool bIsCollecting = true;

        int iRetryCount = 0;

        DataTable g_dtPValues = new DataTable();
        g_dtPValues.Columns.Clear();

        g_dtPValues.Columns.Add(new DataColumn("PCID", typeof(string)));
        g_dtPValues.Columns.Add(new DataColumn("HOSTNAME", typeof(string)));
        g_dtPValues.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
        g_dtPValues.Columns.Add(new DataColumn("PValue", typeof(float)));
        g_dtPValues.Columns.Add(new DataColumn("RValue", typeof(string)));
        g_dtPValues.Columns.Add(new DataColumn("InstanceName", typeof(string)));

        //PValue 테이블에 카운터 이름 추가
        g_dtPValues.Columns.Add(new DataColumn("CategoryName", typeof(string)));
        g_dtPValues.Columns.Add(new DataColumn("CounterName", typeof(string)));

        //패치 : 데이터 수집간격만큼 기다리던 코드를 변경 -> 메인쓰레드에서 다음 주기 시작을 트리거함.
        DateTime dtSavedTime = new DateTime();
        dtSavedTime = DateTime.Now;

        while (_bPerfReaderThreadFlag)
        {
            if (dtSavedTime != g_dtmNow)    //새로운 수집시간이 설정되면 수집을 바로 시작한다.
            {
                g_dtPValues.Rows.Clear();
                //g_dtPValues.Clear();

                //tmNow = DateTime.Now;

                //guidPerfCollectingTurn = Guid.NewGuid();
                g_strGUIDperTurn = g_guidPerfCollectingTurn.ToString();

                for (int i = 0; i <= iThreads; i++)
                    _bReaderTimerFlag[i] = true;

                bIsCollecting = true;
                iRetryCount = 0;

                while (bIsCollecting)                       // To scsan status of all threads
                {
                    Thread.Sleep(1000);                      // To wait data collections from reader thread until next turn
                    bIsCollecting = false;

                    for (int i = 0; i <= iThreads; i++)
                    {
                        if (_bReaderTimerFlag[i])           // _bReaderTimerFlag = false : Finished collecting perf data in a therad.
                            bIsCollecting = true;
                    }

                    iRetryCount++;                          // Loop exit condition
                    if (iRetryCount > 120)
                    {
                        for (int i = 0; i <= iThreads; i++)
                            _bReaderTimerFlag[i] = true;
                        break;
                    }
                }

                for (int i = 0; i <= iThreads; i++)
                {
                    for (int j = 0; j <= g_iItemsPerThread[i]; j++)
                    {
                        if (stSharedPValueArray[i, j].strInstanceName.Length >= 200)
                            stSharedPValueArray[i, j].strInstanceName = stSharedPValueArray[i, j].strInstanceName.Substring(0, 199);
                        g_dtPValues.Rows.Add(stSharedPValueArray[i, j].strPCID, g_SharedData.WSP_AGENT_SETTING.strDisplayName, g_dtmNow, stSharedPValueArray[i, j].flPValue, stSharedPValueArray[i, j].strRValue, stSharedPValueArray[i, j].strInstanceName, stSharedPValueArray[i, j].strCategoryName, stSharedPValueArray[i, j].strCounterName);
                    }
                }

                g_dtSharedPValues = g_dtPValues.Copy();

                dtSavedTime = g_dtmNow;
            }

            Thread.Sleep(200);

            //dtmDuration = DateTime.Now;
            //lDuration = dtmDuration - tmNow;
            
            //iTimeToWait = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval * 1000 - Convert.ToInt32(lDuration.TotalMilliseconds);
            //if (iTimeToWait <= 0)
            //    iTimeToWait = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval * 1000;
            
            //Thread.Sleep(iTimeToWait);
            
        }

    }

    // A reader thread - Perforamnce Counter Reader Thread foreach Performance Object Name
    // 
    private void PerfReaderThreadFunction(Object objname)
    {
        string[] astrParms = new string[2];
        astrParms = (string[])objname;
        
        int iCurrentThreadID = Convert.ToInt32(astrParms[1].ToString());
        string strPObjectName = astrParms[0].ToString();

        _bReaderTimerFlag[iCurrentThreadID] = false;

        DataTable dtUnitCounters = new DataTable();             // To store counters for this thread
        //DataTable dtCurrentPValues = new DataTable();

        dtUnitCounters = dtCounters.Clone();
        //dtCurrentPValues = g_dtPValues.Clone();        
        string strReference;

        string[] arrInstanceNames = {" "};
        string[] arrSavedInstanceNames = {" "};
        
        int iInstanceCount = 0;
        bool bHasInstance = true;
        bool bSameCounterList = false;
        float flPValue = 0;

        PerformanceCounter[] PC = new PerformanceCounter[500];
        for (int i = 0; i < 500; i++)
            PC[i] = new PerformanceCounter();

        float[] arrPValues = new float[500];

        bHasInstance = HasInstanceList(strPObjectName);

        if (!PerformanceCounterCategory.Exists(strPObjectName))
            return;

        DateTime dtNow = DateTime.Now;
        DateTime dtSaved = DateTime.Now;
        
        //TimeSpan tsElapsedTime = dtNow - dtAgentStartTime;
        TimeSpan tsElapsedTime = new TimeSpan();
        //int iOperationMinutes = tsElapsedTime.Minutes;
        int iElapsedSeconds = 0;

        while (_bPerfReaderThreadFlag)
        {
            if (_bReaderTimerFlag[iCurrentThreadID])
            {
                if (bHasInstance)
                {
                    PerformanceCounterCategory pcCat = new PerformanceCounterCategory();
                    pcCat.CategoryName = strPObjectName;

                    arrInstanceNames = pcCat.GetInstanceNames();
                    iInstanceCount = arrInstanceNames.Length;

                    if (!IsSameInstanceList(arrInstanceNames, arrSavedInstanceNames))
                    {
                        dtUnitCounters.Rows.Clear();
                        dtUnitCounters = BuildUnitCounterList(strPObjectName);
                        bSameCounterList = false;
                    }
                    else
                    {
                        bSameCounterList = true;
                    }
                }
                else
                {
                    if (dtUnitCounters.Rows.Count < 1)
                    {
                        dtUnitCounters = BuildUnitCounterList(strPObjectName);
                        bSameCounterList = false;
                    }
                    else
                        bSameCounterList = true;
                }

                ///////////////////////////////////////////////////////////////////
                // PCID_REFRESH_INTERVAL(3분)마다 성능카운터를 갱신한다. 
                ///////////////////////////////////////////////////////////////////
                dtNow = DateTime.Now;
                tsElapsedTime = dtNow - dtSaved;
                iElapsedSeconds = (int)tsElapsedTime.TotalSeconds;

                if (iElapsedSeconds > PCID_REFRESH_INTERVAL && bSameCounterList)
                {
                    dtUnitCounters.Rows.Clear();
                    dtUnitCounters = BuildUnitCounterList(strPObjectName);
                    dtSaved = dtNow;
                    bSameCounterList = false;
                }
                ///////////////////////////////////////////////////////////////////

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                // Read Performance Values
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                
                if (bSameCounterList)     
                {
                    for (int i = 0; i < dtUnitCounters.Rows.Count; i++)
                    {
                        try
                        {
                            flPValue = PC[i].NextValue();
                            if (float.TryParse(flPValue.ToString(), out arrPValues[i]))
                                arrPValues[i] = flPValue;
                            else
                                arrPValues[i] = 0;
                        }
                        catch (Exception ex)
                        {
                            WSPEvent.WriteEvent(ex.Message + " : " + strPObjectName, "I", 1118);
                        }
                    }
                }
                else
                {
                    int iRow = 0;
                    foreach (DataRow dr in dtUnitCounters.Rows)
                    {
                        try
                        {

                            PC[iRow].CategoryName = dr["PObjectName"].ToString();
                            PC[iRow].CounterName = dr["PCounterName"].ToString();
                            if (bHasInstance)
                                PC[iRow].InstanceName = dr["InstanceName"].ToString();

                            PC[iRow].NextValue();
                            Thread.Sleep(40);
                            flPValue = PC[iRow].NextValue();

                            if (float.TryParse(flPValue.ToString(), out arrPValues[iRow]))
                                arrPValues[iRow] = flPValue;
                            else
                                arrPValues[iRow] = 0;

                            iRow++;
                        }
                        catch (Exception ex)
                        {
                            WSPEvent.WriteEvent(ex.Message + " : " + strPObjectName + " : " + dr["PCounterName"].ToString(), "I", 1008);
                        }
                    }
                }                

                try
                {
                    int idx = 0;
                    foreach (DataRow dr in dtUnitCounters.Rows)
                    {

                        stSharedPValueArray[iCurrentThreadID, idx].strPCID = dr["PCID"].ToString();
                        stSharedPValueArray[iCurrentThreadID, idx].flPValue = arrPValues[idx];
                        
                        if (bHasInstance)
                        {
                            strReference = GetReferenceValue(strPObjectName, PC[idx].CounterName, arrPValues[idx], dr["InstanceName"].ToString(), dr["PCID"].ToString());
                            stSharedPValueArray[iCurrentThreadID, idx].strRValue = strReference;
                            stSharedPValueArray[iCurrentThreadID, idx].strInstanceName = dr["InstanceName"].ToString();
                        }
                        else
                        {
                            strReference = GetReferenceValue(strPObjectName, PC[idx].CounterName, arrPValues[idx], "", dr["PCID"].ToString());
                            stSharedPValueArray[iCurrentThreadID, idx].strRValue = strReference;
                            stSharedPValueArray[iCurrentThreadID, idx].strInstanceName = "";
                        }

                        stSharedPValueArray[iCurrentThreadID, idx].strCategoryName = dr["PObjectName"].ToString();
                        stSharedPValueArray[iCurrentThreadID, idx].strCounterName = dr["PCounterName"].ToString();

                        // test code
                        //if(strPObjectName.Contains("Processor"))
                        //    Console.WriteLine(iCurrentThreadID.ToString() + ", " + stSharedPValueArray[iCurrentThreadID, idx].strPCID + ", " + stSharedPValueArray[iCurrentThreadID, idx].flPValue + ", " + stSharedPValueArray[iCurrentThreadID, idx].strInstanceName);

                        idx++;
                    }

                    g_iItemsPerThread[iCurrentThreadID] = idx - 1;

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                }
                catch (Exception ex)
                {
                    WSPEvent.WriteEvent("It failed to get reference values to merge." + ex.Message, "W", 1164);
                }
                // To save current instance list.
                arrSavedInstanceNames = arrInstanceNames;

                _bReaderTimerFlag[iCurrentThreadID] = false;

            }   // END OF TIMER FLAG

            Thread.Sleep(200); // SHOULD SLEEP UNTILL NEXT TURN
        }       // END OF WHILE

    }

    private bool IsSameInstanceList(string[] arrNew, string[] arrOld)
    {
        if (arrOld.Length == arrNew.Length)
        {
            for (int i = 0; i < arrNew.Length; i++)
            {
                if (arrNew[i] != arrOld[i])
                    return false;
            }
            return true;
        }
        return false;
    }

    private bool HasInstanceList(string strPObjectName)
    {
        var rowsCounters = dtPCID.Select("PObjectName = '" + strPObjectName + "'");

        // Check if this counters have instance, if not, no need to build instance list.
        foreach (DataRow dr1 in rowsCounters)
        {
            string strHasInstance = dr1["HasInstance"].ToString().ToLower();
            if (strHasInstance == "true")
                return true;
            else
                return false;
        }

        return false;
    }

    private DataTable BuildUnitCounterList(string strPObjectName)
    {


        DataTable dtUnitCounters = new DataTable();             // To store counters for this thread

        dtUnitCounters = dtCounters.Clone();

        bool bHasInstance = true;
        string strHasInstance;
        string strPCID;
        string strCounterName;

        string[] arrInstanceNames;

        var rowsCounters = dtPCID.Select("PObjectName like '" + strPObjectName + "%'");        

        foreach (DataRow dr1 in rowsCounters)
        {
            strHasInstance = dr1["HasInstance"].ToString().ToLower();
            strPCID = dr1["PCID"].ToString();
            strCounterName = dr1["PCounterName"].ToString();

            if (strHasInstance == "true")
                bHasInstance = true;
            else
                bHasInstance = false;

            if (bHasInstance)
            {
                strPObjectName = dr1["PObjectName"].ToString();
                PerformanceCounterCategory pcCat = new PerformanceCounterCategory();
                pcCat.CategoryName = strPObjectName;

                arrInstanceNames = pcCat.GetInstanceNames();

                foreach (string strInstances in arrInstanceNames)
                {
                    if (DoesInstanceExist(strPObjectName, strPCID, strInstances))
                        dtUnitCounters.Rows.Add(strPObjectName, strCounterName, strInstances, true, strPCID);
                }
            }
            else
                dtUnitCounters.Rows.Add(strPObjectName, dr1["PCounterName"].ToString(), null, false, dr1["PCID"].ToString());

        }

        return dtUnitCounters;

    }
}

