using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;

using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.IO;
using System.Threading;

class SQLDB_Monitor
{
    //get
    public DataTable dtChkQuery = new DataTable();
    public DataTable dtAlertRules = new DataTable();
    public DataTable dtAlerts = new DataTable();

    private DataTable dtSavedResult = new DataTable();

    public string strLocalConnectionString;

    public SharedData g_SharedData = new SharedData();
    
    public DateTime g_TimeIn = new DateTime();
    public DateTime g_TimeIn_UTC = new DateTime();
    public Guid g_sharedGUID = new Guid();
    
    //set
    public string strReadRunningQuery = "";
    string strColsToInsert = "";

    // To Check Linked Servers
    bool bMonitorLinkedServer = false;
    private volatile bool _bIsCheckingLinked = false;
    string strLinkedServerQuery = "";
    string strLinkedServerSP = "";
    string strLinkedServerQueryTable = "";
    
    EventRecorder WSPEvent = new EventRecorder();

    public const int MAX_QUERY = 150;
    public const int MAX_JOBS = 500;

    int[,] iaInterval = new int[MAX_QUERY, 2];         /// Query ID, Interval, Time Passed.
    DateTime[] dtLastRun = new DateTime[MAX_QUERY];
    DateTime dtNow = new DateTime();

    int iQueryCount = 0;
    bool bFirstRun_Q3 = true;
    bool bFirstRun_Q2 = true;

    public struct stMonitoringQueryDesc
    {
        public string strReasonCode;
        public string strReasonCodeDesc;
        public bool bIsEnabled;
        public string strAlertLevel;
    }

    public stMonitoringQueryDesc[] stSQLAlerts = new stMonitoringQueryDesc[5];

    public struct stSQLJobs
    {
        public int iJobID;
        public string strJobName;
    }

    stSQLJobs[] stJobs = new stSQLJobs[MAX_JOBS];

    public struct stSQLRegistedJobs
    {
        public string strJobName;
        public Int16 iEnabled;
    }

    stSQLRegistedJobs[] stRegistedJobs = new stSQLRegistedJobs[MAX_JOBS];


    public SQLDB_Monitor()
    {
        for (int i = 0; i < MAX_JOBS; i++)
            stJobs[i].iJobID = 0;

        dtAlerts.Columns.Clear();
        dtAlerts.Columns.Add(new DataColumn("ReasonCode", typeof(string)));
        dtAlerts.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
        dtAlerts.Columns.Add(new DataColumn("Hostname", typeof(string)));
        dtAlerts.Columns.Add(new DataColumn("InstanceName", typeof(string)));
        dtAlerts.Columns.Add(new DataColumn("PValue", typeof(float)));
        dtAlerts.Columns.Add(new DataColumn("AlertStatus", typeof(int)));                   //reserved field for future purpose.
        dtAlerts.Columns.Add(new DataColumn("AlertDescription", typeof(string)));
        dtAlerts.Columns.Add(new DataColumn("AlertRecordID", typeof(Guid)));

    }

    private void SetMonitoringAlertDesc()
    {
        string strReasonCode = "";
        int i = 0;

        if (dtAlertRules.Rows.Count > 0)
        {
            foreach (DataRow dr in dtAlertRules.Rows)
            {
                strReasonCode = dr["ReasonCode"].ToString();
                if (strReasonCode == "R163" || strReasonCode == "R164" || strReasonCode == "R165" || strReasonCode == "R166" || strReasonCode == "R167")
                {
                    stSQLAlerts[i].strReasonCode = strReasonCode;
                    stSQLAlerts[i].bIsEnabled = Convert.ToBoolean(dr["IsEnabled"].ToString());
                    stSQLAlerts[i].strReasonCodeDesc = dr["ReasonCodeDesc"].ToString();
                    stSQLAlerts[i].strAlertLevel = dr["AlertLevel"].ToString();
                    i++;
                }
            }
        }
    }


