
using System;
using System.Data;
using System.Diagnostics;
using System.Data.SqlClient;
using System.IO;

namespace RulesAlerts
{

    class RulesAndAlerts
    {
        #region

        //get
        //public DataTable dtPCID = new DataTable();
        public DataTable dtPValues = new DataTable();
        public DataTable dtAlertRules = new DataTable();
                
        public DataTable dtSQLHBAlerts = new DataTable();
        public Guid g_guidPerfCollectingTurn = new Guid();
        public DateTime g_TimeIn = new DateTime();
        public DateTime g_TimeIn_UTC = new DateTime();

        public DataTable dtSavedWPList = new DataTable();
        public DataTable dtBTSProcess = new DataTable();

        public SharedData g_SharedData = new SharedData();
        private DataAccess.WSP_Op WSPDB = new DataAccess.WSP_Op();
        
        //public DateTime dtLastUpdatedTime;

        //set
        public DataTable dtAlerts = new DataTable();
        public DataTable dtSavedPValues = new DataTable();
        public DataTable dtAppTrace = new DataTable();
        public DataTable dtQueryMonitorAlerts = new DataTable();

        public int g_iSQLMaxAlertLevel = 0;
        public int g_iWebMaxAlertLevel = 0;
        
        public float flMemoryDelta = 0;
        public int iMaxDuration;
        public bool bCheckProcess = true;
        public bool bFirstAlertCheck = true;
        EventRecorder WSPEvent = new EventRecorder();

        private IISRulesAlerts IISChk = new IISRulesAlerts();
        private BizTalkRulesAlerts BTSChk = new BizTalkRulesAlerts();
        private SQLRulesAlerts SQLChk = new SQLRulesAlerts();

       
        private struct RuleRecord
        {
            public string strServerType;
            public string strInstanceName;
            public string strReasonCode;
            public string strPCID;
            public string strReasonDescription;
            public bool bRecordApp;
            public int iDuration;
            public int iOperator;
            public float flThreshold;
            public bool bHasReference;
            public int iAlertLevel;
            public bool bIsEnabled;
        }

        private RuleRecord CurrentRule = new RuleRecord();

        #endregion

        public void InitializeAlertRules()
        {            
            dtSavedPValues.Columns.Clear();
            dtSavedPValues.Columns.Add(new DataColumn("PCID", typeof(string)));
            dtSavedPValues.Columns.Add(new DataColumn("HOSTNAME", typeof(string)));
            dtSavedPValues.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
            dtSavedPValues.Columns.Add(new DataColumn("PValue", typeof(float)));
            dtSavedPValues.Columns.Add(new DataColumn("RValue", typeof(string)));
            dtSavedPValues.Columns.Add(new DataColumn("InstanceName", typeof(string)));
            
            dtAlerts.Columns.Clear();
            dtAlerts.Columns.Add(new DataColumn("ReasonCode", typeof(string)));
            dtAlerts.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
            dtAlerts.Columns.Add(new DataColumn("Hostname", typeof(string)));
            dtAlerts.Columns.Add(new DataColumn("InstanceName", typeof(string)));
            dtAlerts.Columns.Add(new DataColumn("PValue", typeof(float)));
            dtAlerts.Columns.Add(new DataColumn("AlertStatus", typeof(int))); 
            dtAlerts.Columns.Add(new DataColumn("AlertDescription", typeof(string)));
            dtAlerts.Columns.Add(new DataColumn("AlertRecordID", typeof(Guid)));

            dtAppTrace.Columns.Clear();
            //dtAppTrace.Columns.Add(new DataColumn("ServerNum", typeof(int)));
            dtAppTrace.Columns.Add(new DataColumn("AlertRecordID", typeof(Guid)));
            dtAppTrace.Columns.Add(new DataColumn("Hostname", typeof(string)));
            dtAppTrace.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
            dtAppTrace.Columns.Add(new DataColumn("ReasonCode", typeof(string)));
            dtAppTrace.Columns.Add(new DataColumn("URI", typeof(string)));
            dtAppTrace.Columns.Add(new DataColumn("ClientLocation", typeof(string)));
            dtAppTrace.Columns.Add(new DataColumn("RunningTime", typeof(int)));

            if (dtAlertRules.Rows.Count > 0)
            {
                var maxRow = dtAlertRules.Select("Duration = MAX(Duration)");
                if (maxRow.Length > 0)
                {
                    foreach (var arow in maxRow)
                        iMaxDuration = Convert.ToInt32(arow["Duration"]);
                }
            }

            WSPDB.g_SharedData = g_SharedData;

            //WSPDB.strConnectionString = strConnectionString;            
            //WSPDB.strHostname = SysInfo.strMachineName;
            //WSPDB.strDisplayName = strDisplayName;
            //WSPDB.strServerGroup = strServerGroup;
            //WSPDB.strServerType = strServerType;

            WSPDB.dtAlertRules = dtAlertRules.Copy();
            IISChk.dtAlerts = dtAlerts.Copy(); // To initialize IIS Alerts DataTable
            IISChk.dtAppTrace = dtAppTrace.Clone();
            BTSChk.dtAlerts = dtAlerts.Copy();
            dtQueryMonitorAlerts = dtAlerts.Clone();

            //SQLChk.dtAlerts = dtAlerts.Copy(); 
        }

