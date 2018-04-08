using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Diagnostics;

public class BizTalkRulesAlerts
{
    public int iPerfLogCollectInterval = 15;
    public DataTable dtAlerts = new DataTable();
    public DataTable dtSavedPValues = new DataTable();
    public DataTable dtWebApps = new DataTable();
    public DataTable dtAppTrace = new DataTable();
    public DataTable dtBTSProcess = new DataTable();
    public DataTable dtSavedBTSProcess = new DataTable();
    public DataTable dtPID = new DataTable();

    public DateTime dtLastUpdatedTime = new DateTime();
    public string strMachineName;

    EventRecorder WSPEvent = new EventRecorder();

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

    public void CheckCustomRules(DataRow dr)
    {
        try
        {
            GetCurrentRuleRecord(dr);

            string strReasonCode = CurrentRule.strReasonCode;
            int iDuration = CurrentRule.iDuration;
            int iOperator = CurrentRule.iOperator;

            if (iPerfLogCollectInterval >= iDuration && iDuration != 0)
                iDuration = iPerfLogCollectInterval;

            DateTime tmNow = new DateTime();
            tmNow = DateTime.Now;
            //DateTime tmDuration = DateTime.Now.Subtract(TimeSpan.FromSeconds(iDuration + iPerfLogCollectInterval / 2));
            DateTime tmDuration = dtLastUpdatedTime.AddSeconds(Convert.ToDouble((iDuration) * (-1)));

            if (iOperator == 4)                            // amend for value chages
                iDuration = iPerfLogCollectInterval;

            if (strReasonCode == "R059" || strReasonCode == "R067")
                R059_R067_Rule(tmDuration);

            if (strReasonCode == "R057" || strReasonCode == "R058")
                R057_R058_Rule(tmDuration);
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent(ex.Message, "E", 1148);
        }
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

        if (strAlertLevel == "Warning")
            CurrentRule.iAlertLevel = 2;
        if (strAlertLevel == "Critical")
            CurrentRule.iAlertLevel = 3;
        if (strAlertLevel == "Information")
            CurrentRule.iAlertLevel = 1;
    }

    private void R059_R067_Rule(DateTime tmDuration)
    {

        int iCurrentRecvLocations = 0;
        int iSavedRecvLocations = 0;
        string strHostInstanceName = "";

        if (CurrentRule.strReasonCode == "R059")
        {
            if (dtBTSProcess.Rows.Count > 0)
                dtBTSProcess.Rows.Clear();
            BuildBTSProcessList();

            foreach (DataRow dr in dtSavedBTSProcess.Rows)
            {
                string strQuery = "InstanceName = '" + dr["InstanceName"] + "'";
                var rows = dtBTSProcess.Select(strQuery);  
                strHostInstanceName = dr["InstanceName"].ToString();

                if (!IsBTSProcessAlive(Convert.ToInt32(dr["PID"])) && !strHostInstanceName.ToUpper().Contains("ISOLATEDHOST"))
                    AddThisAlert(Convert.ToSingle(dr["PID"]), strHostInstanceName, "");                
            }
        }
        else
        {
            dtPID.Rows.Clear();
            dtPID.Columns.Clear();
        }

        if (CurrentRule.strReasonCode == "R067")
        {
            foreach (DataRow drCurrent in dtBTSProcess.Rows)
                iCurrentRecvLocations = iCurrentRecvLocations + Convert.ToInt32(drCurrent["ReceiveLocations"]);

            foreach (DataRow drSaved in dtSavedBTSProcess.Rows)
                iSavedRecvLocations = iSavedRecvLocations + Convert.ToInt32(drSaved["ReceiveLocations"]);

            if (iCurrentRecvLocations < iSavedRecvLocations)
            {
                string strRefValue = "#" + (iSavedRecvLocations - iCurrentRecvLocations).ToString() + " Receive Locations decreased.";
                AddThisAlert(Convert.ToSingle(iCurrentRecvLocations), "", strRefValue);
            }
        }       

    }

