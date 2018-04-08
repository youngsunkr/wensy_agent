
namespace DataAccess
{
    using System.Data;
    using System.Data.SqlClient;
    using System.Diagnostics;
    using System;

    using System.Text;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Collections.Generic;
    using System.Web.Services;

    //test
    class WSP_Op
    {
        
        public SharedData g_SharedData = new SharedData();
        public int g_iServerNumber = 0;
        public DateTime g_dtTimeIn = new DateTime();
        public DateTime g_dtTimeIn_UTC = new DateTime();

        public DataTable dtPCID = new DataTable();
        public DataTable dtInstanceList = new DataTable();
        public DataTable dtPValues = new DataTable();
        public DataTable dtAlertRules = new DataTable();

        public int g_iSQLMaxAlertLevel = 0;
        public int g_iWebMaxAlertLevel = 0;
        public int g_iHCQueryTime = 0;

        //set
        public bool bRegisterd = false;

        EventRecorder WSPEvent = new EventRecorder();

        public bool StartWSPDBOperation()
        {
            
            bRegisterd = false;

            RegisterThisHost();

            return bRegisterd;            
        }


        private void RegisterThisHost()
        {
            try
            {
                bRegisterd = false;
               
                InsertHostWS();
            }
            catch (Exception e)
            {
                WSPEvent.WriteEvent("Registering this host by Service Point agent has been failed. - " + e.Message, "E", 1105);
            }
        }
        
        private void InsertHostWS()
        {
            WSP.Console.WS_AGENTINITIAL.AgentInitial UpdateHoststatusVersion = new WSP.Console.WS_AGENTINITIAL.AgentInitial();
            UpdateHoststatusVersion.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AgentInitial.asmx";
            UpdateHoststatusVersion.Timeout = 10000;
            
            try
            { 
                int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;
                string Hostname = g_SharedData.SYSINFO.strComputerName;
                string CurrentStatus = "0";
                string RAMSize = Convert.ToInt64(g_SharedData.SYSINFO.flRAMSize).ToString();
                string WinVer = g_SharedData.SYSINFO.strWinVer;
                string Processors = g_SharedData.SYSINFO.iNumberOfProcessors.ToString();
                string IPAddress = g_SharedData.SYSINFO.strIPAddress;
                string AgentVer =  g_SharedData.WSP_AGENT_SETTING.iBuildNumber;

                int iResult = UpdateHoststatusVersion.UpdateHoststatus_Version(Hostname, CurrentStatus, RAMSize, WinVer, Processors, IPAddress, AgentVer, iServerNumber);

                if (iResult > 0)
                    bRegisterd = true;

            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Service Point Agent has failed to update this server information. - " + ex.Message, "E", 1170);
            }

        }

        
        public bool ReadPerformanceCounters()
        {
            WSP.Console.WS_AGENTINITIAL.AgentInitial PCIDList = new WSP.Console.WS_AGENTINITIAL.AgentInitial();
            PCIDList.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AgentInitial.asmx";
            PCIDList.Timeout = 10000;

            DataTable dtParms = SetParmeterTable();

            try
            {
                if (dtPCID.Rows.Count > 0)
                    dtPCID.Clear();

                dtParms.Rows.Add(g_SharedData.WSP_AGENT_SETTING.iServerNumber, "ServerNum", "INT", g_SharedData.WSP_AGENT_SETTING.iServerNumber);
                Byte[] btResult = PCIDList.PCIDList(g_SharedData.WSP_AGENT_SETTING.iServerNumber.ToString());

                DataTable dtResult = BytesToDataTable(btResult);

                if (btResult == null || dtResult == null)
                    return false;
                else
                    dtPCID = dtResult.Copy();

            }
            catch (Exception e)
            {
                WSPEvent.WriteEvent("Service Point Agent failed to read list of performance counters from WSP DB - " + e.Message, "E", 1172);
                return false;
            }

            
            try
            {
                dtParms.Rows.Clear();                
                if (dtInstanceList.Rows.Count > 0)
                    dtInstanceList.Clear();
                
                dtParms.Rows.Add(g_SharedData.WSP_AGENT_SETTING.iServerNumber, "ServerNum", "INT", g_SharedData.WSP_AGENT_SETTING.iServerNumber);

                byte[] btPIList = PCIDList.PCIDInstanceList(g_SharedData.WSP_AGENT_SETTING.iServerNumber.ToString());

                 DataTable dtResultInstance = BytesToDataTable(btPIList);

                 if (btPIList == null || dtResultInstance == null)
                     return false;
                 else
                     dtInstanceList = dtResultInstance.Copy();

            }
            catch (Exception e)
            {
                WSPEvent.WriteEvent("Service Point Agent failed to read list of Instances from WSP DB - " + e.Message, "E", 1146);
                return false;
            }

            return true;

        }
        
