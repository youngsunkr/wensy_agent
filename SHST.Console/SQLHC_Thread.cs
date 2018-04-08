using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;

using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.IO;

class SQLHC_Thread
{
    public string strAlertDescription;
    public int g_iSQLMaxAlertLevel = 0;

    public int iMaxLevel = 0;
    public Guid guidSQLTrace = new Guid();
    public DateTime g_dtTimeIn = new DateTime();
    public DateTime g_dtTimeIn_UTC = new DateTime();

    EventRecorder WSPEvent = new EventRecorder();
    public SharedData g_SharedData = new SharedData();

    public string strReadRunningQuery = "";

    public int CheckAndUpdateSQLServerStatus()
    {
        DataTable dtHeartBeat = new DataTable();
        SqlConnection conReadQuery = new SqlConnection(g_SharedData.LOCAL_SQL_SETTING.strConnectionString);
        string sCommand = "SELECT 1";

        TimeSpan tsElapsed = new TimeSpan();
        DateTime dtSaved = new DateTime();
        DateTime dtNow = new DateTime();
        int iTimeSpent = 0;

        using (SqlDataAdapter da = new SqlDataAdapter(sCommand, conReadQuery))
        {
            try
            {
                dtSaved = DateTime.Now; // 아래 구간의 실행 시간을 구한다.
                /////////////////////////////////////////////////////////////////////////////
                conReadQuery.Open();

                int iRows = da.Fill(dtHeartBeat);

                /////////////////////////////////////////////////////////////////////////////

                dtNow = DateTime.Now;
                tsElapsed = dtNow - dtSaved;
                iTimeSpent = tsElapsed.Milliseconds;

                if (iRows > 0)
                    return iTimeSpent;
                else
                    return -1;
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Service Point Agent just alerted the failure of running SQL Heat Beat Query. - " + ex.Message, "W", 1177);
                return -1;
            }
            finally
            {
                conReadQuery.Close();
            }
        }
    }


    public void CheckRunningQueriesSP()
    {

            DataTable dtQueryList = new DataTable();
            DataTable dtRunningQueries = new DataTable();
            int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;

            try
            {
                if (String.IsNullOrEmpty(strReadRunningQuery))
                {

                    WSP.Console.WS_AGENTINITIAL.AgentInitial QueryDefineList = new WSP.Console.WS_AGENTINITIAL.AgentInitial();
                    QueryDefineList.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AgentInitial.asmx";

                    DataTable dtParms = SetParmeterTable();
                
                    byte[] dtResult = QueryDefineList.QueryDefineList(iServerNumber.ToString());

                    if (dtResult == null)
                        return;

                    dtQueryList = BytesToDataTable(dtResult);

                    foreach (DataRow dr in dtQueryList.Rows)
                    {
                        if (g_SharedData.LOCAL_SQL_SETTING.bEnableCollectingRunningQueries)
                        {
                            if (Convert.ToInt32(dr["QueryID"].ToString()) == 1)
                                strReadRunningQuery = dr["Query"].ToString();
                        }
                    }
                }

                dtRunningQueries = CollectRunningQueries(strReadRunningQuery);

                if (dtRunningQueries != null)
                    if(dtRunningQueries.Rows.Count > 0)
                        InsertRunningQueries(dtRunningQueries);

            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("WSP failed to collect the information of running queries. - " + ex.Message, "W", 1179);
            }

        
    }


    //실행중인 쿼리 수집 (느린쿼리 얼랏 기능 추가해야함)
    void InsertRunningQueries(DataTable dtRunningQueries)
    {
        int iServerNumber = g_SharedData.WSP_AGENT_SETTING.iServerNumber;    

        try
        {

            WSP.Console.WS_QUERY.AddRunningQueries wsInsert = new WSP.Console.WS_QUERY.AddRunningQueries();
            wsInsert.Url = g_SharedData.WSP_AGENT_SETTING.strWS_URL + "/AddRunningQueries.asmx";
            wsInsert.Timeout = 15000;

            wsInsert.InsertRunningQueries(DataTableToBytes(dtRunningQueries), iServerNumber, g_dtTimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_dtTimeIn_UTC.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch (Exception ex)
        {
            WSPEvent.WriteEvent("Checking running quries has failed. - " + ex.Message, "E", 1183);
        }

    }


    DataTable CollectRunningQueries(string strQueryString)
    {
        DataTable dtQueryResult = new DataTable();
        using (SqlConnection con1 = new SqlConnection(g_SharedData.LOCAL_SQL_SETTING.strConnectionString))
        {
            try
            {
                SqlDataAdapter da = new SqlDataAdapter();

                da.SelectCommand = new SqlCommand("sp_executesql", con1);
                da.SelectCommand.CommandType = CommandType.StoredProcedure;
                da.SelectCommand.Parameters.AddWithValue("@statement", strQueryString);

                da.Fill(dtQueryResult);
                return dtQueryResult;

            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("WSP failed to collect the information of running queries. - " + ex.Message, "W", 1178);
                return null;
            }
            finally
            {
                con1.Close();
            }
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
