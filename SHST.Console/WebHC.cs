
using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;

namespace WebReqTest
{
    class WebHealthCheck
    {
        // get
        //public string strHostname;
        //public string strConnectionString;
        //public int iServerNumber = 0;

        public SharedData g_SharedData = new SharedData();

        public string strR44_AlertDesc;            //Critical
        public string strR45_AlertDesc;            //Warning        

        public DateTime g_tmNow = new DateTime();
        public Guid g_GlobalGUID = new Guid();

        //public struct HC_Settings
        //{
        //    public int iHC_Interval;
        //    public int iHC_Timeout;
        //    public bool bHC_Enabled;
        //    public DataTable dtHC_URL;
        //}
        
        //public HC_Settings HC_Setting = new HC_Settings();
        
        //set
        public int g_iMaxWebAlertLevel = 0;

        EventRecorder WSPEvent = new EventRecorder();

        public bool bHasAlerts = false;
        //private DataTable dtAlerts = new DataTable();
        public int iMaxLevel = 0;

        //private void InitializeWebHC()
        //{
        //    dtAlerts.Columns.Clear();
        //    dtAlerts.Columns.Add(new DataColumn("ReasonCode", typeof(string)));
        //    dtAlerts.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
        //    dtAlerts.Columns.Add(new DataColumn("Hostname", typeof(string)));
        //    dtAlerts.Columns.Add(new DataColumn("InstanceName", typeof(string)));
        //    dtAlerts.Columns.Add(new DataColumn("PValue", typeof(float)));
        //    dtAlerts.Columns.Add(new DataColumn("AlertStatus", typeof(int)));
        //    dtAlerts.Columns.Add(new DataColumn("AlertDescription", typeof(string)));
        //    dtAlerts.Columns.Add(new DataColumn("AlertRecordID", typeof(Guid)));

        //}

        public void RunURLHealthCheck()
        {
            DataTable dtAlerts = new DataTable();

            //if (dtAlerts.Columns.Count < 1)
            //    InitializeWebHC();
            //if (dtAlerts.Rows.Count > 0)
            //    dtAlerts.Rows.Clear();

            string strURL;
            iMaxLevel = 0;
            bHasAlerts = false;

            if (g_SharedData.HC_SETTING.dtHC_URL!= null)
            {
                if (g_SharedData.HC_SETTING.dtHC_URL.Rows.Count > 0)
                {
                    foreach (DataRow dr in g_SharedData.HC_SETTING.dtHC_URL.Rows)
                    {
                        strURL = dr["HC_URL"].ToString();
                        dtAlerts = PingWebSite(strURL);
                    }

                    if (bHasAlerts)
                    {
                        //InsertAlertsToDB();
                        g_iMaxWebAlertLevel = 3;
                        InsertAlerts(dtAlerts);
                        //UpdateHostStatus();         // Update Host Status only when URL HC fails.
                    }
                    else
                        g_iMaxWebAlertLevel = 0;
                }
            }
        }

        private DataTable PingWebSite(string strURL)        //returns true if ok.
        {
            DateTime tmNow = g_tmNow;
            DataTable dtAlerts = new DataTable();

            dtAlerts.Columns.Clear();
            dtAlerts.Columns.Add(new DataColumn("ReasonCode", typeof(string)));
            dtAlerts.Columns.Add(new DataColumn("TimeIn", typeof(DateTime)));
            dtAlerts.Columns.Add(new DataColumn("Hostname", typeof(string)));
            dtAlerts.Columns.Add(new DataColumn("InstanceName", typeof(string)));
            dtAlerts.Columns.Add(new DataColumn("PValue", typeof(float)));
            dtAlerts.Columns.Add(new DataColumn("AlertStatus", typeof(int)));
            dtAlerts.Columns.Add(new DataColumn("AlertDescription", typeof(string)));
            dtAlerts.Columns.Add(new DataColumn("AlertRecordID", typeof(Guid)));

            try
            {
                HttpWebRequest myHttpWebRequest = (HttpWebRequest)WebRequest.Create(strURL);

                myHttpWebRequest.Timeout = g_SharedData.HC_SETTING.iHC_Timeout * 1000;
                myHttpWebRequest.UserAgent = "Mozilla/5.0";

                HttpWebResponse myHttpWebResponse;
                myHttpWebResponse = (HttpWebResponse)myHttpWebRequest.GetResponse();

//                Console.WriteLine("code:" + myHttpWebResponse.StatusCode.ToString());

                if (myHttpWebResponse.StatusCode == HttpStatusCode.OK)
                {
                    myHttpWebResponse.Close();
                }

                return dtAlerts;
                
            }
            catch (WebException e)
            {
                bHasAlerts = true;

                if (e.Status == WebExceptionStatus.ConnectFailure || e.Status == WebExceptionStatus.ProtocolError)
                {
                    string strAlert = strR44_AlertDesc + " URL:" + strURL;
                    if(strAlert.Length >= 250)
                        strAlert = strAlert.Substring(0, 249);
                    dtAlerts.Rows.Add("R044", tmNow, g_SharedData.WSP_AGENT_SETTING.strDisplayName, "", 0, 3, strAlert, g_GlobalGUID.ToString());
                    iMaxLevel = 3;
                }
                else
                {
                    if (e.Status == WebExceptionStatus.Timeout)
                    {
                        string strAlert = strR45_AlertDesc + "[Time Out Error] URL:" + strURL;
                        if (strAlert.Length >= 250)
                            strAlert = strAlert.Substring(0, 249);
                        dtAlerts.Rows.Add("R045", tmNow, g_SharedData.WSP_AGENT_SETTING.strDisplayName, "", 0, 2, strAlert, g_GlobalGUID.ToString());
                        if(iMaxLevel != 3)
                            iMaxLevel = 2;
                    }
                    else
                    {
                        string strAlert = strR45_AlertDesc + "[" + e.Status.ToString() + "] URL:" + strURL;
                        if (strAlert.Length >= 250)
                            strAlert = strAlert.Substring(0, 249);
                        dtAlerts.Rows.Add("R045", tmNow, g_SharedData.WSP_AGENT_SETTING.strDisplayName, "", 0, 2, strAlert, g_GlobalGUID.ToString());
                        if (iMaxLevel != 3)
                            iMaxLevel = 2;
                    }
                }
                return dtAlerts;
            }

        }


        private void InsertAlerts(DataTable dtAlerts)
        {
            DataAccess.WSP_Op WSPDB = new DataAccess.WSP_Op();
            WSPDB.g_SharedData = g_SharedData;
            WSPDB.InsertAlertsToDB(dtAlerts);
        }

///END OF CLASS
    }
}


