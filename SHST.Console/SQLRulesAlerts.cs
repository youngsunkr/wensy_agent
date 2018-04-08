using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Diagnostics;
using System.IO;

class SQLRulesAlerts
{
    public SharedData g_SharedData = new SharedData();

    public int iPerfLogCollectInterval = 15;
    //public DataTable dtAlerts = new DataTable();
    public DataTable dtSavedPValues = new DataTable();

    public DataTable dtCurrentPValues = new DataTable();

    public DateTime dtLastUpdatedTime = new DateTime();
    public string strMachineName;
    public string strDisplayName;
    public Guid g_guidPerfCollectingTurn = new Guid();
    public DateTime dtmPValueTimeIn = new DateTime();

    string[] astrInstanceList = new string[256];
    float[] flaThresholds = new float[256];

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

    public struct stSQLAlerts
    {
        public string strReasonCode;
        public DateTime tmNow;
        public string strDisplayName;
        public int iAlertStatus;
        public float PValue;
        public string strAlertDesc;
        public string strGUID;
        public string strInstanceName;
    }

    public stSQLAlerts[] g_stSQLAlerts = new stSQLAlerts[300];
    public int g_iSQLAlertCount = 0;

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

            if (iDuration == 0)
                iDuration = 1;

            if (iOperator == 4)                            // amend for value chages
                iDuration = iPerfLogCollectInterval;

            DateTime tmNow = new DateTime();
            tmNow = DateTime.Now;
            //DateTime tmDuration = DateTime.Now.Subtract(TimeSpan.FromSeconds(iDuration + iPerfLogCollectInterval / 2));
            DateTime tmDuration = dtLastUpdatedTime.AddSeconds(Convert.ToDouble((iDuration) * (-1)));

            TimeSpan tsGap = tmDuration - dtmPValueTimeIn;
            if (tsGap.Seconds >= 0)             //  if tmDuraion is greater than PValue TimeIn
            {
                tmDuration = dtmPValueTimeIn.AddSeconds(-1);
            }

            RunSQLCustomRules(tmDuration);

        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent("SHST failed to check custom SQL Rules to be alerted. - " + ex.Message, "E", 1152);
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
    