        public void CheckAlerts(DateTime g_TimeIn, DateTime g_TimeIn_UTC)
        {
            //WSPDB.strServerGroup = strServerGroup;     // To support configuration file changes.
            //WSPDB.strServerType = strServerType;
            //WSPDB.strDisplayName = strDisplayName;
            //WSPDB.iServerNumber = iServerNumber;

            WSPDB.g_SharedData = g_SharedData;                        

            BuildPerformanceLog(); // To Save PValues in a table - dtSavedPValues

            if (g_SharedData.WSP_AGENT_SETTING.strServerType.Contains("Web"))
            {
                IISChk.g_SharedData = g_SharedData;
                IISChk.dtmPValueTimeIn = g_TimeIn;

                if (IISChk.dtAlerts.Rows.Count > 0) //Clear saved IIS Alerts before scanning.
                    IISChk.dtAlerts.Rows.Clear();

                IISChk.dtSavedPValues = dtSavedPValues.Copy();
                IISChk.dtLastUpdatedTime = g_TimeIn;
                IISChk.g_guidPerfCollectingTurn = g_guidPerfCollectingTurn;
            }

            if (g_SharedData.WSP_AGENT_SETTING.strServerType.Contains("SQL"))
            {
                SQLChk.g_SharedData = g_SharedData;
                SQLChk.g_iSQLAlertCount = 0;

                SQLChk.dtmPValueTimeIn = g_TimeIn;                
                SQLChk.dtLastUpdatedTime = g_TimeIn;
                SQLChk.g_guidPerfCollectingTurn = g_guidPerfCollectingTurn; 

                SQLChk.dtSavedPValues= dtSavedPValues.Copy();
                SQLChk.iPerfLogCollectInterval = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval;
                SQLChk.strMachineName = g_SharedData.SYSINFO.strComputerName;
                SQLChk.strDisplayName = g_SharedData.WSP_AGENT_SETTING.strDisplayName;
                SQLChk.dtCurrentPValues = dtPValues.Copy();
            }

            foreach (DataRow dr in dtAlertRules.Rows)
            {
                GetCurrentRuleRecord(dr);       //Copy current rule record to CurrentRule struct.

                if (CurrentRule.bIsEnabled)    // if this rule is enabled
                {
                    if (CurrentRule.strServerType == "Windows")          
                    {
                        if (CurrentRule.iOperator == 0 || CurrentRule.bHasReference)   // if it's custom rule, or it uses reference value
                            CheckCustomRules();
                        else
                            CheckNormalRules();
                    }

                    if (CurrentRule.strServerType == "Web" && (g_SharedData.WSP_AGENT_SETTING.strServerType == "Web")) // if server type is matched. Server Type in Rules == Server Type
                    {
                        
                        if (CurrentRule.iOperator == 0 || CurrentRule.bHasReference)                        
                            IISChk.CheckCustomRules(dr);                                                    
                        else
                            CheckNormalRules();
                    }

                    //if (CurrentRule.strServerType == "BizTalk" && g_SharedData.WSP_AGENT_SETTING.strServerType == "BizTalk")
                    //{
                    //    if (CurrentRule.iOperator == 0 || CurrentRule.bHasReference)
                    //        BTSChk.CheckCustomRules(dr);
                    //    else
                    //    {
                    //        CheckNormalRules();
                    //    }
                    //}

                    if (CurrentRule.strServerType == "SQL" && g_SharedData.WSP_AGENT_SETTING.strServerType == "SQL")
                    {
                        if (CurrentRule.iOperator == 0 || CurrentRule.bHasReference)
                            SQLChk.CheckCustomRules(dr);
                        else
                        {
                            CheckNormalRules();
                        }
                    }
                }

                if (CurrentRule.strReasonCode == "R035" && !CurrentRule.bIsEnabled)
                    bCheckProcess = false;
            }

            bFirstAlertCheck = false;

            if (IISChk.dtAlerts.Rows.Count > 0)     // If IIS Rules has alerts
                dtAlerts.Merge(IISChk.dtAlerts);

            //if (BTSChk.dtAlerts.Rows.Count > 0)     
            //    dtAlerts.Merge(BTSChk.dtAlerts);

            if (g_SharedData.WSP_AGENT_SETTING.strServerType == "SQL")
            {
                if (SQLChk.g_iSQLAlertCount > 0)
                    MergeSQLAlerts();

                if (dtSQLHBAlerts.Rows.Count > 0)
                {
                    dtAlerts.Merge(dtSQLHBAlerts);
                    dtSQLHBAlerts.Clear();
                }

                if (dtQueryMonitorAlerts.Rows.Count > 0)
                {
                    dtAlerts.Merge(dtQueryMonitorAlerts);
                    dtQueryMonitorAlerts.Clear();
                }
            }

            if (dtAlerts.Rows.Count > 0)              // If there's any alerts
            {
                //WSPDB.dtToBeAlerted.Rows.Clear();
                //WSPDB.dtToBeAlerted = dtAlerts.Copy();

                WSPDB.g_dtTimeIn = g_TimeIn;
                WSPDB.g_dtTimeIn_UTC = g_TimeIn_UTC;
                WSPDB.InsertAlertsToDB(dtAlerts);                       
            }

            if (IISChk.dtAppTrace.Rows.Count > 0)           //If there's any Apps to record in IIS Rules
            {
                dtAppTrace.Merge(IISChk.dtAppTrace);
                //foreach (DataRow drApp in IISChk.dtAppTrace.Rows)
                //    dtAppTrace.ImportRow(drApp);
            }

            if (dtAppTrace.Rows.Count > 0)                      ////If there's any Apps to record
            {
                //WSPDB.dtAppTrace.Rows.Clear();
                //WSPDB.dtAppTrace = dtAppTrace.Copy();
                WSPDB.InsertAppsToDB(dtAppTrace);
                dtAppTrace.Clear();
                IISChk.dtAppTrace.Clear();
            }

            WSPDB.g_iSQLMaxAlertLevel = g_iSQLMaxAlertLevel;
            WSPDB.g_iWebMaxAlertLevel = g_iWebMaxAlertLevel;

            WSPDB.UpdateHostPerfStatus(dtAlerts, g_TimeIn, g_TimeIn_UTC);
            dtAlerts.Rows.Clear();     
            //WSPDB.dtToBeAlerted.Rows.Clear();          //Clear alerts to be updated, after alerting in DB.                
        }