        public bool ReadAlertRules()
        {
            WSP.Console.WS_AGENTINITIAL.AgentInitial AlertRuleList = new WSP.Console.WS_AGENTINITIAL.AgentInitial();
            AlertRuleList.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AgentInitial.asmx";
            AlertRuleList.Timeout = 10000;

            DataTable dtParms = SetParmeterTable();

            try
            {
                if (dtAlertRules.Rows.Count > 0)
                    dtAlertRules.Rows.Clear();

                dtParms.Rows.Add(g_SharedData.WSP_AGENT_SETTING.iServerNumber, "ServerNum", "INT", g_SharedData.WSP_AGENT_SETTING.iServerNumber);

                //byte[] btResult = wsSelect.SelectSPVaules("p_AlertRules_Server_List", DataTableToBytes(dtParms));
                byte[] btResult = AlertRuleList.AlertRuleList(g_SharedData.WSP_AGENT_SETTING.iServerNumber.ToString());

                 DataTable dtResult = BytesToDataTable(btResult);

                 if (btResult == null || dtResult == null)
                     return false;
                 else
                     dtAlertRules = dtResult.Copy();
            }
            catch (Exception e)
            {
                WSPEvent.WriteEvent("Service Point Agent failed to read rules for alerts - " + e.Message, "E", 1171);
                return false;
            }

            return true;

        }

        private static byte[] StrToByteArray(string str)
        {
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetBytes(str);
        }

        private static string ByteArrayToStr(byte[] barr)
        {
            UTF8Encoding encoding = new UTF8Encoding();
            return encoding.GetString(barr, 0, barr.Length);
        }


        public DateTime InsertPerformanceValues()
        {
            return InsertPerfDataUsingWebService();
        }

