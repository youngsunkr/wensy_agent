using System;
using System.Collections.Generic;
using System.Text;
using System.Data;

public class SharedData
{
    public const int MAX_APP_POOLS = 100; 
    public const int MAX_SITES = 100;
    public const int MAX_WEB_REQUESTS = 300;

    public struct stSysInfo
    {
        public int iNumberOfWorkers;
        public int iNumberOfSites;
        public int iWinVer;
        public string strWinVer;
        public int iNumberOfProcessors;
        public bool bIs64bit;
        public float flRAMSize;
        public string strComputerName;
        public bool bIsIIS;
        public string strIPAddress;
    }

    public stSysInfo SYSINFO = new stSysInfo();

    //public DataTable dtWPList = new DataTable();
    //public DataTable dtSiteList = new DataTable();
    //public DataTable dtJavaWP = new DataTable();

    public struct stWPList
    {
        public string strInstanceName;
        public string strPID;
        public string strAppPoolDesc;
    }

    public stWPList[] arrWPList = new stWPList[MAX_APP_POOLS];

    public struct stSiteList
    {
        public string strSiteID;
        public string strSiteDesc;
    }

    public stSiteList[] arrSiteList = new stSiteList[MAX_SITES];

    public struct stJavaWPList
    {
        public string strInstanceName;
        public string strPID;
    }
    
    public stJavaWPList[] arrJavaWPList = new stJavaWPList[MAX_APP_POOLS];

    public struct stWebRequests
    {
        public string strURL;
        public int iTimeTaken;
        public string strClientIP;
    }

    public stWebRequests[] arrWebRequests = new stWebRequests[MAX_WEB_REQUESTS];

    public struct stWebInfo
    {
        public int iNumberOfRequests;
        public int iNumberOfAppPools;
        public int iNumberOfJavaPools;
        public int iNumberOfSites;
    }

    public stWebInfo WEBINFO = new stWebInfo();

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // APP SETTINGS
    ///////////////////////////////////////////////////////////////////////////////////////////////

    #region

    public struct WSP_AGENT_SETTINGS
    {
        public string strMachineID;
        public string strWS_URL;
        public int iPerfLogCollectInterval;
        public string strServerType;
        public string strDisplayName;
        public string strDisplayGroup;
        public int iMaxAgentMemorySizeMB;
        public int iServerNumber;
        public string strServerKey;
        public string strMemberID;
        public int iCompanyNumber;
        public int iRemainingMinutes;
        public string iBuildNumber;
        public float iPricePerMinute;
    }

    public struct WSP_LICENSE
    {
        public bool bIsLicensed;
        public bool bIsTrial;
        public int iMaxServers;
        public string strServerType;
        public bool bIsUnlimited;
    }

    public struct WEB_SETTINGS
    {
        public bool bIISLogAnalysis;
        public string strLogFilesDirectory;
        public string strHostHeader;
        public string strLastLogFileName;
        public int iMaxConnectionPoolSize;
    }

    public struct HC_SETTINGS
    {
        public int iHC_Interval;
        public int iHC_Timeout;
        public bool bHC_Enabled;
        public DataTable dtHC_URL;
    }

    public struct LOCAL_SQL_SETTINGS
    {
        public string strServerName;
        public string strAuthentication;
        public string strAccount;
        public string strPassword;
        public string strEncrypted;
        public string strConnectionString;
        public string strInstanceName;
        public int iQueryInterval;
        public bool bEnableCollectingRunningQueries;
    }

    public HC_SETTINGS HC_SETTING = new HC_SETTINGS();
    public LOCAL_SQL_SETTINGS LOCAL_SQL_SETTING = new LOCAL_SQL_SETTINGS();
    public WSP_AGENT_SETTINGS WSP_AGENT_SETTING = new WSP_AGENT_SETTINGS();
    public WEB_SETTINGS WEB_SETTING = new WEB_SETTINGS();
    public WSP_LICENSE LICENSE_SETTING = new WSP_LICENSE();
    
    #endregion

}