        private void MergeSQLAlerts()
        {
            for (int i = 0; i < SQLChk.g_iSQLAlertCount; i++)
            {
                dtAlerts.Rows.Add(SQLChk.g_stSQLAlerts[i].strReasonCode, SQLChk.g_stSQLAlerts[i].tmNow.ToString("yyyy-MM-dd HH:mm:ss"), SQLChk.g_stSQLAlerts[i].strDisplayName, SQLChk.g_stSQLAlerts[i].strInstanceName, SQLChk.g_stSQLAlerts[i].PValue, SQLChk.g_stSQLAlerts[i].iAlertStatus, SQLChk.g_stSQLAlerts[i].strAlertDesc, SQLChk.g_stSQLAlerts[i].strGUID);
            }

            SQLChk.g_iSQLAlertCount = 0;
        }

        private void GetCurrentRuleRecord(DataRow dr)
        {
            string strAlertLevel = "Information";

            CurrentRule.strServerType = dr["ServerType"].ToString();
            CurrentRule.strPCID = dr["PCID"].ToString();
            CurrentRule.flThreshold = (float)Convert.ToSingle(dr["Threshold"]);
            CurrentRule.iOperator = Convert.ToInt32(dr["TOperator"]);
            CurrentRule.iDuration = Convert.ToInt32(dr["Duration"]);
            CurrentRule.strInstanceName = dr["InstanceName"].ToString();
            CurrentRule.bRecordApp = Convert.ToBoolean(dr["RecordApps"]);
            CurrentRule.strReasonCode = dr["ReasonCode"].ToString();
            CurrentRule.strReasonDescription = dr["ReasonCodeDesc"].ToString();
            CurrentRule.bHasReference = Convert.ToBoolean(dr["HasReference"]);
            CurrentRule.bIsEnabled = Convert.ToBoolean(dr["IsEnabled"]);            
            
            strAlertLevel = dr["AlertLevel"].ToString();
            CurrentRule.iAlertLevel = 0;

            if (strAlertLevel == "Information")
                CurrentRule.iAlertLevel = 1;
            if (strAlertLevel == "Warning")
                CurrentRule.iAlertLevel = 2;
            if (strAlertLevel == "Critical")
                CurrentRule.iAlertLevel = 3;

            ////////////////////////////////////////////////////////////////////////////////////////////////
            // To Adjust Threshold (by #CPU)
            ////////////////////////////////////////////////////////////////////////////////////////////////

            if (CurrentRule.strPCID == "P006")  // Process, %Processor Time
            {
                if(g_SharedData.SYSINFO.iNumberOfProcessors > 0)
                    CurrentRule.flThreshold = CurrentRule.flThreshold * g_SharedData.SYSINFO.iNumberOfProcessors;
            }
            
        }