    private bool IsBTSProcessAlive(int iPID)
    {
        if (dtPID.Columns.Count < 1)
        {
            dtPID.Columns.Clear();
            dtPID.Columns.Add(new DataColumn("PID", typeof(int)));
            BuildBizTalkPIDList();
        }

        var rows = dtPID.Select("PID = '" + iPID.ToString() + "'");

        if (rows.Length > 0)
            return true;

        return false;
    }

    private void BuildBizTalkPIDList()
    {

        string[] arrInstanceNames;

        PerformanceCounterCategory pcCat = new PerformanceCounterCategory();
        PerformanceCounter PC = new PerformanceCounter();

        pcCat.CategoryName = "Process";
        float flPValue = 0;

        try
        {
            arrInstanceNames = pcCat.GetInstanceNames();

            foreach (string strProcess in arrInstanceNames)
            {
                if (strProcess.ToUpper().Contains("BTSNTSVC"))
                {
                    PC.CategoryName = "Process";
                    PC.CounterName = "ID Process";
                    PC.InstanceName = strProcess;

                    PC.NextValue();
                    System.Threading.Thread.Sleep(50);
                    flPValue = PC.NextValue();

                    int iCurrentPID = Convert.ToInt32(flPValue);
                    dtPID.Rows.Add(iCurrentPID);
                }
            }
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent("Service Point Agent failed to read list of BizTalk processes." + ex.Message, "E", 1174);
        }
    }

    public void BuildBTSProcessList()
    {
        dtBTSProcess.Columns.Clear();
        dtBTSProcess.Columns.Add(new DataColumn("InstanceName", typeof(string)));
        dtBTSProcess.Columns.Add(new DataColumn("PID", typeof(int)));
        dtBTSProcess.Columns.Add(new DataColumn("ReceiveLocations", typeof(int)));

        string[] arrInstanceNames;

        PerformanceCounterCategory pcCat = new PerformanceCounterCategory();
        PerformanceCounter PC = new PerformanceCounter();

        pcCat.CategoryName = "BizTalk:Messaging";
        DateTime dtNow = new DateTime();
        dtNow = DateTime.Now;
        float flPValue = 0;

        try
        {
            arrInstanceNames = pcCat.GetInstanceNames();

            foreach (string strHost in arrInstanceNames)
            {
                PC.CategoryName = "BizTalk:Messaging";
                PC.CounterName = "ID Process";
                PC.InstanceName = strHost;

                PC.NextValue();
                System.Threading.Thread.Sleep(50);
                flPValue = PC.NextValue();

                int iPID = Convert.ToInt32(flPValue);

                PC.CategoryName = "BizTalk:Messaging";
                PC.CounterName = "Active receive locations";
                PC.InstanceName = strHost;

                PC.NextValue();
                System.Threading.Thread.Sleep(50);
                flPValue = PC.NextValue();

                int iRecvLoc =  Convert.ToInt32(flPValue);
                dtBTSProcess.Rows.Add(strHost, iPID, iRecvLoc);                
            }
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent(ex.Message, "E", 1127);
        }

    }

    private void R057_R058_Rule(DateTime tmDuration)
    {
        bool bMultiInstances = true;

        DataTable dtAnalysis = new DataTable();
        dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
        
        CheckBizTalkHostThrottlingConditions(dtAnalysis);
//        CheckThreshold(bMultiInstances, CurrentRule.iOperator, CurrentRule.flThreshold, dtAnalysis);
    }