    private void CheckThreshold(bool bMultipleInstances, int iOperator, float flThreshold, DataTable dtAnalysis)
    {
        string strQuery = "";
        bool bRecordApps = CurrentRule.bRecordApp;
        string strInstance = "";

        if (bMultipleInstances)
        {
            DataTable dtInstanceList = new DataTable();
            dtInstanceList = dtAnalysis.DefaultView.ToTable(true, "InstanceName");

            foreach (DataRow dr2 in dtInstanceList.Rows)
            {
                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                // Amended for Checking SQL Counters only.
                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                strInstance = dr2["InstanceName"].ToString();

                for (int i = 0; i < 256; i++)                           // look up threshold for each instance
                {
                    if (astrInstanceList[i] == strInstance)
                    {
                        flThreshold = flaThresholds[i];
                        break;
                    }
                }

                if (flThreshold < 0)                                    // if it's not valid threshold, skip this instance
                    continue;
                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

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
        guidNew = g_guidPerfCollectingTurn;
        DateTime tmNow = dtLastUpdatedTime;

        string strAlertDesc = "";
        if (strRefValue.Length > 0)
            strAlertDesc = CurrentRule.strReasonDescription + "[" + strRefValue + "]";
        else
            strAlertDesc = CurrentRule.strReasonDescription;
        
        if (strInstanceName.Length >= 200)
            strInstanceName = strInstanceName.Substring(0, 199);

        if (strAlertDesc.Length >= 250)
            strAlertDesc = strAlertDesc.Substring(0, 254);

        //dtAlerts.Rows.Add(CurrentRule.strReasonCode, tmNow, strDisplayName, strInstanceName, PValue, 1, strAlertDesc, guidNew.ToString());

        if (g_iSQLAlertCount < 300)
        {
            g_stSQLAlerts[g_iSQLAlertCount].strReasonCode = CurrentRule.strReasonCode;
            g_stSQLAlerts[g_iSQLAlertCount].tmNow = tmNow;
            g_stSQLAlerts[g_iSQLAlertCount].strDisplayName = strDisplayName;
            g_stSQLAlerts[g_iSQLAlertCount].strInstanceName = strInstanceName;
            g_stSQLAlerts[g_iSQLAlertCount].strAlertDesc = strAlertDesc;
            g_stSQLAlerts[g_iSQLAlertCount].strGUID = guidNew.ToString();
            g_stSQLAlerts[g_iSQLAlertCount].PValue = PValue;
            g_stSQLAlerts[g_iSQLAlertCount].iAlertStatus = CurrentRule.iAlertLevel;

            g_iSQLAlertCount++;
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

    private void RunSQLCustomRules(DateTime tmDuration)
    {
        bool bMultiInstances = true;
        float flRValue = 0;
        float flThreshold = 0;
        int iOperator = 1;
        int i = 0;

        DataTable dtAnalysis = new DataTable();        

        if (CurrentRule.strReasonCode == "R100")
        {
            
            bMultiInstances = false;
            flRValue = ReadReferenceValue();
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
            iOperator = CurrentRule.iOperator;
            flThreshold = CurrentRule.flThreshold * flRValue / 100;          // In case that 10% of batch requests/sec is threshold.

            if (flRValue != -1)                                  // If there's a valid reference value
                CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);

        }

        if (CurrentRule.strReasonCode == "R101")
        {
            bMultiInstances = false;
            flRValue = ReadReferenceValue();
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
            flThreshold = flRValue * CurrentRule.flThreshold;

            if (flRValue != -1)                                  // If there's a valid reference value
                CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
        }

        if (CurrentRule.strReasonCode == "R102")
        {
            bMultiInstances = false;
            flRValue = ReadReferenceValue();
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
            flThreshold = flRValue * CurrentRule.flThreshold;

            if (flRValue != -1)                                  // If there's a valid reference value
                CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
        }

        if (CurrentRule.strReasonCode == "R103")
        {
            bMultiInstances = false;
            flRValue = ReadReferenceValue();
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
            flThreshold = flRValue * CurrentRule.flThreshold / 100;

            if (flRValue != -1)                                  // If there's a valid reference value
                CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
        }

        if (CurrentRule.strReasonCode == "R105")
        {
            bMultiInstances = false;
            flRValue = ReadReferenceValue();
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
            flThreshold = flRValue * CurrentRule.flThreshold / 100;

            if (flRValue != -1)                                  // If there's a valid reference value
                CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
        }

        if (CurrentRule.strReasonCode == "R111")
        {
            bMultiInstances = false;
            flRValue = ReadReferenceValue();
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
            flThreshold = flRValue * CurrentRule.flThreshold;

            if (flRValue != -1)                                  // If there's a valid reference value
                CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
        }

        if (CurrentRule.strReasonCode == "R114")
        {
            bMultiInstances = false;
            flRValue = ReadReferenceValue();
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
            if (CurrentRule.flThreshold != 0)
            {
                flThreshold = (flRValue / CurrentRule.flThreshold) * 100;
                if (flRValue != -1)                                  // If there's a valid reference value
                    CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
            }
        }

        if (CurrentRule.strReasonCode == "R119" || CurrentRule.strReasonCode == "R120")
        {
            bMultiInstances = false;
            flRValue = ReadReferenceValue();
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
            flThreshold = flRValue * CurrentRule.flThreshold / 100;

            if (flRValue != -1)                                  // If there's a valid reference value
                CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
        }

        if (CurrentRule.strReasonCode == "R124")
        {
            bMultiInstances = true;
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);

            DataTable dtInstanceList = new DataTable();
            dtInstanceList = dtAnalysis.DefaultView.ToTable(true, "InstanceName");

            foreach (DataRow dr2 in dtInstanceList.Rows)
            {
                flRValue = ReadReferenceValue(dr2["InstanceName"].ToString());
                flaThresholds[i] = flRValue * CurrentRule.flThreshold / 100;
                astrInstanceList[i] = dr2["InstanceName"].ToString();
                i++;
            }

            CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
        }

        if (CurrentRule.strReasonCode == "R127")
        {
            bMultiInstances = false;
            flRValue = ReadReferenceValue();
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);
            flThreshold = flRValue * CurrentRule.flThreshold / 100;

            if (flRValue != -1)                                  // If there's a valid reference value
                CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
        }

        if (CurrentRule.strReasonCode == "R130")
        {
            bMultiInstances = true;
            iOperator = CurrentRule.iOperator;
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);

            DataTable dtInstanceList = new DataTable();
            dtInstanceList = dtAnalysis.DefaultView.ToTable(true, "InstanceName");

            foreach (DataRow dr2 in dtInstanceList.Rows)
            {
                flRValue = ReadReferenceValue(dr2["InstanceName"].ToString());
                if (flRValue != 0)
                    flaThresholds[i] = CurrentRule.flThreshold / flRValue;
                else
                    flaThresholds[i] = -1;

                astrInstanceList[i] = dr2["InstanceName"].ToString();
                i++;
            }

            CheckThreshold(bMultiInstances, iOperator, flThreshold, dtAnalysis);
            
        }

        if (CurrentRule.strReasonCode == "R144")
        {
            bMultiInstances = true;
            iOperator = CurrentRule.iOperator;

            DateTime tmNow = DateTime.Now;      // To check increased value, duration should be 2 * data collection interval
            tmNow = tmNow.AddSeconds(Convert.ToDouble((iPerfLogCollectInterval * 2) * (-1)));

            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmNow, bMultiInstances);
            CheckIncreasedPValue(dtAnalysis);
        }

    }

    private float ReadReferenceValue()
    {
        try
        {
            foreach (DataRow dr in dtCurrentPValues.Rows)
            {
                if (CurrentRule.strPCID == dr["PCID"].ToString() )
                {
                    return (float)Convert.ToSingle(dr["RValue"].ToString());
                }
            }

            foreach (DataRow dra in dtSavedPValues.Rows)
            {
                if (CurrentRule.strPCID == dra["PCID"].ToString() )
                {
                    return (float)Convert.ToSingle(dra["RValue"].ToString());
                }
            }
        }
        catch (Exception ex)
        {
            return -1;
        }

        return -1;
    }

    private float ReadReferenceValue(string strInstanceName)
    {

        try
        {
            foreach (DataRow dr in dtCurrentPValues.Rows)
            {
                if (CurrentRule.strPCID == dr["PCID"].ToString() && strInstanceName == dr["InstanceName"].ToString())
                {
                    return (float)Convert.ToSingle(dr["RValue"].ToString());
                }
            }

            foreach (DataRow dra in dtSavedPValues.Rows)
            {
                if (CurrentRule.strPCID == dra["PCID"].ToString() && strInstanceName == dra["InstanceName"].ToString())
                {
                    return (float)Convert.ToSingle(dra["RValue"].ToString());
                }
            }
        }
        catch (Exception ex)
        {
            return -1;
        }

        return -1;
    }


    private void CheckIncreasedPValue(DataTable dtAnalysis)
    {
        DataTable dtCounters = new DataTable();
        dtCounters = dtAnalysis.Copy();

        DataTable dtInstanceList = new DataTable();
        dtInstanceList = dtCounters.DefaultView.ToTable(true, "InstanceName");
        
        float[] flPValues = new float[64];
        int iIndex = 0;
        string strInstanceName = "";

        foreach (DataRow instance in dtInstanceList.Rows)
        {
            foreach (DataRow dr in dtCounters.Rows)     // assuming dtCounters are ordered by TimeIn, to compare last 2 values.
            {
                if (instance["InstanceName"].ToString() == dr["InstanceName"].ToString())
                {                    
                    flPValues[iIndex] = (float)Convert.ToSingle(dr["PValue"].ToString());
                    strInstanceName = dr["InstanceName"].ToString();
                    iIndex += 1;
                }
            }

            if (iIndex >= 2)        // If #Perfmon values for this instance is greater than 2
            {
                if (flPValues[iIndex-1] > flPValues[iIndex - 2])
                    AddThisAlert(flPValues[iIndex-1], strInstanceName, "");                
            }

            iIndex = 0;
        }

    }





}