        private void CheckNormalRules()   // Normal operator, & no reference, Server Type has been checked.
        {
               
            int iDuration = CurrentRule.iDuration;
            bool bMultipleInstances = false;
            int iIndex = 0;

            if (g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval >= iDuration && iDuration != 0)
                iDuration = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval;

            if (iDuration == 0)
                iDuration = 1;

            if (CurrentRule.iOperator == 4)     // Threshold - checking changes of values.
                iDuration = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval;

            DateTime tmNow = new DateTime();
            tmNow = DateTime.Now;
            DateTime tmDuration = g_TimeIn.AddSeconds(Convert.ToDouble((iDuration) * (-1)));
            
            TimeSpan tsGap = tmDuration - g_TimeIn;
            if (tsGap.Seconds >= 0 )             //  if tmDuraion is greater than PValue TimeIn
            {
                tmDuration = g_TimeIn.AddSeconds(-1);
            }


            System.Data.DataRow[] rows1;

            string strQuery;
            if (CurrentRule.strInstanceName.Length > 0)
            {
                if (CurrentRule.strInstanceName == "All Instances")
                {
                    strQuery = "PCID = '" + CurrentRule.strPCID + "'";
                    bMultipleInstances = true;
                }
                else
                    strQuery = "PCID = '" + CurrentRule.strPCID + "' AND InstanceName = '" + CurrentRule.strInstanceName + "'";
            }
            else
                strQuery = "PCID = '" + CurrentRule.strPCID + "'";

            strQuery = strQuery + " AND TimeIn >= '" + tmDuration + "'";

            rows1 = dtSavedPValues.Select(strQuery);

            DataTable dtAnalysis = new DataTable();
            dtAnalysis = dtSavedPValues.Clone();
            dtAnalysis.Rows.Clear();

            foreach (var row1 in rows1)
            {
                DataRow newRow = dtAnalysis.NewRow();
                newRow["InstanceName"] = row1["InstanceName"];
                newRow["PValue"] = row1["PValue"];
                newRow["RValue"] = row1["RValue"];
                dtAnalysis.Rows.Add(newRow);
            }

            if (bMultipleInstances)
            {
                DataTable dtInstanceList = new DataTable();
                dtInstanceList = dtAnalysis.DefaultView.ToTable(true, "InstanceName");

                foreach (DataRow dr2 in dtInstanceList.Rows)
                {
                    strQuery = "InstanceName = '" + dr2["InstanceName"].ToString() + "'";
                    var varComp1 = dtAnalysis.Select(strQuery); // Comp1 should use instance name only without threshold value.

                    if (CurrentRule.iOperator == 1)
                        strQuery = "PValue > " + CurrentRule.flThreshold.ToString() + " AND InstanceName = '" + dr2[0].ToString() + "'";

                    if (CurrentRule.iOperator == 2)
                        strQuery = "PValue < " + CurrentRule.flThreshold.ToString() + " AND InstanceName = '" + dr2[0].ToString() + "'";

                    if (CurrentRule.iOperator == 3)
                        strQuery = "PValue = " + CurrentRule.flThreshold.ToString() + " AND InstanceName = '" + dr2[0].ToString() + "'";

                    if (CurrentRule.iOperator == 4)
                        return;               // reserved

                    var varComp2 = dtAnalysis.Select(strQuery);

                    if (varComp1.Length == varComp2.Length && varComp2.Length != 0)
                    {
                        iIndex = varComp2.Length - 1;
                        float flPValue = (float)Convert.ToSingle(varComp2[iIndex]["PValue"]);
                        string strRValue = varComp2[iIndex]["RValue"].ToString();
                        AddThisAlert(flPValue, dr2["InstanceName"].ToString(), strRValue);
                    }
                }
            }
            else
            {
                if (CurrentRule.iOperator == 1)
                    strQuery = "PValue > " + CurrentRule.flThreshold.ToString();

                if (CurrentRule.iOperator == 2)
                    strQuery = "PValue < " + CurrentRule.flThreshold.ToString();

                if (CurrentRule.iOperator == 3)
                    strQuery = "PValue = " + CurrentRule.flThreshold.ToString();

                if (CurrentRule.iOperator == 4)
                {
                    int iPrevValue = -1;
                    foreach (DataRow drPvalue in dtAnalysis.Rows)
                    {
                        if (iPrevValue > 0)
                        {
                            if (iPrevValue != Convert.ToInt32(drPvalue["PValue"]))
                                AddThisAlert(Convert.ToSingle(drPvalue["PValue"]), CurrentRule.strInstanceName, drPvalue["RValue"].ToString());
                        }
                        iPrevValue = Convert.ToInt32(drPvalue["PValue"]);
                    }
                }

                var varComp2 = dtAnalysis.Select(strQuery);       //dtAnalysis -> all samples.

                if (dtAnalysis.Rows.Count == varComp2.Length && varComp2.Length != 0)
                {
                    iIndex = varComp2.Length - 1;
                    float flPValue = (float)Convert.ToSingle(varComp2[iIndex]["PValue"]);
                    string strRValue = varComp2[iIndex]["RValue"].ToString();
                    AddThisAlert(flPValue, CurrentRule.strInstanceName, strRValue);
                }
            }

        }

        private void CheckCustomRules()  
        {
            string strReasonCode = CurrentRule.strReasonCode;
            int iDuration = CurrentRule.iDuration;
            int iOperator = CurrentRule.iOperator;

            if (g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval >= iDuration && iDuration != 0)
                iDuration = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval;

            if (iDuration == 0)
                iDuration = 1;

            if (iOperator == 4)                            // check value chages
                iDuration = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval;

            DateTime tmNow = new DateTime();
            tmNow = DateTime.Now;
            //DateTime tmDuration = DateTime.Now.Subtract(TimeSpan.FromSeconds(iDuration + iPerfLogCollectInterval / 2));
            DateTime tmDuration = g_TimeIn.AddSeconds(Convert.ToDouble((iDuration) * (-1)));

            TimeSpan tsGap = tmDuration - g_TimeIn;
            if (tsGap.Seconds >= 0)             //  if tmDuraion is greater than PValue TimeIn
            {
                tmDuration = g_TimeIn.AddSeconds(-1);
            }

            if (strReasonCode == "R003")
                R003_Rule(tmDuration);

            if (strReasonCode == "R006" || strReasonCode == "R007") // Exessive committed memory size(90% of RAM)
                R006_R009_Rule(tmDuration);

            if (strReasonCode == "R010" || strReasonCode == "R011")
                R010_R011_Rule(tmDuration);

            if (strReasonCode == "R019")         // chking 70% of network bytes/sec
                R019_Rule(tmDuration);

        }

  
  