    public void ReadMonitoringQueriesWS()
    {

        int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;        

        try
        {
            SetMonitoringAlertDesc();           // Set Alert Description from Alert Rules table.

            int iSavedQueryCount = iQueryCount;

            WSP.Console.WS_AGENTINITIAL.AgentInitial QueryDefineList = new WSP.Console.WS_AGENTINITIAL.AgentInitial();
            QueryDefineList.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AgentInitial.asmx";
            QueryDefineList.Timeout = 10000;

            DataTable dtParms = SetParmeterTable();

            byte[] dtResult = QueryDefineList.QueryDefineList(iServerNumber.ToString());

            if (dtResult == null)
                return;

            dtChkQuery.Clear();
            dtChkQuery = BytesToDataTable(dtResult);

            iQueryCount = dtChkQuery.Rows.Count;

            if (iQueryCount > 0)                        
            {
                int i = 0;
                bool bEnabled = false;
                int iQueryID = 0;

                foreach (DataRow dr in dtChkQuery.Rows)            // save query interval foreach Query ID.
                {
                    try
                    {
                        iQueryID = Convert.ToInt32(dr["QueryID"].ToString());
                        bEnabled = Convert.ToBoolean(dr["Enabled"].ToString());

                        iaInterval[i, 0] = iQueryID;
                        iaInterval[i, 1] = Convert.ToInt32(dr["Interval"].ToString());
                        dtLastRun[i] = DateTime.Now.AddHours(-24.00);
                        i++;

                        if (iQueryID == 4 && bEnabled)      // To Save Linked Server Checking
                        {
                            bMonitorLinkedServer = true;
                            strLinkedServerQuery = dr["Query"].ToString();
                            strLinkedServerSP = dr["SPName"].ToString();
                            strLinkedServerQueryTable = dr["DestinationTable"].ToString();
                        }

                        if (iQueryID == 1)
                            strReadRunningQuery = dr["Query"].ToString();
                    }
                    catch (Exception ex)
                    {
                        WSPEvent.WriteEvent("Service Point Agent failed to read list of query definitions from DB - " + ex.Message, "W", 1302);
                    }
                }
            }
        }
        catch (Exception e)
        {
            WSPEvent.WriteEvent("Service Point Agent failed to read list of query definitions from DB - " + e.Message, "W", 1128);
        }


    }