        DateTime InsertPerfDataUsingWebService()
        {
            DateTime tmNow = new DateTime();
            tmNow = g_dtTimeIn;

            /////////////////////////////////////////////////////////////////////////////////////////
            //FIX : 2014.11.24
            //현지 시간을 맞추기 위해 시간을 문자열 형식으로 전송한다.

            DataTable dtPValuesToSend = new DataTable();
            dtPValuesToSend.Columns.Clear();

            dtPValuesToSend.Columns.Add(new DataColumn("PCID", typeof(string)));
            dtPValuesToSend.Columns.Add(new DataColumn("HOSTNAME", typeof(string)));
            dtPValuesToSend.Columns.Add(new DataColumn("TimeIn", typeof(string)));
            dtPValuesToSend.Columns.Add(new DataColumn("PValue", typeof(float)));
            dtPValuesToSend.Columns.Add(new DataColumn("RValue", typeof(string)));
            dtPValuesToSend.Columns.Add(new DataColumn("InstanceName", typeof(string)));
            dtPValuesToSend.Columns.Add(new DataColumn("PObjectName", typeof(string)));
            dtPValuesToSend.Columns.Add(new DataColumn("PCounterName", typeof(string)));
            dtPValuesToSend.Columns.Add(new DataColumn("ServerNum", typeof(string)));

            //////////////////////////////////////////////////////////////////////////////////////

            try
            {
                for (int i = 0; i < dtPValues.Rows.Count; i++)
                {
                    //v3. P001, P002 가 100을 초과하는 값은 '0'으로 설정.
                    if (dtPValues.Rows[i]["PCID"].ToString() == "P001" || dtPValues.Rows[i]["PCID"].ToString() == "P002")
                    {
                        if(Convert.ToSingle(dtPValues.Rows[i]["PValue"].ToString()) > 100)
                        {
                            dtPValuesToSend.Rows.Add(dtPValues.Rows[i]["PCID"].ToString(), dtPValues.Rows[i]["HOSTNAME"].ToString(),
                                Convert.ToDateTime(dtPValues.Rows[i]["TimeIn"]).ToString("yyyy-MM-dd HH:mm:ss"),
                                0, dtPValues.Rows[i]["RValue"].ToString(), dtPValues.Rows[i]["InstanceName"].ToString(), g_iServerNumber.ToString());

                            continue;
                        }
                    }
                    
                    dtPValuesToSend.Rows.Add(dtPValues.Rows[i]["PCID"].ToString(), 
                        dtPValues.Rows[i]["HOSTNAME"].ToString(),
                        Convert.ToDateTime(dtPValues.Rows[i]["TimeIn"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        Convert.ToSingle(dtPValues.Rows[i]["PValue"].ToString()), 
                        dtPValues.Rows[i]["RValue"].ToString(), 
                        dtPValues.Rows[i]["InstanceName"].ToString(),
                        dtPValues.Rows[i]["CategoryName"].ToString(),
                        dtPValues.Rows[i]["CounterName"].ToString(),
                        g_iServerNumber.ToString());
                }
                
                WSP.Console.WS_PERFINSERT.PerfValueInsert wsPInsert = new WSP.Console.WS_PERFINSERT.PerfValueInsert();
                
                wsPInsert.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/PerfValueInsert.asmx";
                wsPInsert.Timeout = 10000;

                byte[] b = DataTableToBytes(dtPValuesToSend);

                int iReturn = wsPInsert.PValueInsert(b, g_SharedData.WSP_AGENT_SETTING.iServerNumber, g_dtTimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_dtTimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));

                //v3.patch.중간에 시간을 다시 읽어오지 않고, tbDashboard, tbPerfmonValues 시간 값을 동기화한다.
                tmNow = g_dtTimeIn;

                foreach (DataRow dr in dtPValues.Rows)
                {
                    if (dr["TimeIn"] != null)
                    {
                        tmNow = Convert.ToDateTime(dr["TimeIn"].ToString());
                        break;
                    }
                }

                return tmNow;
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Converting Data failed." + ex.Message, "E", 1176);
                return tmNow;
                //Console.WriteLine("Calling SP has been failed." + ex.Message);
            }
        }

        public void InsertAlertsToDB(DataTable dtToBeAlerted)
        {
            InsertAlertsUsingWS(dtToBeAlerted);
            return;
        }

        private void InsertAlertsUsingWS(DataTable dtToBeAlerted)
        {
            if (dtToBeAlerted.Rows.Count < 1)
                return;
            
            int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;
            string strServertype = g_SharedData.WSP_AGENT_SETTING.strServerType;
    

            /////////////////////////////////////////////////////////////////////////////////////////
            //FIX : 2014.11.24
            //현지 시간을 맞추기 위해 시간을 문자열 형식으로 전송한다.

            DataTable dtAlertsToSend = new DataTable();


            //DisplayName (앞단에서 Hostname으로 전달하나 웹에서 변수명때문에 display으로 변경)
            dtAlertsToSend.Columns.Clear();
            dtAlertsToSend.Columns.Add(new DataColumn("ReasonCode", typeof(string)));
            dtAlertsToSend.Columns.Add(new DataColumn("TimeIn", typeof(string)));
            dtAlertsToSend.Columns.Add(new DataColumn("DisplayName", typeof(string)));
            dtAlertsToSend.Columns.Add(new DataColumn("InstanceName", typeof(string)));
            dtAlertsToSend.Columns.Add(new DataColumn("PValue", typeof(float)));
            dtAlertsToSend.Columns.Add(new DataColumn("AlertStatus", typeof(int)));
            dtAlertsToSend.Columns.Add(new DataColumn("AlertDescription", typeof(string)));
            dtAlertsToSend.Columns.Add(new DataColumn("AlertRecordID", typeof(Guid)));
            dtAlertsToSend.Columns.Add(new DataColumn("ServerNum", typeof(string)));
            dtAlertsToSend.Columns.Add(new DataColumn("ServerType", typeof(string)));
            dtAlertsToSend.Columns.Add(new DataColumn("TimeIn_UTC", typeof(string)));
            /////////////////////////////////////////////////////////////////////////////////////////

            try
            {
                string strAlertDescription = "";

                /////////////////////////////////////////////////////////////////////////////////////////
                for (int i = 0; i < dtToBeAlerted.Rows.Count; i++)
                {
                    // <i> : 인스턴스명, <t> : 임계값, <p> : 성능값 태그를 실제값으로 대체한다.
                    strAlertDescription = dtToBeAlerted.Rows[i]["AlertDescription"].ToString();
                    
                    if(strAlertDescription.Contains("{i}"))
                        strAlertDescription = strAlertDescription.Replace("{i}", dtToBeAlerted.Rows[i]["InstanceName"].ToString());
                    
                    if(strAlertDescription.Contains("{t}"))
                        strAlertDescription = strAlertDescription.Replace("{t}", GetThresholdValue(dtToBeAlerted.Rows[i]["ReasonCode"].ToString()));

                    if (strAlertDescription.Contains("{p}"))
                        strAlertDescription = strAlertDescription.Replace("{p}", dtToBeAlerted.Rows[i]["PValue"].ToString());

                    dtAlertsToSend.Rows.Add(dtToBeAlerted.Rows[i]["ReasonCode"].ToString(),
                        Convert.ToDateTime(dtToBeAlerted.Rows[i]["TimeIn"]).ToString("yyyy-MM-dd HH:mm:ss"),
                        dtToBeAlerted.Rows[i]["Hostname"].ToString(),
                        dtToBeAlerted.Rows[i]["InstanceName"].ToString(), 
                        Convert.ToSingle(dtToBeAlerted.Rows[i]["PValue"].ToString()),
                        Convert.ToInt32(dtToBeAlerted.Rows[i]["AlertStatus"]),
                        strAlertDescription,
                        dtToBeAlerted.Rows[i]["AlertRecordID"].ToString(),
                        iServerNumber.ToString(),
                        strServertype,
                        g_dtTimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                /////////////////////////////////////////////////////////////////////////////////////////

                WSP.Console.WS_ALERTS.AlertsAdd wsAddAlerts = new WSP.Console.WS_ALERTS.AlertsAdd();

                wsAddAlerts.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AlertsAdd.asmx";
                wsAddAlerts.Timeout = 10000;

                int iResult = wsAddAlerts.AddAlerts(DataTableToBytes(dtAlertsToSend), iServerNumber, g_dtTimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_dtTimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Inserting alerts has been failed. " + ex.Message, "E", 1106);
            }

        }

        private string GetThresholdValue(string strReasonCode)
        {
            var Rows = dtAlertRules.Select("ReasonCode = '" + strReasonCode + "'");
            string strThreshold = "";

            foreach (var row in Rows)
            {
                strThreshold = row["Threshold"].ToString();    
            }

            return strThreshold;

        }
        
        public void InsertAppsToDB(DataTable dtAppTrace)
        {
            InsertAppsUsingWS(dtAppTrace);
        }

        public void InsertAppsUsingWS(DataTable dtAppTrace)
        {
            int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

            DataTable dtApps = new DataTable();
            
            try
            {
                WSP.Console.WS_DBOP.GeneralDBOp wsInsert = new WSP.Console.WS_DBOP.GeneralDBOp();
                wsInsert.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/GeneralDBOp.asmx";
                wsInsert.Timeout = 10000;

                DataTable dtParms = SetParmeterTable();

                dtParms.Rows.Add(iServerNumber, "ServerNum", "INT", iServerNumber);
                dtParms.Rows.Add(iServerNumber, "TimeIn", "DATETIME", "");
                dtParms.Rows.Add(iServerNumber, "AlertRecordID", "STRING", "");
                dtParms.Rows.Add(iServerNumber, "ReasonCode", "STRING", "");
                dtParms.Rows.Add(iServerNumber, "URI", "STRING", "");
                dtParms.Rows.Add(iServerNumber, "ClientLocation", "STRING", "");
                dtParms.Rows.Add(iServerNumber, "RunningTime", "INT", "");

                dtApps.Columns.Clear();
                dtApps.Columns.Add(new DataColumn("ServerNum", typeof(int)));
                dtApps.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
                dtApps.Columns.Add(new DataColumn("AlertRecordID", typeof(Guid)));
                dtApps.Columns.Add(new DataColumn("ReasonCode", typeof(string)));
                dtApps.Columns.Add(new DataColumn("URI", typeof(string)));
                dtApps.Columns.Add(new DataColumn("ClientLocation", typeof(string)));
                dtApps.Columns.Add(new DataColumn("RunningTime", typeof(int)));

                string strURI = "";
                foreach (DataRow dr in dtAppTrace.Rows)
                {
                    strURI = dr["URI"].ToString();
                    if(strURI.Length >=128)
                        strURI = strURI.Substring(0,127);

                    //dtApps.Rows.Add(iServerNumber, dr["TimeIn"].ToString(), dr["AlertRecordID"].ToString(), dr["ReasonCode"].ToString(), strURI, dr["ClientLocation"].ToString(), Convert.ToInt32(dr["RunningTime"].ToString()));
                    dtApps.Rows.Add(iServerNumber, Convert.ToDateTime(dr["TimeIn"]).ToString("yyyy-MM-dd HH:mm:ss"), 
                        dr["AlertRecordID"].ToString(), dr["ReasonCode"].ToString(), strURI, dr["ClientLocation"].ToString(), Convert.ToInt32(dr["RunningTime"].ToString()));
                }

                //int iResult = wsInsert.InsertSPValues("p_tbAppTrace_Add", DataTableToBytes(dtParms), DataTableToBytes(dtApps));
                int iResult = wsInsert.ApptraceInsert(DataTableToBytes(dtApps), iServerNumber, g_dtTimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_dtTimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
                dtApps.Clear();                
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Service Point Agent has failed to insert list of running applications of WEB. - " + ex.Message, "E", 1173);
            }
        }

        public void UpdateHostPerfStatus(DataTable dtToBeAlerted, DateTime dtLastPerfDataTimeIn, DateTime g_dtTimeIn_UTC)
        {

            string strReasonCode = "";
            string strAlertLevel = "";
            int iAlertLevel = 0;
            int iMaxLevel = 0;            

            if (dtToBeAlerted.Rows.Count > 0)
            {
                foreach (DataRow dr in dtToBeAlerted.Rows)
                {
                    strReasonCode = dr["ReasonCode"].ToString();
                    var Rows = dtAlertRules.Select("ReasonCode = '" + strReasonCode + "'");

                    foreach (DataRow dr2 in Rows)
                    {
                        strAlertLevel = dr2["AlertLevel"].ToString();
                        if (strAlertLevel == "Information")
                            iAlertLevel = 1;
                        if (strAlertLevel == "Warning")
                            iAlertLevel = 2;
                        if (strAlertLevel == "Critical")
                            iAlertLevel = 3;
                        if (iAlertLevel > iMaxLevel)
                            iMaxLevel = iAlertLevel;
                    }                    
                }
            }

            if (g_SharedData.WSP_AGENT_SETTING.strServerType == "SQL")
            {
                if (g_iSQLMaxAlertLevel > iMaxLevel)
                    iMaxLevel = g_iSQLMaxAlertLevel;
            }

            if (g_SharedData.WSP_AGENT_SETTING.strServerType.ToUpper() == "WEB")
            {
                if (g_iWebMaxAlertLevel > iMaxLevel)
                    iMaxLevel = g_iWebMaxAlertLevel;
            }

            DateTime dtNow = DateTime.Now;
            int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

            try
            {
                WSP.Console.WS_AGENTINITIAL.AgentInitial UpdateHoststatus = new WSP.Console.WS_AGENTINITIAL.AgentInitial();
                UpdateHoststatus.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AgentInitial.asmx";
                UpdateHoststatus.Timeout = 10000;

                string CurrentStatus = iMaxLevel.ToString();
                string TimeIn = dtLastPerfDataTimeIn.ToString("yyyy-MM-dd HH:mm:ss");

                int iResult = UpdateHoststatus.UpdateHoststatus(CurrentStatus, iServerNumber, TimeIn, g_dtTimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("WSP has failed to update host information for performance data. - " + ex.Message, "E", 1167);
            }

        }
        
        //Agent Login 서비스 시작
        public bool IsServiceReady()
        {

            WSP.Console.WS_AGENTINITIAL.AgentInitial AgentLogin = new WSP.Console.WS_AGENTINITIAL.AgentInitial();
            AgentLogin.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AgentInitial.asmx";
            AgentLogin.Timeout = 10000;
            g_SharedData.WSP_AGENT_SETTING.iRemainingMinutes = 0;

            DataTable dtParms = SetParmeterTable();
            int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

            try
            {
                dtParms.Rows.Add(iServerNumber, "ProductKey", "STRING", g_SharedData.WSP_AGENT_SETTING.strServerKey);
                
                byte[] btResult = AgentLogin.AgentLogin(g_SharedData.WSP_AGENT_SETTING.strServerKey);

                if (btResult == null)
                    return false;

                DataTable dtAgentLogIn = new DataTable();
                dtAgentLogIn = BytesToDataTable(btResult);

                if (dtAgentLogIn == null)
                    return false;

                foreach (DataRow dr in dtAgentLogIn.Rows)
                {
                    g_SharedData.WSP_AGENT_SETTING.iServerNumber = Convert.ToInt32(dr["ServerNum"].ToString());
                    g_iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

                    if (g_SharedData.WSP_AGENT_SETTING.iServerNumber < 1)       // No valid product key for this server.
                    {
                        WSPEvent.WriteEvent("Service Point Agent has failed to validate product key.", "E", 1169);
                        return false;
                    }

                    g_SharedData.WSP_AGENT_SETTING.strServerType = dr["ServerType"].ToString();
                    SetServerType();

                    g_SharedData.WSP_AGENT_SETTING.strDisplayName = dr["DisplayName"].ToString();
                    g_SharedData.WSP_AGENT_SETTING.strDisplayGroup = dr["DisplayGroup"].ToString();
                    g_SharedData.WSP_AGENT_SETTING.iCompanyNumber = Convert.ToInt32(dr["CompanyNum"]);
                }

                // WHEN AGENT FAILS TO GET SERVER NUMBER, OR IT'S EXPIRED.
                if (g_SharedData.WSP_AGENT_SETTING.iServerNumber <= 0)
                {
                    WSPEvent.WriteEvent("License for Service Point Agent has been expired, or failed to validate the product key.", "E", 1184);
                    return false;
                }
                
                return true;

            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Service Point Agent has failed to access the database to validate this server. - " + ex.Message, "E", 1169);
                return false;
            }

        }

        private void SetServerType()
        {
            string strServerType;
            strServerType = g_SharedData.WSP_AGENT_SETTING.strServerType.ToUpper();

            if (strServerType == "SQL")
                g_SharedData.WSP_AGENT_SETTING.strServerType = "SQL";

            if (strServerType == "WEB")
                g_SharedData.WSP_AGENT_SETTING.strServerType = "Web";

            if (strServerType == "BIZTALK")
                g_SharedData.WSP_AGENT_SETTING.strServerType = "BizTalk";

            if (strServerType == "SharePoint")
                g_SharedData.WSP_AGENT_SETTING.strServerType = "SharePoint";

            if (strServerType == "WINDOWS")
                g_SharedData.WSP_AGENT_SETTING.strServerType = "Windows";

            // Setting a default server type to be Windows.
            if (strServerType != "WINDOWS" && strServerType != "BIZTALK" && strServerType != "WEB" && strServerType != "SQL" && strServerType != "SharePoint")
            {
                g_SharedData.WSP_AGENT_SETTING.strServerType = "Windows";
                WSPEvent.WriteEvent("The ServerType in ServicePoint.Settings.xml is invalid, SHST will be running under Windows type. Your input : " + strServerType, "W", 1138);
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