    private void CheckBizTalkHostThrottlingConditions(DataTable dtAnalysis)
    {
        DataTable dtInstanceList = new DataTable();
        dtInstanceList = dtAnalysis.DefaultView.ToTable(true, "InstanceName");
        string strQuery = "";

        foreach (DataRow dr2 in dtInstanceList.Rows)
        {
            string strInstanceName = dr2["InstanceName"].ToString();
            strQuery = "InstanceName = '" + strInstanceName + "'";
            var varComp1 = dtAnalysis.Select(strQuery);

            strQuery = "PValue > 0" + " AND InstanceName = '" + strInstanceName + "'";
           
            var varComp2 = dtAnalysis.Select(strQuery);

            if (varComp1.Length == varComp2.Length && varComp2.Length != 0)
            {
                int iIndex = 0;
                iIndex = varComp2.Length - 1;

                string strRefValue = SetHostThrottlingMessage(Convert.ToInt32(varComp2[iIndex]["PValue"]));
                AddThisAlert(Convert.ToSingle(varComp2[iIndex]["PValue"]), strInstanceName, strRefValue);
            }
            else
                strQuery = "";
        }
    }

    private string SetHostThrottlingMessage(int iIndicator)
    {
        switch (iIndicator)
        {
            case 1:
                return  "due to imbalanced message delivery rate (input rate exceeds output rate)";
            case 2:
                return "due to imbalanced message publishing rate (input rate exceeds output rate)";
            case 3:
                return "due to high in-process message count";
            case 4:
                return "due to process memory pressure";
            case 5:
                return "due to system memory pressure";
            case 6:
                return "due to database growth";
            case 8:
                return "due to high session count";
            case 9:
                return "due to high thread count";
            case 10:
                return "due to user override on delivery";
            case 11:
                return "due to user override on publishing";
            default:
                break;
        }
        return "";
    }

//0: Not throttling
//1: Throttling due to imbalanced message delivery rate (input rate exceeds output rate)
//3: Throttling due to high in-process message count
//4: Throttling due to process memory pressure
//5: Throttling due to system memory pressure
//9: Throttling due to high thread count
//10: Throttling due to user override on delivery

//0: Not throttling
//2: Throttling due to imbalanced message publishing rate (input rate exceeds output rate)
//4: Throttling due to process memory pressure
//5: Throttling due to system memory pressure
//6: Throttling due to database growth
//8: Throttling due to high session count
//9: Throttling due to high thread count
//11: Throttling due to user override on publishing

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
                    strQuery = "PValue > " + flThreshold.ToString() + " AND InstanceName = '" + dr2["InstanceName"].ToString() + "'";

                if (iOperator == 2)
                    strQuery = "PValue < " + flThreshold.ToString() + " AND InstanceName = '" + dr2["InstanceName"].ToString() + "'";

                if (iOperator == 3)
                    strQuery = "PValue = " + flThreshold.ToString() + " AND InstanceName = '" + dr2["InstanceName"].ToString() + "'";

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


    private void AddThisAlert(float PValue, string strInstanceName, string strRefValue)
    {
        Guid guidNew = new Guid();
        guidNew = Guid.NewGuid();
        DateTime tmNow = DateTime.Now;

        string strAlertDesc = "";
        if (strRefValue.Length > 0)
            strAlertDesc = CurrentRule.strReasonDescription + "[" + strRefValue + "]";
        else
            strAlertDesc = CurrentRule.strReasonDescription;

        if (strInstanceName.Length >= 200)
            strInstanceName = strInstanceName.Substring(0, 199);
        
        if (strAlertDesc.Length >= 250)
            strAlertDesc = strAlertDesc.Substring(0, 254);

        dtAlerts.Rows.Add(CurrentRule.strReasonCode, tmNow, strMachineName, strInstanceName, PValue, CurrentRule.iAlertLevel, strAlertDesc, guidNew.ToString());

        string strURL = "";

        if (CurrentRule.bRecordApp)
        {
            if (dtWebApps.Rows.Count > 0)
            {
                foreach (DataRow drURI in dtWebApps.Rows)
                {
                    strURL = drURI["URL"].ToString();
                    if (strURL.Length >= 128)
                        strURL = strURL.Substring(0, 127);

                    dtAppTrace.Rows.Add(guidNew.ToString(), strMachineName, tmNow.ToString("yyyy-MM-dd HH:mm:ss"), CurrentRule.strReasonCode, strURL, drURI["CLIENT"].ToString(), drURI["TIME_TAKEN"].ToString());
                }
            }
        }
    }

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

    //////////////////////////////////
}