        public void CheckProcessMemory(DataTable dtW3WPMemory)
        {

            if (flMemoryDelta < (float)1000000)         // 20MB by default
                flMemoryDelta = (float)20000000;

            float flPValue1 = 0;
            float flPValue2 = 0;
            float flBaseProcessMemory = (float)250000000;         // 250MB, to start monitoring for process memory.

            DataTable dtProcessList = new DataTable();
            System.Data.DataRow[] rowsProcess;

            string strR035Desc = "";
            string strR046Desc = "";
            bool bIsR035Enabled = false;
            bool bIsR046Enabled = false;

            DataRow[] drRules = dtAlertRules.Select("ReasonCode IN ('R035', 'R046')");
            foreach(DataRow drDesc in drRules)
            {
                if (drDesc["ReasonCode"].ToString() == "R035")
                {
                    strR035Desc = drDesc["ReasonCodeDesc"].ToString();
                    bIsR035Enabled = Convert.ToBoolean(drDesc["IsEnabled"]);
                }

                if (drDesc["ReasonCode"].ToString() == "R046")
                {
                    strR046Desc = drDesc["ReasonCodeDesc"].ToString();
                    bIsR046Enabled = Convert.ToBoolean(drDesc["IsEnabled"]);
                }
            }

            if (!bIsR035Enabled && !bIsR046Enabled)
                return;

            try
            {
                if (dtW3WPMemory.Rows.Count < 1)
                    return;

                dtProcessList = dtW3WPMemory.DefaultView.ToTable(true, "InstanceName");

                foreach (DataRow drProcess in dtProcessList.Rows)
                {
                    string strProcess = drProcess["InstanceName"].ToString();
                    string strQuery = "InstanceName = '" + strProcess + "'";

                    rowsProcess = dtW3WPMemory.Select(strQuery);
                    if (rowsProcess.Length < 1)
                        return;

                    foreach (DataRow drW3wp in rowsProcess)
                    {
                        flPValue2 = (float)Convert.ToSingle(drW3wp["PValue"]);

                        if (flPValue2 > flBaseProcessMemory && flPValue1 != 0)
                        {
                            float flChange = flPValue2 - flPValue1;
                            if (flChange > flMemoryDelta)
                            {
                                if (strProcess.Contains("w3wp"))
                                {
                                    string strAppPool = GetAppPoolDesc(strProcess);
                                    CurrentRule.strReasonCode = "R035";
                                    CurrentRule.strReasonDescription = strR035Desc;
                                    CurrentRule.bRecordApp = true;
                                    CurrentRule.strInstanceName = strAppPool;

                                    AddThisAlert(flPValue2, strAppPool, drW3wp["RValue"].ToString());
                                }
                                else
                                {
                                    CurrentRule.strReasonCode = "R046";
                                    CurrentRule.strReasonDescription = strR046Desc;
                                    CurrentRule.bRecordApp = false;
                                    string strPID = GetJavaPID(strProcess);
                                    AddThisAlert(flPValue2, strProcess, strPID);
                                }
                                return;        // to avoid noisy alerts continuosly, if true, clear data tables.
                            }
                        }

                        flPValue1 = flPValue2;
                    }

                    flPValue1 = 0;
                }
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "E", 1126);
            }

            return;
        }

        private string GetAppPoolDesc(string strInstance)
        {
            
            if (g_SharedData.WEBINFO.iNumberOfAppPools > 0)
            {
                for (int i = 0; i < g_SharedData.WEBINFO.iNumberOfAppPools; i++)
                {
                    if (g_SharedData.arrWPList[i].strInstanceName == strInstance)
                        return g_SharedData.arrWPList[i].strAppPoolDesc;
                }
            }

            return "";

            //if (dtWPList.Rows.Count > 0 && strInstance.Contains("w3wp"))
            //{
            //    var AppDesc = dtWPList.Select("InstanceName = '" + strInstance + "'");

            //    foreach (var row in AppDesc)
            //    {
            //        strAppDesc = "[" + row["PID"].ToString() + "]" + row["AppPoolDesc"].ToString();
            //    }
            //}

            //if (strAppDesc.Length < 1)
            //    return strInstance;
            //return strAppDesc;
        }

