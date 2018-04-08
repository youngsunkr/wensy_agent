using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Diagnostics;


    public class IISRulesAlerts
    {
        public SharedData g_SharedData = new SharedData();

        //public int iPerfLogCollectInterval = 15;
        public DataTable dtAlerts = new DataTable();
        public DataTable dtSavedPValues = new DataTable();

        public DataTable dtAppTrace = new DataTable();

        public DateTime dtLastUpdatedTime = new DateTime();
        string strDisplayName;
        public Guid g_guidPerfCollectingTurn = new Guid();
        public DateTime dtmPValueTimeIn = new DateTime();

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

        const int MAX_APP_POOLS = 100;

        struct stSavedWPList
        {
            public string strInstanceName;
            public string strPID;
            public string strAppPoolDesc;
        }

        stSavedWPList[] arrSavedWPList = new stSavedWPList[MAX_APP_POOLS];
        int iNumberOfAppPools = 0;

        struct stSavedJavaWPList
        {
            public string strInstanceName;
            public string strPID;
        }

        stSavedJavaWPList[] arrSavedJavaWPList = new stSavedJavaWPList[MAX_APP_POOLS];
        int iNumberOfJavaPools = 0;

        public void CheckCustomRules(DataRow dr)
        {
            try
            {
                GetCurrentRuleRecord(dr);

                string strReasonCode = CurrentRule.strReasonCode;
                int iDuration = CurrentRule.iDuration;
                int iOperator = CurrentRule.iOperator;

                if (g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval >= iDuration && iDuration != 0)
                    iDuration = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval;

                if (iDuration == 0)
                    iDuration = 1;

                DateTime tmNow = new DateTime();
                tmNow = DateTime.Now;
                //DateTime tmDuration = DateTime.Now.Subtract(TimeSpan.FromSeconds(iDuration + iPerfLogCollectInterval / 2));
                DateTime tmDuration = dtLastUpdatedTime.AddSeconds(Convert.ToDouble((iDuration) * (-1)));

                TimeSpan tsGap = tmDuration - dtmPValueTimeIn;
                if (tsGap.Seconds >= 0)             //  if tmDuraion is greater than PValue TimeIn
                {
                    tmDuration = dtmPValueTimeIn.AddSeconds(-1);
                }

                if (iOperator == 4)                            // check value chages
                    iDuration = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval;

                if (strReasonCode == "R012" || strReasonCode == "R013" || strReasonCode == "R014")
                    R012_R013_R014_Rule(tmDuration);

                if (strReasonCode == "R015" || strReasonCode == "R039" || strReasonCode == "R043")
                    R015_R039_R043_Rule_v2(tmDuration);

                if (strReasonCode == "R022" || strReasonCode == "R023")
                    R022_R023_Rule(tmDuration);

                if (strReasonCode == "R036" || strReasonCode == "R037" || strReasonCode == "R038" || strReasonCode == "R040" || strReasonCode == "R041" || strReasonCode == "R042")
                    R036_R037_R038_R040_R041_R042_Rule(tmDuration);
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent(ex.Message, "E", 1130);
            }
        }

        private void GetCurrentRuleRecord(DataRow dr)
        {
            string strAlertLevel = "Information";

            try
            {
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
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Checking the current anaysis rule has been failed. - " + ex.Message, "E", 1180);
            }
        }

        private void R012_R013_R014_Rule(DateTime tmDuration)
        {

            bool bMultiInstances = true;

            DataTable dtAnalysis = new DataTable();
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);

            int iOperator = 1;
            CheckThreshold(bMultiInstances, iOperator, CurrentRule.flThreshold, dtAnalysis);
        }


        private void R036_R037_R038_R040_R041_R042_Rule(DateTime tmDuration)
        {
            bool bMultiInstances = true;

            DataTable dtAnalysis = new DataTable();
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);

            int iOperator = 1;
            CheckThreshold(bMultiInstances, iOperator, CurrentRule.flThreshold, dtAnalysis);
        }

        private void R015_R039_R043_Rule_v2(DateTime tmDuration)
        {

            string strReasonCode = CurrentRule.strReasonCode;
            string strReasonCodeDesc = CurrentRule.strReasonDescription;
            //string strPID;
            //string strInstanceDesc;

            int i = 0;
            string strSavedPID = "";
            bool bHasPID = false;

            //test code
            string strEventMsg = "";

            try
            {
                if (CurrentRule.strInstanceName.Contains("w3wp"))
                {
                    strSavedPID = "";
                    bHasPID = false;

                    if (iNumberOfAppPools > 0)
                    {
                        for (i = 0; i < iNumberOfAppPools; i++)
                        {
                            bHasPID = false;

                            strSavedPID = arrSavedWPList[i].strPID;

                            if (string.IsNullOrEmpty(strSavedPID))
                                break;

                            for (int j = 0; j < g_SharedData.WEBINFO.iNumberOfAppPools; j++)
                            {
                                if (strSavedPID == g_SharedData.arrWPList[j].strPID)
                                {
                                    bHasPID = true;
                                    break;
                                }
                            }

                            if (!bHasPID)       // PID disappeard
                            {
                                AddThisAlert(0, arrSavedWPList[i].strInstanceName, "[" + arrSavedWPList[i].strPID + "]" + arrSavedWPList[i].strAppPoolDesc);
                                
                                //test code
                                if (g_SharedData.WSP_AGENT_SETTING.strWS_URL.Contains("juliabook"))
                                {
                                    for (int t1 = 0; t1 < iNumberOfAppPools; t1++)
                                    {
                                        strEventMsg += "Saved : " + t1.ToString() + "," + iNumberOfAppPools.ToString() + "," + arrSavedWPList[t1].strPID + ",\n";
                                    }
                                    for (int t2 = 0; t2 < g_SharedData.WEBINFO.iNumberOfAppPools; t2++)
                                    {
                                        strEventMsg += "Shared : " + t2.ToString() + "," + g_SharedData.WEBINFO.iNumberOfAppPools.ToString() + "," + g_SharedData.arrWPList[t2].strPID + ",\n";
                                    }

                                    WSPEvent.WriteEvent(strEventMsg, "I", 4000);
                                }
                            }
                        }
                    }

                    iNumberOfAppPools = g_SharedData.WEBINFO.iNumberOfAppPools;
                    for (i = 0; i < iNumberOfAppPools; i++)
                    {
                        arrSavedWPList[i].strPID = g_SharedData.arrWPList[i].strPID;
                        arrSavedWPList[i].strInstanceName = g_SharedData.arrWPList[i].strInstanceName;
                        arrSavedWPList[i].strAppPoolDesc = g_SharedData.arrWPList[i].strAppPoolDesc;
                    }
                }

                if (CurrentRule.strInstanceName.Contains("java"))
                {
                    strSavedPID = "";
                    bHasPID = false;

                    if (iNumberOfJavaPools > 0)
                    {
                        for (i = 0; i < iNumberOfJavaPools; i++)
                        {
                            bHasPID = false;

                            strSavedPID = arrSavedJavaWPList[i].strPID;

                            if (string.IsNullOrEmpty(strSavedPID))
                                break;

                            for (int j = 0; j < g_SharedData.WEBINFO.iNumberOfJavaPools; j++)
                            {
                                if (strSavedPID == g_SharedData.arrJavaWPList[j].strPID)
                                {
                                    bHasPID = true;
                                    break;
                                }
                            }

                            if (!bHasPID)       // PID disappeard
                                AddThisAlert(0, arrSavedJavaWPList[i].strInstanceName, "[" + arrSavedJavaWPList[i].strPID + "]" + arrSavedJavaWPList[i].strInstanceName);
                        }
                    }

                    iNumberOfJavaPools = g_SharedData.WEBINFO.iNumberOfJavaPools;
                    for (i = 0; i < iNumberOfJavaPools; i++)
                    {
                        arrSavedJavaWPList[i].strPID = g_SharedData.arrJavaWPList[i].strPID;
                        arrSavedJavaWPList[i].strInstanceName = g_SharedData.arrJavaWPList[i].strInstanceName;
                    }
                }
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Checking application pool status has failed. - " + ex.Message, "E", 1186);
            }
        }

        private void R022_R023_Rule(DateTime tmDuration)           // SQL Connection Pool, MaxPoolSize, R022 - pooled connections.
        {

            bool bMultiInstances = false;

            if (CurrentRule.strInstanceName == "All Instances")
                bMultiInstances = true;

            DataTable dtAnalysis = new DataTable();
            dtAnalysis = BuildAnalysisTable(CurrentRule.strInstanceName, CurrentRule.strPCID, tmDuration, bMultiInstances);

            if (CurrentRule.strReasonCode == "R022")
            {
                if (g_SharedData.WEB_SETTING.iMaxConnectionPoolSize != 100)
                    CurrentRule.flThreshold = (float)g_SharedData.WEB_SETTING.iMaxConnectionPoolSize * (float)0.9;
            }

            int iOperator = 1;
            CheckThreshold(bMultiInstances, iOperator, CurrentRule.flThreshold, dtAnalysis);
        }


        private void CheckThreshold(bool bMultipleInstances, int iOperator, float flThreshold, DataTable dtAnalysis)
        {
            string strQuery = "";
            bool bRecordApps = CurrentRule.bRecordApp;

            if (dtAnalysis == null)
                return;
            try
            {
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
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Internal Error - Analyzing IIS Performance failed. - " + ex.Message, "W", 1182);
            }
        }


        private void AddThisAlert(float PValue, string strInstanceName, string strRefValue)
        {
            try
            {
                Guid guidNew = new Guid();
                guidNew = g_guidPerfCollectingTurn;
                DateTime tmNow = dtLastUpdatedTime;

                string strAlertDesc = "";
                if (strInstanceName.Contains("w3wp") || strInstanceName.Contains("w3svc"))
                    strAlertDesc = CurrentRule.strReasonDescription + "[" + strRefValue + "]";
                else
                    strAlertDesc = CurrentRule.strReasonDescription;

                if (strInstanceName.Length >= 200)
                    strInstanceName = strInstanceName.Substring(0, 199);

                if (strAlertDesc.Length >= 250)
                    strAlertDesc = strAlertDesc.Substring(0, 254);

                dtAlerts.Rows.Add(CurrentRule.strReasonCode, tmNow.ToString("yyyy-MM-dd HH:mm:ss"), strDisplayName, strInstanceName, PValue, CurrentRule.iAlertLevel, strAlertDesc, guidNew.ToString());

                if (CurrentRule.bRecordApp)
                {

                    strDisplayName = g_SharedData.WSP_AGENT_SETTING.strDisplayName;

                    if (g_SharedData.WEBINFO.iNumberOfRequests > 0)
                    {
                        string strClientIP = "";
                        string strURL = "";
                        string strTimeTaken = "";

                        for (int i = 0; i < g_SharedData.WEBINFO.iNumberOfRequests; i++)
                        {
                            strURL = g_SharedData.arrWebRequests[i].strURL;
                            strClientIP = g_SharedData.arrWebRequests[i].strClientIP;
                            strTimeTaken = g_SharedData.arrWebRequests[i].iTimeTaken.ToString();

                            dtAppTrace.Rows.Add(guidNew.ToString(), strDisplayName, tmNow.ToString("yyyy-MM-dd HH:mm:ss"), CurrentRule.strReasonCode, strURL, strClientIP, strTimeTaken);

                        }
                    }

                }
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Handling IIS Alerts has failed. - " + ex.Message, "E", 1185);
            }
        }

        private DataTable BuildAnalysisTable(string strInstanceName, string strPCID, DateTime tmDuration, bool bMultiInstances)
        {

            System.Data.DataRow[] rows1;

            try
            {
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
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Internal Error - Analzing IIS performance. - " + ex.Message, "E", 1181);
                return null;
            }
            
        }

        //////////////////////////////////
    }
