using System;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml;
using System.Data;
using System.Threading;

//  Performance Object, C#
//  http://msdn.microsoft.com/en-US/library/system.diagnostics.performancecounter.nextvalue(v=vs.80)#Y705 

public class clsPerfMonOp
{
    public DataTable dtPCID = new DataTable();
    public DataTable dtCategory = new DataTable();
    public DataTable dtCounters = new DataTable();
    public DataTable dtPValues;
    public DataTable dtWPList = new DataTable();
    public DataTable dtNI = new DataTable();
    public DataTable dtSiteList = new DataTable();
    public DataTable dtInstanceList = new DataTable();

    public DataTable dtW3WPMemory = new DataTable();

    public string strServerType;
    public int iWindowsVersion;
    public string strApplicationType;
    public int iProcessors;
    public bool bIs64bit;
    public string strHostName;
    public float flRAMSize;
    public string strHostHeaders;
    public DateTime dtLastUpdatedTime;
    public bool bEnableIISCounters;
    
    EventRecorder SHSTEvent = new EventRecorder();

    public volatile bool _bIISPerfThread = false;
    private volatile bool _bObserverFlag = false;                // To start / pause second SQL performance reader thread   
    
    private DataTable dtIISPValues;
    private Thread IISPerfThread;

    private void ReadPerformanceCategory()   // To compare Performance Object Names in DB with local objects (ASP.NET Versions).
    {

        if (strServerType.Contains("BizTalk") && !bEnableIISCounters)
        {
            var rowsBizTalkCounters = dtPCID.Select("ServerType NOT IN ('Windows', 'BizTalk')");
            foreach (var row in rowsBizTalkCounters)
                row.Delete();

            dtPCID.AcceptChanges();
        }

        if (strServerType.Contains("BizTalk") && bEnableIISCounters)
        {
            var rowsCounters = dtPCID.Select("ServerType NOT IN ('Windows', 'BizTalk', 'Web')");
            foreach (var row in rowsCounters)
                row.Delete();

            dtPCID.AcceptChanges();
            SetASPNETCategoryNames();
        }


        if (strServerType.Contains("Web"))
        {
            var rowsIISCounters = dtPCID.Select("ServerType NOT IN ('Windows', 'Web')");
            foreach (var row in rowsIISCounters)
                row.Delete();

            dtPCID.AcceptChanges();
            SetASPNETCategoryNames();
        }

        if (strServerType.Contains("SQL"))
        {
            var rowsIISCounters = dtPCID.Select("ServerType NOT IN ('Windows')");
            foreach (var row in rowsIISCounters)
                row.Delete();

            dtPCID.AcceptChanges();
        }

        if (strServerType == "Windows")
        {
            var rows = dtPCID.Select("ServerType NOT IN ('Windows')");
            foreach (var row in rows)
                row.Delete();

            dtPCID.AcceptChanges();
        }
    }

    private void SetASPNETCategoryNames()
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
                SHSTEvent.WriteEvent(ex.Message, "E", 1119);
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