    public DataTable RunChkQueries()
    {
        dtAlerts.Clear();

        dtNow = DateTime.Now;
        bool bEnabled = false;
        int iQueryID = 0;
        string strTargetTableName = "";
        string strSchedule = "";

        if (bMonitorLinkedServer)
        {
            if (CheckTimeToRunQuery(4, "") && !_bIsCheckingLinked)  // 시간이 되었거나, 현재 동작중이 아니면.
            {
                Thread QueryCheckThread = new Thread(CheckSQLLinkedServer);
                QueryCheckThread.Start();                
            }
        }
        
        foreach (DataRow dr in dtChkQuery.Rows)
        {
            try
            {
                iQueryID = Convert.ToInt32(dr["QueryID"].ToString());
                bEnabled = Convert.ToBoolean(dr["Enabled"].ToString());
                strTargetTableName = dr["DestinationTable"].ToString();
                
                //v3. 쿼리진단 테이블 구조 변경 : SPName컬럼에, Ad Hoc 쿼리인 경우, INSERT할 컬럼명을 기재한다.
                if(dr["SPName"] != null)
                    strColsToInsert = dr["SPName"].ToString();

                if (dr["OccursTime"] != null)
                    strSchedule = dr["OccursTime"].ToString();

                if (iQueryID != 1 && iQueryID != 4)         // 실행중인 쿼리 내역, 링크드 서버 체크 쿼리가 아니면.
                {
                    if (!string.IsNullOrEmpty(strTargetTableName))         // 대상 테이블이 있는 경우.
                    {
                        if (CheckTimeToRunQuery(iQueryID, strSchedule) && bEnabled)
                            ExecuteQueryRecordCommand(dr["Query"].ToString(), strTargetTableName, iQueryID, "");
                    }
                    else                                        // 대상 SP가 있는 경우.
                    {
                        if (CheckTimeToRunQuery(iQueryID, strSchedule) && bEnabled)
                            ExecuteQueryRecordCommand(dr["Query"].ToString(), "", iQueryID, dr["SPName"].ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Anyalizing SQL Monitoring queries has failed in this local database. - " + ex.Message, "W", 1201);
            }
        }

        return dtAlerts;

    }

    void CheckSQLLinkedServer()
    {
        _bIsCheckingLinked = true;
        
        ExecuteQueryRecordCommand(strLinkedServerQuery, strLinkedServerQueryTable, 4, strLinkedServerSP);

        _bIsCheckingLinked = false;
    }
     
    private bool CheckTimeToRunQuery(int iQueryID, string strSchedule)
    {
        DateTime dtNow = DateTime.Now;
        DateTime dtTimePassed = new DateTime();

        for (int i = 0; i < MAX_QUERY; i++)
        {
            if (iaInterval[i,0] == iQueryID)
            {
                if (iaInterval[i, 1] > 0)
                {
                    dtTimePassed = dtNow.AddSeconds(Convert.ToDouble(iaInterval[i, 1]) * (-1));       // set (now - time passed)

                    if (dtTimePassed.CompareTo(dtLastRun[i]) > 0)                                     //  if dtTimePassed is greater, -> means it passed more than interval.
                    {
                        dtLastRun[i] = dtNow;
                        return true;
                    }
                    else
                        return false;
                }
                else               // 일정이 기재된 쿼리인 경우, 실행하려면 TRUE를 리턴한다.
                {
                    dtTimePassed = dtNow.AddSeconds(Convert.ToDouble(60) * (-1));       // set (now - time passed)

                    if (dtTimePassed.CompareTo(dtLastRun[i]) > 0)           //  1분이 지났으면
                    {
                        if (dtNow.ToString("HH:mm") == strSchedule)
                        {
                            dtLastRun[i] = dtNow;
                            return true;
                        }
                    }
                    else
                        return false;

                }
            }
        }
        return false;
    }

    void ExecuteQueryRecordCommand(string strQuery, string strDestTable, int iQueryID, string strSPName)
    {
        DataTable dtQueryResult = new DataTable();
        DateTime dtTimeIn = DateTime.Now;
        DateTime dtTimeIn_UTC = DateTime.UtcNow;

        using (SqlConnection con1 = new SqlConnection(strLocalConnectionString))
        {
            try
            {
                WSP.Console.WS_QUERY.AddRunningQueries wsInsert = new WSP.Console.WS_QUERY.AddRunningQueries();
                wsInsert.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AddRunningQueries.asmx";
                wsInsert.Timeout = 15000;

                int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

                SqlDataAdapter da = new SqlDataAdapter();

                da.SelectCommand = new SqlCommand("sp_executesql", con1);
                da.SelectCommand.CommandType = CommandType.StoredProcedure;
                da.SelectCommand.Parameters.AddWithValue("@statement", strQuery);

                da.Fill(dtQueryResult);

                dtSavedResult = dtQueryResult.Clone();
                dtSavedResult = dtQueryResult.Copy();


                //컬럼매핑때문에 주석 처리해봄
                DataColumnCollection columns = dtQueryResult.Columns;

                if (columns.Contains("ServerNum") && columns.Contains("RegDate") && columns.Contains("TimeIn"))
                {
                    for (int i = 0; i < dtQueryResult.Rows.Count; i++)
                    {
                        dtQueryResult.Rows[i].BeginEdit();
                        dtQueryResult.Rows[i]["ServerNum"] = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

                        if (iQueryID > 3)        // Queries for bulk insert.
                            dtQueryResult.Rows[i]["RegDate"] = DBNull.Value;

                        dtQueryResult.Rows[i]["TimeIn"] = dtTimeIn.ToString("yyyy-MM-dd HH:mm:ss");
                        dtQueryResult.Rows[i].EndEdit();
                        dtQueryResult.AcceptChanges();
                    }
                }

                if (dtQueryResult.Rows.Count > 0)
                {
                    
                    if (iQueryID == 2 || iQueryID == 3)
                    {
                        dtSavedResult.Clear();
                        dtSavedResult = dtQueryResult.Clone();

                        if (IsSQLAgentStatusOK(dtQueryResult, iQueryID))
                        {
                            dtQueryResult.Clear();
                            return;
                        }
                    }

                    //if (dtSavedResult.Rows.Count > 0)
                    //{
                    //    InsertQueryResult(dtSavedResult, strDestTable, strSPName);
                    //    dtQueryResult.Clear();
                    //}

                    //if (iQueryID >= 4)           // Custom Queries to insert BULK.
                    //{
                    //    InsertQueryResult(dtQueryResult, strDestTable, strSPName);
                    //    dtQueryResult.Clear();
                    //}

                    if (iQueryID == 2)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLJobAgentFailCheck(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 3)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLJobStatusCheck(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 4)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLLinkedCheck(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 5)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLErrorlog(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 6)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLTableSize(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 7)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLBlock(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 8)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLObjectCheck(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 9)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLDatabase(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 11)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLIndexDuplication(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 12)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLServerInfo(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 13)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLServiceStatus(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 14)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLConfiguration(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 15)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLDataBaseFileSize(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 16)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLSession(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 17)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLIndexFlagment(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }

                    if (iQueryID == 18)           // Custom Queries to insert BULK.
                    {
                        wsInsert.SQLAgentErrorlog(DataTableToBytes(dtSavedResult), iServerNumber, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_TimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                        dtQueryResult.Clear();
                    }





                }
            }
            catch (Exception e)
            {
                WSPEvent.WriteEvent("Running SQL Monitoring queries has failed. in this local database. - " + iQueryID.ToString() + ":" + e.Message, "W", 1131);
            }
            finally
            {
                con1.Close();
            }
        }

    }

    private bool IsSQLAgentStatusOK(DataTable dtResult, int iQueryID)
    {
        bool bIsOk = true;
        int iIndex = 0;

        if (iQueryID == 2)                      // List of failed jobs
        {
            if (bFirstRun_Q2)
            {                
                bFirstRun_Q2 = false;
                foreach (DataRow dr in dtResult.Rows)
                {
                    stJobs[iIndex].strJobName = dr["JOB_NAME"].ToString();
                    
                    stJobs[iIndex].iJobID = Convert.ToInt32(dr["JOB_HISTORY_ID"].ToString());
                    iIndex++;
                }

                return true;
                
            }

            if (dtResult.Rows.Count == 0)
                return true;

            foreach (DataRow dr in dtResult.Rows)
            {
                if (!HasAlertedJobs(dr))              // Failed Job found, and it's not alerted yet.
                {
                    AlertFailedSQLAgentJob(dr);
                    bIsOk = false;
                }
            }

            // Save current status
            iIndex = 0;
            foreach (DataRow dr in dtResult.Rows)
            {
                stJobs[iIndex].strJobName = dr["JOB_NAME"].ToString();
                stJobs[iIndex].iJobID = Convert.ToInt32(dr["JOB_HISTORY_ID"].ToString());
                iIndex++;
            }
            stJobs[iIndex].iJobID = 0;
        }

        if (iQueryID == 3)
        {
            if (bFirstRun_Q3)      // No saved data to compare
            {
                bFirstRun_Q3 = false;
                foreach (DataRow dr in dtResult.Rows)
                {
                    stRegistedJobs[iIndex].strJobName = dr["JOB_NAME"].ToString();
                    stRegistedJobs[iIndex].iEnabled = Convert.ToInt16(dr["enabled"].ToString());
                    iIndex++;
                }
                return true;
            }

            if (IsJobRemoved(dtResult))
            {
                bIsOk = false;
            }

            foreach (DataRow dr in dtResult.Rows)
            {
                if (HasStatusChage(dr))
                {
                    bIsOk = false;
                }
            }

            foreach (DataRow dr in dtResult.Rows)
            {
                stRegistedJobs[iIndex].strJobName = dr["JOB_NAME"].ToString();
                stRegistedJobs[iIndex].iEnabled = Convert.ToInt16(dr["enabled"].ToString());
                iIndex++;
            }
            stRegistedJobs[iIndex].strJobName = "";     //To support reverse seach
        }        

        return bIsOk;
    }

    private void AlertFailedSQLAgentJob(DataRow dr)
    {
        dtSavedResult.ImportRow(dr);

        string strAlertDesc = "";
        int iAlertLevel = 0;

        for (int i = 0; i < 5; i++)
        {
            if (stSQLAlerts[i].strReasonCode == "R163")          // alert job fail
            {
                strAlertDesc = stSQLAlerts[i].strReasonCodeDesc;
                if (stSQLAlerts[i].strAlertLevel.ToUpper() == "INFORMATION")
                    iAlertLevel = 1;
                if (stSQLAlerts[i].strAlertLevel.ToUpper() == "WARNING")
                    iAlertLevel = 2;
                if (stSQLAlerts[i].strAlertLevel.ToUpper() == "CRITICAL")
                    iAlertLevel = 3;
            }
        }

        dtAlerts.Rows.Add("R163", g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_SharedData.WSP_AGENT_SETTING.strDisplayName, dr["JOB_NAME"].ToString(), 0, iAlertLevel, strAlertDesc, g_sharedGUID);
    }

    /*
        dtAlerts.Columns.Add(new DataColumn("ReasonCode", typeof(string)));
        dtAlerts.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
        dtAlerts.Columns.Add(new DataColumn("Hostname", typeof(string)));
        dtAlerts.Columns.Add(new DataColumn("InstanceName", typeof(string)));
        dtAlerts.Columns.Add(new DataColumn("PValue", typeof(float)));
        dtAlerts.Columns.Add(new DataColumn("AlertStatus", typeof(int)));                   //reserved field for future purpose.
        dtAlerts.Columns.Add(new DataColumn("AlertDescription", typeof(string)));
        dtAlerts.Columns.Add(new DataColumn("AlertRecordID", typeof(Guid)));
    */

    private void AlertJobStatusChange(DataRow dr, string strReasonCode, string strJobName)
    {

        if (strReasonCode == "R167")            // deleted job : dr=null
            dtSavedResult.Rows.Add(g_TimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, strJobName, 4);
        else
        {
            if (strReasonCode == "R166")        // added job
            {
                dtSavedResult.Rows.Add(g_TimeIn, g_SharedData.WSP_AGENT_SETTING.iServerNumber, strJobName, 3);
                dtSavedResult.ImportRow(dr);
            }
            else
                dtSavedResult.ImportRow(dr);
        }

        string strAlertDesc = "";
        int iAlertLevel = 0;
        int iR164Level = 0;
        int iR165Level = 0;
        string strR164 = "";
        string strR165 = "";

        for (int i = 0; i < 5; i++)
        {
            if (stSQLAlerts[i].strReasonCode == strReasonCode)          // alert job fail
            {
                strAlertDesc = stSQLAlerts[i].strReasonCodeDesc;
                iAlertLevel = GetAlertLevel(stSQLAlerts[i].strAlertLevel);
            }

            if (stSQLAlerts[i].strReasonCode == "R164")
            {
                strR164 = stSQLAlerts[i].strReasonCodeDesc;
                iR164Level = GetAlertLevel(stSQLAlerts[i].strAlertLevel);
            }
            if (stSQLAlerts[i].strReasonCode == "R165")
            {
                strR165 = stSQLAlerts[i].strReasonCodeDesc;
                iR165Level = GetAlertLevel(stSQLAlerts[i].strAlertLevel);
            }
        }

        dtAlerts.Rows.Add(strReasonCode, g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_SharedData.WSP_AGENT_SETTING.strDisplayName, strJobName, 0, iAlertLevel, strAlertDesc, g_sharedGUID);
        if (strReasonCode == "R166")
        {
            if(Convert.ToInt16(dr["enabled"].ToString()) == 0)
                dtAlerts.Rows.Add("R165", g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_SharedData.WSP_AGENT_SETTING.strDisplayName, strJobName, 0, iR165Level, strR165, g_sharedGUID);
            else
                dtAlerts.Rows.Add("R164", g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_SharedData.WSP_AGENT_SETTING.strDisplayName, strJobName, 0, iR164Level, strR164, g_sharedGUID);
        }

    }

    private int GetAlertLevel(string strAlertLevel)
    {
        int iAlertLevel = 0;

        if (strAlertLevel.ToUpper() == "INFORMATION")
            iAlertLevel = 1;
        if (strAlertLevel.ToUpper() == "WARNING")
            iAlertLevel = 2;
        if (strAlertLevel.ToUpper() == "CRITICAL")
            iAlertLevel = 3;

        return iAlertLevel;
    }


    private bool IsJobRemoved(DataTable dtResult)
    {
        string strJobName = "";
        bool bExist = false;
        int iRemovedJobs = 0;

        for (int i = 0; i < MAX_JOBS; i ++)
        {
            if (!string.IsNullOrEmpty(stRegistedJobs[i].strJobName))        //nothing to compare
            {
                bExist = false;    

                strJobName = stRegistedJobs[i].strJobName;
                foreach (DataRow dr in dtResult.Rows)
                {
                    if (dr["JOB_NAME"].ToString() == strJobName)            // Job exists
                        bExist = true;
                }
                if (!bExist)                                                // Job disappeared
                {
                    //AlertJobStatusChange(null);
                    iRemovedJobs++;
                    AlertJobStatusChange(null, "R167", stRegistedJobs[i].strJobName);                    
                }
            }
            else
            {
                break;
            }
        }

        if (iRemovedJobs > 0)
            return true;
        else
            return false;
    }

    private bool HasStatusChage(DataRow dr)
    {
        bool bChanged = false;
        bool bExist = false;

        string strJobName = dr["JOB_NAME"].ToString();
        Int16 iEnabled = Convert.ToInt16(dr["enabled"].ToString());

        for (int i = 0; i < MAX_JOBS; i++)
        {
            if (strJobName == stRegistedJobs[i].strJobName)
            {
                if (iEnabled != stRegistedJobs[i].iEnabled)         // Job Enabled has been changed.
                {
                    bChanged = true;
                    if (iEnabled > 0)
                        AlertJobStatusChange(dr, "R164", strJobName);       // Disabled to Enabled
                    else
                        AlertJobStatusChange(dr, "R165", strJobName);
                }
                
                bExist = true;                  //exists in saved list.
            }

            if(string.IsNullOrEmpty(stRegistedJobs[i].strJobName))
                break;
        }

        if (!bExist)                        // Job has been added.
        {
            AlertJobStatusChange(dr, "R166", strJobName);            
            return true;
        }
       
        return bChanged;
    }

    private bool HasAlertedJobs(DataRow dr)
    {
        int iJobID = 0;
        string strJobName = "";

        iJobID = Convert.ToInt32(dr["JOB_HISTORY_ID"].ToString());
        strJobName = dr["JOB_NAME"].ToString();

        for (int i = 0; i < MAX_JOBS; i++)
        {
            if (iJobID == stJobs[i].iJobID && strJobName == stJobs[i].strJobName)
                return true;

            if (stJobs[i].iJobID == 0)
                break;
        }

        return false;
    }

    public void InsertQueryResult(DataTable dtResult, string strDestTable, string strSPName)
    {

        WSP.Console.WS_DBOP.GeneralDBOp wsSend = new WSP.Console.WS_DBOP.GeneralDBOp();
        wsSend.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/GeneralDBOp.asmx";
        wsSend.Timeout = 10000;

        try
        {
            int iResult = 0;

            //v3. strDestTable 이 NULL이면 SP 호출이고, 아니면 Ad Hoc Insert이다.
            if (!string.IsNullOrEmpty(strDestTable))
            {
                iResult = wsSend.SQLMonitoringDataInsert(g_SharedData.WSP_AGENT_SETTING.iServerNumber, strDestTable, DataTableToBytes(dtResult), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), strColsToInsert);
                iResult = wsSend.CopyTable(g_SharedData.WSP_AGENT_SETTING.iServerNumber, strDestTable, DataTableToBytes(dtResult));


            }
            else
                iResult = wsSend.InsertQueryResultUsingSP(g_SharedData.WSP_AGENT_SETTING.iServerNumber, strSPName, DataTableToBytes(dtResult), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent("Service Point failed to insert result of monitoring queries. - (" + strDestTable + ")" + ex.Message, "E", 1129);
        }

    }

    public void ResetConnectionPool()
    {
        try
        {
            SqlConnection.ClearAllPools();
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent("WSP failed to reset all DB connections. - " + ex.Message, "E", 1136);
        }
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

    private DataTable SetParmeterTable()
    {
        DataTable dtParms = new DataTable();

        dtParms.Columns.Add(new DataColumn("SERVER_NUMBER", typeof(int)));
        dtParms.Columns.Add(new DataColumn("PARM_NAME", typeof(string)));
        dtParms.Columns.Add(new DataColumn("DATA_TYPE", typeof(string)));
        dtParms.Columns.Add(new DataColumn("DATA_VALUE", typeof(string)));

        return dtParms;
    }


}