        private string GetJavaPID(string strInstance)
        {
            if (g_SharedData.WEBINFO.iNumberOfJavaPools > 0)
            {
                for (int i = 0; i < g_SharedData.WEBINFO.iNumberOfJavaPools; i++)
                {
                    if (g_SharedData.arrJavaWPList[i].strInstanceName == strInstance)
                        return g_SharedData.arrJavaWPList[i].strPID;
                }
            }

            return "";

            //string strPID = "";
            //strInstance = strInstance.ToLower();

            //if (dtJavaWP.Rows.Count > 0 && strInstance.Contains("java"))
            //{
            //    var AppDesc = dtJavaWP.Select("InstanceName = '" + strInstance + "'");

            //    foreach (var row in AppDesc)
            //    {
            //        strPID = "[" + row["PID"].ToString() + "]";
            //    }
            //}

            //return strPID;
        }
  
        private void R019_Rule(DateTime tmDuration)           // check bytes/total for each NI
        {

            bool bMultiInstances = false;

            if (CurrentRule.strInstanceName == "All Instances")
                bMultiInstances = true;

            DataTable dtAnalysis = new DataTable();
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);

            CheckNetworkThreshold(CurrentRule.flThreshold, dtAnalysis);
        }

        private void CheckNetworkThreshold(float flThreshold, DataTable dtAnalysis)
        {
            string strQuery = "";
            float flRValue = 100000000;

            DataTable dtInstanceList = new DataTable();
            dtInstanceList = dtAnalysis.DefaultView.ToTable(true, "InstanceName");

            foreach (DataRow dr2 in dtInstanceList.Rows)
            {
                strQuery = "InstanceName = '" + dr2["InstanceName"].ToString() + "'";
                var varComp1 = dtAnalysis.Select(strQuery);

                foreach (DataRow drR in dtAnalysis.Rows)
                {
                    if (drR["InstanceName"].ToString() == dr2["InstanceName"].ToString())
                    {
                        try
                        {
                            flRValue = (float)Convert.ToSingle(drR["RValue"]);
                        }
                        catch
                        {
                            flRValue = 100000000;
                        }
                        break;
                    }
                }

                float flNetworkThreshold = flRValue * (float)flThreshold;

                strQuery = "PValue > " + flNetworkThreshold.ToString() + " AND InstanceName = '" + dr2[0].ToString() + "'";

                var varComp2 = dtAnalysis.Select(strQuery);

                if (varComp1.Length == varComp2.Length && varComp2.Length != 0)
                {
                    int iIndex = 0;
                    iIndex = varComp2.Length - 1;
                    AddThisAlert(Convert.ToSingle(varComp2[iIndex]["PValue"]), CurrentRule.strInstanceName, "");
                }
                else
                    flRValue = 0;
            }

        }

   
        private void R010_R011_Rule(DateTime tmDuration)
        {
            if (g_SharedData.SYSINFO.bIs64bit)         // In 64bit Windows, any threshold of kernel memory is greater than system commit limit on physical memory.
                return;

            bool bMultiInstances = false;

            if (CurrentRule.strInstanceName == "All Instances")
                bMultiInstances = true;

            DataTable dtAnalysis = new DataTable();
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);

            int iOperator = 1;
            if (CurrentRule.strReasonCode == "R010")             // Paged Pool
                    CurrentRule.flThreshold = CurrentRule.flThreshold * (float)0.9;
            else                                                            // Nonpaged Pool, - R011
                    CurrentRule.flThreshold = CurrentRule.flThreshold * (float)0.9;

            CheckThreshold(bMultiInstances, iOperator, CurrentRule.flThreshold, dtAnalysis);
        }

        private void R006_R009_Rule(DateTime tmDuration)
        {
            bool bMultiInstances = false;

            if (CurrentRule.strInstanceName == "All Instances")
                bMultiInstances = true;

            DataTable dtAnalysis = new DataTable();
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);

            int iOperator = 1;
            if (CurrentRule.strReasonCode == "R006")
                CurrentRule.flThreshold = Convert.ToSingle(g_SharedData.SYSINFO.flRAMSize * 0.9);
            else
                CurrentRule.flThreshold = Convert.ToSingle(g_SharedData.SYSINFO.flRAMSize * 0.99);

            CurrentRule.flThreshold = (float)CurrentRule.flThreshold;

            CheckThreshold(bMultiInstances, iOperator, CurrentRule.flThreshold, dtAnalysis);
        }

        private void R003_Rule(DateTime tmDuration)     // processor queue length
        {
            bool bMultiInstances = false;

            if (CurrentRule.strInstanceName == "All Instances")
                bMultiInstances = true;

            DataTable dtAnalysis = new DataTable();
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);

            if (g_SharedData.SYSINFO.iNumberOfProcessors < 1)
                g_SharedData.SYSINFO.iNumberOfProcessors = 1;

            int iOperator = 1;

            CurrentRule.flThreshold = g_SharedData.SYSINFO.iNumberOfProcessors * CurrentRule.flThreshold;
            CheckThreshold(bMultiInstances, iOperator, CurrentRule.flThreshold, dtAnalysis);

        }

        // Build data table that includes only required data with PCID & its duration to analize
        private DataTable BuildAnalysisTable(string strInstanceName, string strPCID, DateTime tmDuration, bool bMultiInstances)
        {

            System.Data.DataRow[] rows1;

            string strQuery;
            if (strInstanceName.Length > 0)
            {
                if (strInstanceName == "All Instances")
                {
                    strQuery = "PCID = '" + strPCID + "'";
                    bMultiInstances = true;
                }
                else
                    strQuery = "PCID = '" + strPCID + "' AND InstanceName = '" + strInstanceName + "'";

                if (strInstanceName == "w3wp" || strInstanceName.Contains("w3wp#"))
                    strQuery = "PCID = '" + strPCID + "' AND InstanceName LIKE 'w3wp%'";

                if (strInstanceName == "java" || strInstanceName.Contains("java#"))
                    strQuery = "PCID = '" + strPCID + "' AND InstanceName LIKE 'java%'";

                if (strInstanceName == "javaw" || strInstanceName.Contains("javaw#"))
                    strQuery = "PCID = '" + strPCID + "' AND InstanceName LIKE 'javaw%'";
            }
            else
                strQuery = "PCID = '" + strPCID + "'";

            strQuery = strQuery + " AND TimeIn > '" + tmDuration + "'";

            rows1 = dtSavedPValues.Select(strQuery);

            DataTable dtAnalysis = new DataTable();
            dtAnalysis = dtSavedPValues.Clone();
            dtAnalysis.Rows.Clear();

            foreach (var row1 in rows1)
            {
                DataRow newRow = dtAnalysis.NewRow();
                newRow["InstanceName"] = row1["InstanceName"];
                newRow["PValue"] = row1["PValue"];
                newRow["RValue"] = row1["RValue"];
                dtAnalysis.Rows.Add(newRow);
            }

            return dtAnalysis;
        }

        private void CheckThreshold(bool bMultipleInstances, int iOperator, float flThreshold, DataTable dtAnalysis)
        {
            string strQuery = "";
            bool bRecordApps = CurrentRule.bRecordApp;

            if (bMultipleInstances)
            {
                DataTable dtInstanceList = new DataTable();
                dtInstanceList = dtAnalysis.DefaultView.ToTable(true, "InstanceName");

                foreach (DataRow dr2 in dtInstanceList.Rows)
                {
                    strQuery = "InstanceName = '" + dr2["InstanceName"].ToString() + "'";
                    var varComp1 = dtAnalysis.Select(strQuery);

                    if (iOperator == 1)
                        strQuery = "PValue > " + flThreshold.ToString() + " AND InstanceName = '" + dr2[0].ToString() + "'";

                    if (iOperator == 2)
                        strQuery = "PValue < " + flThreshold.ToString() + " AND InstanceName = '" + dr2[0].ToString() + "'";

                    if (iOperator == 3)
                        strQuery = "PValue = " + flThreshold.ToString() + " AND InstanceName = '" + dr2[0].ToString() + "'";

                    if (iOperator == 4)
                        return;                   // reserved

                    var varComp2 = dtAnalysis.Select(strQuery);

                    if (varComp1.Length == varComp2.Length && varComp2.Length != 0)
                    {
                        int iIndex = 0;
                        iIndex = varComp2.Length - 1;
                        AddThisAlert(Convert.ToSingle(varComp2[iIndex]["PValue"]), dr2["InstanceName"].ToString(), varComp2[iIndex]["RValue"].ToString());
                    }
                    else
                        strQuery = "";

                }
            }
            else
            {
                if (iOperator == 1)
                    strQuery = "PValue > " + flThreshold.ToString();

                if (iOperator == 2)
                    strQuery = "PValue < " + flThreshold.ToString();

                if (iOperator == 3)
                    strQuery = "PValue = " + flThreshold.ToString();

                if (iOperator == 4)
                {
                    int iPrevValue = -1;
                    foreach (DataRow drPvalue in dtAnalysis.Rows)
                    {
                        if (iPrevValue > 0)
                        {
                            if (iPrevValue != Convert.ToInt32(drPvalue["PValue"]))
                                AddThisAlert(iPrevValue, drPvalue["InstanceName"].ToString(), drPvalue["RValue"].ToString());
                        }
                        iPrevValue = Convert.ToInt32(drPvalue["PValue"]);
                    }
                }

                var varComp2 = dtAnalysis.Select(strQuery);       //dtAnalysis -> all samples

                if (dtAnalysis.Rows.Count == varComp2.Length && varComp2.Length != 0)
                {
                    int iIndex = 0;
                    iIndex = varComp2.Length - 1;
                    AddThisAlert(Convert.ToSingle(varComp2[iIndex]["PValue"]), varComp2[iIndex]["InstanceName"].ToString(), varComp2[iIndex]["RValue"].ToString());
                }
                else
                    strQuery = "";

            }
        }

        public void BuildPerformanceLog()
        {
            try
            {
                if (dtSavedPValues.Rows.Count > 0)
                {
                    DateTime tmNow = new DateTime();
                    tmNow = DateTime.Now;
                    //DateTime tmDuration = DateTime.Now.Subtract(TimeSpan.FromSeconds(iMaxDuration));
                    DateTime tmDuration = DateTime.Now.AddSeconds(Convert.ToDouble((iMaxDuration) * (-1)));

                    var rows = dtSavedPValues.Select("TimeIn < '" + tmDuration + "'");
                    foreach (var row in rows)
                        row.Delete();

                    dtSavedPValues.AcceptChanges();
                }

                dtSavedPValues.Merge(dtPValues);
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "E", 1120);
            }
        }

        private void AddThisAlert(float PValue, string strInstanceName, string strRefValue)
        {
            Guid guidNew = new Guid();
            guidNew = g_guidPerfCollectingTurn;
            DateTime tmNow = g_TimeIn;         
            
            string strAlertDesc = "";
            if (strInstanceName.Contains("w3wp") || strInstanceName.Contains("w3svc"))
                strAlertDesc = CurrentRule.strReasonDescription + "[" + strRefValue + "]";
            else
                strAlertDesc = CurrentRule.strReasonDescription;
            
            if (strInstanceName.Length >= 200)
                strInstanceName = strInstanceName.Substring(0, 199);

            if (strAlertDesc.Length >= 250)
                strAlertDesc = strAlertDesc.Substring(0, 254);

            dtAlerts.Rows.Add(CurrentRule.strReasonCode, tmNow.ToString("yyyy-MM-dd HH:mm:ss"), g_SharedData.WSP_AGENT_SETTING.strDisplayName, strInstanceName, PValue, CurrentRule.iAlertLevel, strAlertDesc, guidNew.ToString());

            if (CurrentRule.bRecordApp)
            {
                if (g_SharedData.WEBINFO.iNumberOfRequests > 0)
                {
                    string strClientIP = "";
                    string strURL = "";
                    string strTimeTaken = "";
                    string strDisplayName = g_SharedData.WSP_AGENT_SETTING.strDisplayName;

                    for (int i = 0; i < g_SharedData.WEBINFO.iNumberOfRequests; i++)
                    {
                        strURL = g_SharedData.arrWebRequests[i].strURL;
                        strClientIP = g_SharedData.arrWebRequests[i].strClientIP;
                        strTimeTaken = g_SharedData.arrWebRequests[i].iTimeTaken.ToString();

                        dtAppTrace.Rows.Add(guidNew.ToString(), strDisplayName, tmNow.ToString("yyyy-MM-dd HH:mm:ss"), CurrentRule.strReasonCode, strURL, strClientIP, strTimeTaken);
                    }
                }
            }

                //if (dtWebApps.Rows.Count > 0)
                //{
                //    foreach (DataRow drURI in dtWebApps.Rows)
                //    {
                //        strURL = drURI["URL"].ToString();
                //        if (strURL.Length >= 128)
                //            strURL = strURL.Substring(0, 127);

                //        dtAppTrace.Rows.Add(guidNew.ToString(), g_SharedData.WSP_AGENT_SETTING.strDisplayName, tmNow, CurrentRule.strReasonCode, strURL, drURI["CLIENT"].ToString(), drURI["TIME_TAKEN"].ToString());
                //    }
                //}
            
        }


        void TraceData()
        {

            string strPath = "c:\\temp\\servicelog\\a" + DateTime.Now.ToString("HHmmss") + ".txt";
            string strLine = "";

            using (StreamWriter w = File.AppendText(strPath))
            {
                strLine = "dtPValues";
                w.WriteLine(strLine);

                foreach (DataRow dr in dtPValues.Rows)
                {
                    strLine = dr["PCID"].ToString() + "," + dr["TimeIn"].ToString() + "," + dr["InstanceName"].ToString() + "," + dr["PValue"].ToString();
                    if(dr["PCID"].ToString() == "P164")
                        w.WriteLine(strLine);
                }

                strLine = " ";
                w.WriteLine(strLine);
                strLine = "dtSavedPValue";
                w.WriteLine(strLine);

                foreach (DataRow dr in dtSavedPValues.Rows)
                {
                    
                    strLine = dr["PCID"].ToString() + "," + dr["TimeIn"].ToString() + "," + dr["InstanceName"].ToString() + "," + dr["PValue"].ToString();
                    if (dr["PCID"].ToString() == "P164")
                        w.WriteLine(strLine);
                }

                strLine = " ";
                w.WriteLine(strLine);
                strLine = "dtRules";
                w.WriteLine(strLine);

                foreach (DataRow dr in dtAlertRules.Rows)
                {
                    
                    strLine = dr["ReasonCode"].ToString() + "," + dr["PCID"].ToString() + "," + dr["Threshold"].ToString() + dr["ReasonCodeDesc"].ToString();
                    if (dr["PCID"].ToString() == "P164")
                        w.WriteLine(strLine);
                }

                strLine = " ";
                w.WriteLine(strLine);
                strLine = "dtAlerts";
                w.WriteLine(strLine);

                foreach (DataRow dr in dtAlerts.Rows)
                {
                    strLine = dr["ReasonCode"].ToString() + "," + dr["InstanceName"].ToString() + "," + dr["PValue"].ToString() + dr["AlertDescription"].ToString();
                    w.WriteLine(strLine);
                }

                w.Close();
            }
        
        }

    }

}