                    string strReference = GetReferenceValue("Process", PC.CounterName, flPValue, PC.InstanceName);
                    dtW3WPMemory.Rows.Add("P013", strHostName, dtNow, flPValue, strReference, strProcess);
                }
            }
        }
        catch (Exception ex)
        {
            SHSTEvent.WriteEvent(ex.Message, "E", 1123);
        }
    }

    public bool ReadSHSTAgentMemorySize()
    {

        PerformanceCounter PC = new PerformanceCounter();

        float flPValue = 0;

        try
        {
            PC.CategoryName = "Process";
            PC.CounterName = "Private Bytes";
            PC.InstanceName = "Service.Point.Agent";
            PC.NextValue();
            System.Threading.Thread.Sleep(100);
            flPValue = PC.NextValue();

        }
        catch (Exception ex)
        {
            SHSTEvent.WriteEvent("Reading a memory size of Service Point Agent Process has been missed. - " + ex.Message, "I", 1123);
            return true;
        }

        int iMemorySize = Convert.ToInt32(flPValue);        
        return true;
    }


    public void InitializeDataTables()
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

    public void StartIISPerfReaderThread()
    {
        _bIISPerfThread = true;
        _bObserverFlag = false;         // To avoid collecting data before collecting main counters with initial data

        IISPerfThread = new Thread(ReadIISPerformanceData);
        IISPerfThread.Start();
    }

    public void BuildCounterList()
    {
        try
        {            
            ReadPerformanceCategory();
            bool bHasInstance;

            dtCounters.Rows.Clear();

            foreach (DataRow dr1 in dtPCID.Rows)
            {
                string strHasInstance = dr1["HasInstance"].ToString().ToLower();

                if (strHasInstance == "true")
                    bHasInstance = true;
                else
                    bHasInstance = false;

                if (PerformanceCounterCategory.Exists(dr1["PObjectName"].ToString()))
                {
                    if (bHasInstance)
                        BuildInstanceCounterRecord(dr1["PObjectName"].ToString(), dr1["PCounterName"].ToString(), dr1["PCID"].ToString());
                    else
                        AddCounterRecord(dr1["PObjectName"].ToString(), dr1["PCounterName"].ToString(), dr1["PCID"].ToString());
                }
            }
        }
        catch (Exception ex)
        {
            SHSTEvent.WriteEvent(ex.Message, "E", 1117);
        }

    }


    private void AddCounterRecord(string strCategory, string strCounter, string strPCID)
    {
        dtCounters.Rows.Add(strCategory, strCounter, null, false, strPCID);
    }

    private void BuildInstanceCounterRecord(string strCategory, string strCounter, string strPCID)
    {
        string[] arrInstanceNames;

        PerformanceCounterCategory pcCat = new PerformanceCounterCategory();
        pcCat.CategoryName = strCategory;

        if (strCategory == "Process")
        {

            arrInstanceNames = pcCat.GetInstanceNames();

            foreach (string strProcess in arrInstanceNames)
            {
//                if (strProcess == "w3wp" || strProcess.Contains("w3wp#") || strProcess == "java" || strProcess.Contains("java#") || strProcess == "javaw" || strProcess.Contains("javaw#"))
                if(DoesInstanceExist(strCategory, strPCID, strProcess))
                    dtCounters.Rows.Add(strCategory, strCounter, strProcess, true, strPCID);
            }

            return;
        }

        if (strCategory == "Processor")
        {
            arrInstanceNames = pcCat.GetInstanceNames();

            foreach (string strProcessor in arrInstanceNames)
            {
                if(DoesInstanceExist(strCategory, strPCID, strProcessor))
                    dtCounters.Rows.Add(strCategory, strCounter, "_Total", true, strPCID);
            }
            return;
        }

        if (strCategory == ".NET CLR Memory")
        {
            arrInstanceNames = pcCat.GetInstanceNames();

            foreach (string strCLRProcess in arrInstanceNames)
            {
                //if (strCLRProcess.Contains("w3wp"))
                if (DoesInstanceExist(strCategory, strPCID, strCLRProcess))
                    dtCounters.Rows.Add(strCategory, strCounter, strCLRProcess, true, strPCID);
            }

            return;
        }

        //if (strCategory == ".NET Data Provider for SqlServer")
        //{
        //    arrInstanceNames = pcCat.GetInstanceNames();

        //    foreach (string strSQLDataInstance in arrInstanceNames)
        //    {
        //        //                if (strSQLDataInstance.Contains("w3svc"))
        //        dtCounters.Rows.Add(strCategory, strCounter, strSQLDataInstance, true, strPCID);
        //    }

        //    return;
        //}

        //if (strCategory == "LogicalDisk")
        //{
        //    dtCounters.Rows.Add(strCategory, strCounter, "C:", true, strPCID);
        //    if (strCounter.Contains("Avg. Disk sec/Read"))
        //        dtCounters.Rows.Add(strCategory, strCounter, "_Total", true, strPCID);
        //    return;
        //}

        if (strCategory == "Web Service")
        {
            arrInstanceNames = pcCat.GetInstanceNames();

            foreach (string strSiteInstance in arrInstanceNames)
            {
//                if (!strSiteInstance.Contains("_Total"))
                    dtCounters.Rows.Add(strCategory, strCounter, strSiteInstance, true, strPCID);
            }

            return;
        }

        if (strCategory.Contains("ASP.NET Apps"))
        {
            arrInstanceNames = pcCat.GetInstanceNames();
            foreach (string strApplicationName in arrInstanceNames)
            {
//                if (!strApplicationName.Contains("_Total"))                   // Amended. to get __Total__ values.
                    dtCounters.Rows.Add(strCategory, strCounter, strApplicationName, true, strPCID);
            }

            return;
        }

        arrInstanceNames = pcCat.GetInstanceNames();
        foreach (string strInstances in arrInstanceNames)
        {
            if(DoesInstanceExist(strCategory, strPCID, strInstances))
                dtCounters.Rows.Add(strCategory, strCounter, strInstances, true, strPCID);
        }
    }

    private void ReadIISPerformanceData()
    {
        bool bHasInstance;
        string strCategory;
        string strInstanceName;
        float flPValue = 0;
        string strReference;
        bool bHasException = false;

        PerformanceCounter PC = new PerformanceCounter();
        string strCategoryCheck = "";

        while (_bIISPerfThread)
        {
            if (_bObserverFlag)
            {

                dtIISPValues = new DataTable();
                dtIISPValues = dtPValues.Clone();

                foreach (DataRow dr1 in dtCounters.Rows)
                {
                    bHasInstance = Convert.ToBoolean(dr1["HasInstance"]);
                    strCategory = dr1["PObjectName"].ToString();

                    strCategoryCheck = strCategory.ToUpper();
                    if (strCategoryCheck.Contains("ASP.NET") || strCategoryCheck.Contains("WEB") || strCategoryCheck.Contains("ACTIVE SERVER PAGES"))
                    {
                        if (PerformanceCounterCategory.Exists(strCategory))
                        {
                            if (bHasInstance)
                                PC.InstanceName = dr1["InstanceName"].ToString();
                            else
                                PC.InstanceName = "";

                            PC.CategoryName = strCategory;
                            PC.CounterName = dr1["PCounterName"].ToString();
                            try
                            {
                                bHasException = true;           // assume there can be an exception until end of try block.

                                PC.NextValue();
                                if (PC.CounterName.Contains("%"))
                                    System.Threading.Thread.Sleep(200);
                                else
                                {
                                    if (PC.CounterName.Contains("/"))
                                        System.Threading.Thread.Sleep(100);
                                    else
                                        System.Threading.Thread.Sleep(20);
                                }
                                flPValue = PC.NextValue();
                                bHasException = false;          // if this code reaches here, there was no exception.
                            }
                            catch (Exception ex)
                            {
                                SHSTEvent.WriteEvent(ex.Message + " : " + strCategory + " : " + PC.CounterName, "I", 1118);
                                bHasException = true;
                            }

                            if (!bHasException)
                            {
                                strReference = GetReferenceValue(strCategory, PC.CounterName, flPValue, PC.InstanceName);
                                if (strReference != null)
                                    if (strReference.Length > 64)
                                        strReference = strReference.Substring(0, 63);
                                if (strHostName.Length > 64)
                                    strHostName = strHostName.Substring(0, 63);
                                strInstanceName = dr1["InstanceName"].ToString();

                                if (strInstanceName != null)
                                    if (strInstanceName.Length >= 200)
                                        strInstanceName = strInstanceName.Substring(0, 199);

                                if (flPValue != null)
                                {
                                    float flNumber = flPValue;
                                    if (float.TryParse(flPValue.ToString(), out flNumber))           // check if a performance value is the correct float casting.
                                        dtIISPValues.Rows.Add(dr1["PCID"].ToString(), strHostName, dtLastUpdatedTime, flPValue, strReference, strInstanceName);
                                }
                            }
                            bHasException = false;

                        }
                    }

                }       // END OF FOREACH

            }            
            _bObserverFlag = false;
            Thread.Sleep(200);
        }
    }

    private void InitializePerfTable()
    {

        dtPValues.Columns.Clear();

        dtPValues.Columns.Add(new DataColumn("PCID", typeof(string)));
        dtPValues.Columns.Add(new DataColumn("HOSTNAME", typeof(string)));
        dtPValues.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
        dtPValues.Columns.Add(new DataColumn("PValue", typeof(float)));
        dtPValues.Columns.Add(new DataColumn("RValue", typeof(string)));
        dtPValues.Columns.Add(new DataColumn("InstanceName", typeof(string)));
    }

    public void ReadPerformanceData()
    {

        dtPValues = new DataTable();
        InitializePerfTable();

        DataTable dtWinPValues = new DataTable();
        dtWinPValues = dtPValues.Clone();

        bool bHasInstance;
        string strCategory;
        string strInstanceName;
        float flPValue = 0;
        string strReference;
        bool bHasException = false;
               
        PerformanceCounter PC = new PerformanceCounter();
        DateTime dtNow = new DateTime();
        dtNow = DateTime.Now;
        dtLastUpdatedTime = dtNow;          //set last inserted time in HostStatus table.
        string strCategoryCheck = "";

        _bObserverFlag = true;                  //To start IIS Performance Reader Thread

        foreach (DataRow dr1 in dtCounters.Rows)
        {
            bHasInstance = Convert.ToBoolean(dr1["HasInstance"]);
            strCategory = dr1["PObjectName"].ToString();
            strCategoryCheck = strCategory.ToUpper();

            if (strCategoryCheck.Contains("ASP.NET") || strCategoryCheck.Contains("WEB") || strCategoryCheck.Contains("ACTIVE SERVER PAGES"))
                continue;

            if (PerformanceCounterCategory.Exists(strCategory))
            {
                if (bHasInstance)
                    PC.InstanceName = dr1["InstanceName"].ToString();
                else
                    PC.InstanceName = "";

                PC.CategoryName = strCategory;
                PC.CounterName = dr1["PCounterName"].ToString();
                try
                {
                    bHasException = true;           // assume there can be an exception until end of try block.

                    PC.NextValue();
                    if (PC.CounterName.Contains("%"))
                        System.Threading.Thread.Sleep(200);
                    else
                    {
                        if (PC.CounterName.Contains("/"))
                            System.Threading.Thread.Sleep(100);
                        else
                            System.Threading.Thread.Sleep(20);
                    }
                    flPValue = PC.NextValue();
                    bHasException = false;          // if this code reaches here, there was no exception.
                }
                catch (Exception ex)
                {
                    SHSTEvent.WriteEvent(ex.Message + " : " + strCategory + " : " + PC.CounterName, "I", 1118);
                    bHasException = true;
                }

                if (!bHasException)
                {
                    strReference = GetReferenceValue(strCategory, PC.CounterName, flPValue, PC.InstanceName);
                    if (strReference != null)
                        if (strReference.Length > 64)
                            strReference = strReference.Substring(0, 63);
                    if (strHostName.Length > 64)
                        strHostName = strHostName.Substring(0, 63);
                    strInstanceName = dr1["InstanceName"].ToString();

                    if (strInstanceName != null)
                        if (strInstanceName.Length >= 200)
                            strInstanceName = strInstanceName.Substring(0, 199);

                    if (flPValue != null)
                    {
                        float flNumber = flPValue;
                        if (float.TryParse(flPValue.ToString(), out flNumber))           // check if a performance value is the correct float casting.
                            dtWinPValues.Rows.Add(dr1["PCID"].ToString(), strHostName, dtNow, flPValue, strReference, strInstanceName);
                    }
                }
                bHasException = false;

            }
            
        }

        if (_bIISPerfThread)                // In case that IIS perf thread is working
        {
            int iretry = 0;
            while (_bObserverFlag)      // To wait until 2nd perf thread finishes
            {
                Thread.Sleep(100);
                iretry++;
                if (iretry > 300)                           //to wait 30 seconds
                {
                    _bObserverFlag = false;
                    break;
                }
            }

            if (dtIISPValues.Rows.Count > 0)
                dtPValues.Merge(dtIISPValues);

            dtIISPValues.Clear();
            
        }

        if (dtWinPValues.Rows.Count > 0)
            dtPValues.Merge(dtWinPValues);

        dtWinPValues.Clear();        
        
    }

 
/*
 *      PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");     
 *      CounterSample cs1 = cpuCounter.NextSample();     
 *      System.Threading.Thread.Sleep(100);     
 *      CounterSample cs2 = cpuCounter.NextSample();     
 *      float finalCpuCounter = CounterSample.Calculate(cs1, cs2);
 *      */


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
            SHSTEvent.WriteEvent(ex.Message, "E", 1110);
        }
    }


    private string GetReferenceValue(string strCategory, string strCounterName, float flPValue, string strInstanceName)
    {
        string strReference = "";

        if (strCategory == "Processor")
            return iProcessors.ToString();

        if (strCategory == "System")
        {
            if (strCounterName.Contains("Processor Queue"))
                return iProcessors.ToString();
            else
                return (flPValue / iProcessors).ToString();
        }

        if (strCategory == ".NET CLR Memory" || strCategory == "Process")
        {
            if (dtWPList.Rows.Count > 0)
            {
                var procList = dtWPList.Select("InstanceName IN ('" + strInstanceName + "')");
                if (procList.Length > 0)
                {
                    foreach (var row in procList)
                        strReference = row[2].ToString() + "(" + row[1].ToString() + ")";
                    return strReference;
                }
            }
        }

        if (strCategory == "Memory")
        {
            if (strCounterName.Contains("Available") || strCounterName.Contains("Committed"))
                return flRAMSize.ToString();
            else
                return bIs64bit.ToString();
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
            return strHostHeaders;

        if (strCategory == ".NET Data Provider for SqlServer")
            return GetSQLProviderReference(strInstanceName);

        if (strCategory.Contains("ASP.NET"))
            return GetSQLProviderReference(strInstanceName);
        //         Site Desc - Root/AppName	_LM_W3SVC_1_ROOT_sqltest

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
        iIndex = dtWPList.Rows.Count;

        if(strOutput.Contains("__Total__"))             //amended to get __Total__ (ASP.NET Apps v.x.)
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
            var AppDesc = dtWPList.Select("PID in ('" + strPID + "')");
            foreach (var row in AppDesc)
            {
                strAppDesc = row[2].ToString();
            }
        }

        i = 0;
        iIndex = dtSiteList.Rows.Count;
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
            var SiteDesc = dtSiteList.Select("SiteID in ('" + strSiteID + "')");
            foreach (var row1 in SiteDesc)
            {
                strSiteDesc = row1[1].ToString();
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
            SHSTEvent.WriteEvent("Performace category can't be read in this system. - " + ex.Message, "E", 1133);
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
}
