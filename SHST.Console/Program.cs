
using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;

using System.Threading;

namespace SHST.Console
{
    class Service1
    {
        //private System.ComponentModel.Container components = null;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // declarations, copy this block for cosole mode test, 
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private ServiceConfigurations.AppConfigs AppConfig = new ServiceConfigurations.AppConfigs();
        private DataAccess.WSP_Op WSPDB = new DataAccess.WSP_Op();
     
        private PerfReader PerfReaders = new PerfReader();
        private SharedData g_SharedData = new SharedData();
        private WebInformation WebInfo = new WebInformation();

        private RulesAlerts.RulesAndAlerts clsAlerts = new RulesAlerts.RulesAndAlerts();
        private BizTalkRulesAlerts clsBTSChk = new BizTalkRulesAlerts();
        //private SQLHC_Thread clsSQLHC = new SQLHC_Thread();

        WebReqTest.WebHealthCheck WebHC = new WebReqTest.WebHealthCheck();

        bool bFirstRunIISLog = true;
        bool bFirstRunPerfLog = true;

        DateTime dtAgentStartTime = new DateTime();
        private Guid g_GlobalGUID = new Guid();
        private DateTime g_TimeIn = new DateTime();
        private DateTime g_TimeIn_UTC = new DateTime();

        EventRecorder WSPEvent = new EventRecorder();

        private volatile bool bIsServiceReady = false;
        private volatile bool bIsExpired = false;

        int iAgentHighCPUCounter = 0;

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //  End of declarations
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        

        static void Main(string[] args)
        {
            
            /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            ////////    SHST Main, Keep Running Once... COPY & PASTE from HERE
            /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            
            Service1 WSPMAIN = new Service1();

            if (!WSPMAIN.AppConfig.LoadXMLConfig())
                return;

            WSPMAIN.g_SharedData = WSPMAIN.AppConfig.g_SharedData;
        
            System.Threading.TimerCallback tcService;
            System.Threading.Timer tmService;

            tcService = new TimerCallback(WSPMAIN.InitService);
            tmService = new System.Threading.Timer(tcService, null, 1000, Timeout.Infinite);

            ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // END OF COPY & PASTE BLOCK
            // System.ServiceProcess.ServiceBase.Run(ServicesToRun);
            //////////////////////////////////////////////////////// only for console application //

            System.Console.ReadLine();
            //////////////////////////////////////////////////////// only for console application //

        }

        
      private void InitService(object obj)
      {

          //////////////////////////////////////////////////////////////////
          //         Shell commands &  List of Web Site
          //////////////////////////////////////////////////////////////////

          bIsServiceReady = false;

          if (dtAgentStartTime == null)
              dtAgentStartTime = DateTime.Now;

          AppConfig.GetSystemInformation();

          g_SharedData.WEBINFO.iNumberOfAppPools = 0;
          g_SharedData.WEBINFO.iNumberOfJavaPools = 0;
          g_SharedData.WEBINFO.iNumberOfSites = 0;

          WSPDB.g_SharedData = g_SharedData;

          bool bRegisterd = false;
          int iRetryCount = 1;

          do
          {
              if (PerfReaders.IsPerformanceCounterReady() && WSPDB.IsServiceReady())    // p_AgentLogin OK
              {
                  g_SharedData.WSP_AGENT_SETTING.iServerNumber = WSPDB.g_iServerNumber;       // GET SERVER NUMBER when it's checking WSP DB READY.

                  if (WSPDB.StartWSPDBOperation())            //StartSHSTDBOpertaion - includes license checking, and registration of this server.
                  {
                      bRegisterd = true;
                  }
                  else
                  {
                      bRegisterd = false;
                      Thread.Sleep(5000);
                  }

                  iRetryCount = iRetryCount + 1;
                  if (iRetryCount > 10)
                  {
                      WSPEvent.WriteEvent("Service Point Agent can not access Service Point Server.", "E", 1107);
                      iRetryCount = 1;
                  }

                  while (!WSPDB.ReadPerformanceCounters())
                      Thread.Sleep(1000);      //Read required counters from DB

                  while (!WSPDB.ReadAlertRules())              //Read alert rules
                      Thread.Sleep(1000);

                  bIsServiceReady = true;
              }
              else                                                       // if it can't read perfmon, and can't access database.
              {
                  bIsServiceReady = false;
                  iRetryCount = iRetryCount + 1;
                  if (iRetryCount > 10)
                  {
                      WSPEvent.WriteEvent("Service Point Agent can not access Service Point Server, or read performance counters.", "E", 1135);
                      iRetryCount = 1;
                  }
                  Thread.Sleep(5000);
              }
          }
          while (!bRegisterd);

          Thread workerThread = new Thread(HandlingPerfValues);
          Thread loggerThread = new Thread(LoggerWork);
          Thread HCThread = new Thread(WebHCWorker);
          Thread SQLChkThread = new Thread(SQLQueryMonitoringWorker);
          //Thread SQLHCThread = new Thread(SQLHCWorker);

          if (bRegisterd)
          {
              int iInterval = g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval * 1000;

              g_GlobalGUID = Guid.NewGuid();
              g_TimeIn = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
              g_TimeIn_UTC = Convert.ToDateTime(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

              // Run/Stop/Resume performance value collection thread.
              System.Threading.TimerCallback tc;
              System.Threading.Timer tm;

              tc = new TimerCallback(ObserverCheck);
              tm = new System.Threading.Timer(tc, null, iInterval, iInterval);

              if (g_SharedData.WSP_AGENT_SETTING.strServerType == "Web")
              {
                  // Refresh Process Memory status & running applications every 3 seconds.
                  System.Threading.TimerCallback tcProcess;
                  System.Threading.Timer tmProcess;

                  tcProcess = new TimerCallback(ProcessMemoryCheck);
                  tmProcess = new System.Threading.Timer(tcProcess, null, iInterval + 3000, 5000);
              }

              // Reload Application Configurations every 5 minutes
              System.Threading.TimerCallback tcAppConfg;
              System.Threading.Timer tmAppConfig;

              tcAppConfg = new TimerCallback(RefreshWSPService);
              tmAppConfig = new System.Threading.Timer(tcAppConfg, null, 300000, 300000);
              
            
              //////////////////////////////////////////////////////////////////
              //          Worker Threads
              //////////////////////////////////////////////////////////////////            

              _bThreadFlag = true;

              //Performance counter thread start
              workerThread.Start();

              if (g_SharedData.WEB_SETTING.bIISLogAnalysis && (g_SharedData.WSP_AGENT_SETTING.strServerType == "Web"))
              {
                  _bLoggerThreadFlag = true;
                  loggerThread.Start();
              }

              if (g_SharedData.HC_SETTING.bHC_Enabled && (g_SharedData.WSP_AGENT_SETTING.strServerType == "Web"))
              {
                  _bHCThreadFlag = true;
                  HCThread.Start();
              }

              if (g_SharedData.WSP_AGENT_SETTING.strServerType.Contains("SQL"))
              {
                  SQLChkThread.Start();
                  //SQLHCThread.Start();
              }
          }

          while (!_shouldStop)
              Thread.Sleep(10000);

          if (_shouldStop)
          {
              workerThread.Join();

              if (g_SharedData.WEB_SETTING.bIISLogAnalysis && (g_SharedData.WSP_AGENT_SETTING.strServerType == "Web"))
                  loggerThread.Join();

              if (g_SharedData.HC_SETTING.bHC_Enabled && (g_SharedData.WSP_AGENT_SETTING.strServerType == "Web"))
                  HCThread.Join();

              if (g_SharedData.WSP_AGENT_SETTING.strServerType.Contains("SQL"))
              {
                  SQLChkThread.Join();
                  //SQLHCThread.Join();
              }
          }
      }

        private void ObserverCheck(object obj)
        {
            if (!bIsServiceReady)
                return;

            g_GlobalGUID = Guid.NewGuid();
            g_TimeIn = DateTime.Now;
            g_TimeIn_UTC = DateTime.UtcNow;
            //g_TimeIn = Convert.ToDateTime(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            //g_TimeIn_UTC = Convert.ToDateTime(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

            _bThreadFlag = true;          // RESUME PERF DATA THREAD 

            bool bIsMaxAgentMemory = false;

            PerfReaders.g_SharedData = g_SharedData;

            //bIsMaxAgentMemory = PerfReaders.ReadWSPAgentMemorySize();

            if (bIsMaxAgentMemory)
            {
                PerfReaders.StopPerformanceReaders();

                _shouldStop = true;
                _bThreadFlag = false;
                _bLoggerThreadFlag = false;
                _bHCThreadFlag = false;

                WSPEvent.WriteEvent("Service Point Agent reached its maximum memory size. This service process will be shutdowned, and resumed", "W", 1168);

                Thread.Sleep(10000);

                Environment.Exit(1);
            }


            // test code - to enable agent CPU chk.
            bool bIsHighCPU = false;

            //bIsHighCPU = PerfReaders.CheckSPAgentCPUStatus();

            if (bIsHighCPU)
            {
                iAgentHighCPUCounter++;

                if (iAgentHighCPUCounter == 3)   // 3회 연속
                {
                    g_SharedData.WEB_SETTING.bIISLogAnalysis = false;
                    WSPEvent.WriteEvent("Because of high CPU utilization of Service Point Agent, it will turn off [Enable IIS Log Analysis] option until restarting this service.", "W", 1208);
                }

                if (iAgentHighCPUCounter == 12)  // 3분 이상 
                {
                    WSPEvent.WriteEvent("Because of high CPU utilization of Service Point Agent, it will turn off [Enable IIS Log Analysis] option in its settings.", "W", 1207);

                    IISLogHandler.IISLogOperation LogOp = new IISLogHandler.IISLogOperation();
                    LogOp.TurnOffIISLogAnalysisOption();

                    WSPEvent.WriteEvent("Service Point Agent reached its maximum CPU Utilzation. This service process will be shutdowned, and resumed", "W", 1206);

                    PerfReaders.StopPerformanceReaders();

                    _shouldStop = true;
                    _bThreadFlag = false;
                    _bLoggerThreadFlag = false;
                    _bHCThreadFlag = false;

                    Thread.Sleep(10000);

                    Environment.Exit(1);
                }
            }
            else
                iAgentHighCPUCounter = 0;


        }


        private void ProcessMemoryCheck(object obj)
      {

          if (bIsServiceReady)
          {
              if (!clsAlerts.bCheckProcess)
                  return;

              //clsSysStatus.SysInfo = AppConfig.Sys_Info;              
              WebInfo.g_SharedData = g_SharedData;
              clsAlerts.g_SharedData = g_SharedData;

              try
              {
                  // Build list of running applications
                  if (g_SharedData.WSP_AGENT_SETTING.strServerType == "Web")
                  {
                      if (g_SharedData.SYSINFO.iWinVer != 2003)          // only available in IIS7.x
                          WebInfo.GetRunningWebApplications();

                      if (g_SharedData.WEBINFO.iNumberOfSites < 1)
                          WebInfo.GetWebSiteList();

                      WebInfo.GetWorkerProcessList();

                      PerfReaders.ReadW3WPMemory();                           // dtW3WPMemory, keeps process memory size for 60 seconds.
                      if (PerfReaders.dtW3WPMemory.Rows.Count > 0)
                      {
                          clsAlerts.CheckProcessMemory(PerfReaders.dtW3WPMemory.Copy());
                      }
                  }

              }
              catch (Exception ex)
              {
                  WSPEvent.WriteEvent("WSP can't find a list of web applications. -" + ex.Message, "I", 1004);
              }

          }

          Thread.Sleep(1000);
      }

    
      private void RefreshWSPService(object obj)           //run every 5 minutes
      {

          try
          {
              if (bIsServiceReady)
              {
                  AppConfig.LoadXMLConfig();

                  WSPDB.ReadPerformanceCounters();            //Read required counters from DB
                  WSPDB.ReadAlertRules();                     //Read alert rules

                  if (WSPDB.dtPCID.Rows.Count > 0)
                  {
                      PerfReaders.dtPCID = WSPDB.dtPCID.Copy();
                      //PerfReaders.SetASPNETCategoryNames();
                      //clsAlerts.dtPCID = PerfReaders.dtPCID.Copy();
                  }

                  if (WSPDB.dtInstanceList.Rows.Count > 0)
                      PerfReaders.dtInstanceList = WSPDB.dtInstanceList.Copy();

                  if(g_SharedData.WSP_AGENT_SETTING.strServerType.ToUpper() == "WEB")
                    WebInfo.GetWebSiteList();

                  if (WSPDB.dtAlertRules.Rows.Count > 0)
                      clsAlerts.dtAlertRules = WSPDB.dtAlertRules.Copy();

              }

              bool bSavedStatus = bIsServiceReady;

              if (!bIsExpired)
                  bIsServiceReady = WSPDB.IsServiceReady();         //Check Current Status (AGENT LICENSE, Remaining minutes.)

              if (PerfReaders.IsPerformanceCounterReady() && bIsServiceReady)
              {
                  if (!bSavedStatus)                // Changed status -> Not Ready to Ready
                  {
                      bool bRegisterd = false;
                      do
                      {
                          if (WSPDB.StartWSPDBOperation())            // Register this host again, for failover, db transfer while this agent is running.                  
                              bRegisterd = true;
                          else
                              Thread.Sleep(5000);
                      } while (!bRegisterd);
                  }
              }
              else
              {
                  bIsServiceReady = false;
              }
          }
          catch (Exception ex)
          {
              WSPEvent.WriteEvent("Refreshing Service Point task has an error. - " + ex.Message, "W", 1199);
          }

          Thread.Sleep(1000);
      }

      public void LoggerWork()
      {
          while (!bIsServiceReady)
          {
              Thread.Sleep(1000);
          }

          IISLogHandler.IISLogOperation LogOp = new IISLogHandler.IISLogOperation();

          if (g_SharedData.WEB_SETTING.bIISLogAnalysis)
          {
              do
              {
                  LogOp.g_SharedData = g_SharedData;

                  try
                  {
                      if (bIsServiceReady)
                      {
                          if (LogOp.BuildAnalsysIISLog())
                          {
                              if (LogOp.AnalyzeIISLogCommands())
                              {
                                  LogOp.LoadAnalysisFilesToDataTable();
                                  LogOp.InsertAllToDB();

                                  if (bFirstRunIISLog)
                                  {
                                      WSPEvent.WriteEvent("Service Point Agent started collecting IIS Log successfully.", "I", 1002);
                                      bFirstRunIISLog = false;
                                  }
                              }
                          }
                      }

                  }
                  catch (Exception ex)
                  {
                      WSPEvent.WriteEvent("Service Point Agent failed to analyze IIS Log. - " + ex.Message, "W", 1161);
                  }

                  Thread.Sleep(300000);                // To run every 5 minutes 
              }
              while (g_SharedData.WEB_SETTING.bIISLogAnalysis && _bLoggerThreadFlag);
          }
      }

      public void WebHCWorker()
      {
          while (!bIsServiceReady)
          {
              Thread.Sleep(1000);
          }

          int iInterval = 0;
          bool bIsFirstRun = true;

          WebHC.g_SharedData = g_SharedData;

          try
          {
              var Rows = WSPDB.dtAlertRules.Select("ReasonCode = 'R044'");
              foreach (DataRow dr in Rows)
                  WebHC.strR44_AlertDesc = dr["ReasonCodeDesc"].ToString();

              var Rows2 = WSPDB.dtAlertRules.Select("ReasonCode = 'R045'");
              foreach (DataRow dr2 in Rows2)
                  WebHC.strR45_AlertDesc = dr2["ReasonCodeDesc"].ToString();
          }
          catch (Exception ex)
          {
              string strMessage;
              strMessage = "Service Point Agent failed to read HC Rules. " + ex.Message;
              WSPEvent.WriteEvent(strMessage, "W", 1121);
              WebHC.strR44_AlertDesc = "The site failed to response for the health check request.";
              WebHC.strR45_AlertDesc = "The site responsed with an error for the health check request.";
          }

          while (g_SharedData.HC_SETTING.bHC_Enabled && _bHCThreadFlag)
          {
              if (bIsServiceReady)
              {
                  //WebHC.strConnectionString = g_SharedData.WSP_AGENT_SETTING.strConnectionString;
                  //WebHC.HC_Setting.dtHC_URL = g_SharedData.HC_SETTING.dtHC_URL.Copy();
                  //WebHC.HC_Setting.iHC_Interval = g_SharedData.HC_SETTING.iHC_Interval;
                  //WebHC.HC_Setting.iHC_Timeout = g_SharedData.HC_SETTING.iHC_Timeout;
                  //WebHC.HC_Setting.bHC_Enabled = g_SharedData.HC_SETTING.bHC_Enabled;
                  try
                  {
                      iInterval = g_SharedData.HC_SETTING.iHC_Interval * 1000;
                      if (iInterval < 1000)
                          iInterval = 5000;

                      if (g_SharedData.HC_SETTING.dtHC_URL != null)
                      {
                          if (g_SharedData.HC_SETTING.dtHC_URL.Rows.Count > 0)
                          {
                              WebHC.g_GlobalGUID = g_GlobalGUID;
                              WebHC.g_tmNow = g_TimeIn;

                              WebHC.RunURLHealthCheck();
                              //                          clsAlerts.bHasHCAlert = WebHC.bHasAlerts;
                              clsAlerts.g_iWebMaxAlertLevel = WebHC.g_iMaxWebAlertLevel;

                              if (bIsFirstRun)
                              {
                                  string strMessage;
                                  strMessage = "URL Health Check Monitoring has been started, using the configured Health Check URLs";
                                  WSPEvent.WriteEvent(strMessage, "I", 1003);
                              }
                              bIsFirstRun = false;
                          }
                      }
                  }
                  catch (Exception ex)
                  {
                      WSPEvent.WriteEvent("URL Health Check Monitoring fuction error - " + ex.Message, "W", 1301);
                  }
              }

              Thread.Sleep(iInterval);
          }
      }

        /*
      public void SQLHCWorker()
      {
          while (!bIsServiceReady)
              Thread.Sleep(100);

          bool bIsFirstRun = true;
          int iInterval = 0;

          var Rows = WSPDB.dtAlertRules.Select("ReasonCode = 'R151'");
          foreach (DataRow dr in Rows)
              clsSQLHC.strAlertDescription = dr["ReasonCodeDesc"].ToString();

          clsAlerts.dtSQLHBAlerts = clsAlerts.dtAlerts.Clone();

          ///////////////////////////////////////////////////////////////////////////////////////////////////
          // FIX : 설정된 간격마다 돌도록 TimeSpan 점검. 기존에는 Interval 만큼 쉬다가 돌았음.

          TimeSpan tsElapsedTime = new TimeSpan();
          DateTime dtSavedTime = DateTime.Now;
          DateTime dtNow = new DateTime();
          int iTimeToSleep = 0;
          int iHCQueryTime = 0;
          ///////////////////////////////////////////////////////////////////////////////////////////////////

          while (!_shouldStop)
          {
              if (bIsServiceReady)
              {
                  dtNow = DateTime.Now;
                  clsSQLHC.g_SharedData = g_SharedData;

                  iInterval = g_SharedData.LOCAL_SQL_SETTING.iQueryInterval * 1000;

                  if (iInterval < 1000)
                      iInterval = 2000;

                  try
                  {

                      clsSQLHC.guidSQLTrace = g_GlobalGUID;                // To Sync GUID for trace & alerts                      
                      clsSQLHC.g_dtTimeIn = g_TimeIn;
                      clsSQLHC.g_dtTimeIn_UTC = g_TimeIn_UTC;


                        // SELECT 1 쿼리 수행시간을 리턴한다. 실패하면 -1이 리턴됨.
                        iHCQueryTime = clsSQLHC.CheckAndUpdateSQLServerStatus();
                      if (iHCQueryTime < 0)            // To notify a critical SQL Status, check running queries.
                      {
                          clsSQLHC.g_iSQLMaxAlertLevel = 3;
                          clsAlerts.dtSQLHBAlerts.Rows.Add("R151", g_TimeIn.ToString("yyyy-MM-dd HH:mm:ss"), g_SharedData.WSP_AGENT_SETTING.strDisplayName, "", 0, clsSQLHC.g_iSQLMaxAlertLevel, clsSQLHC.strAlertDescription, g_GlobalGUID);
                      }
                      else
                      {
                          clsSQLHC.g_iSQLMaxAlertLevel = 0;
                          clsSQLHC.CheckRunningQueriesSP();
                          WSPDB.g_iHCQueryTime = iHCQueryTime;
                      }

                      if (bIsFirstRun)
                      {
                          string strMessage;
                          strMessage = "Service Point Agent started to monitoring the local SQL Server.";
                          WSPEvent.WriteEvent(strMessage, "I", 1007);
                          bIsFirstRun = false;
                      }
                  }
                  catch (Exception ex)
                  {
                      string strMessage;
                      strMessage = "Service Point Agent failed to monitor local SQL Database. " + ex.Message;
                      WSPEvent.WriteEvent(strMessage, "W", 1154);
                  }
              }

              GC.Collect();

              tsElapsedTime = dtNow - dtSavedTime;
              iTimeToSleep = tsElapsedTime.Milliseconds;
              dtSavedTime = dtNow;

              if (iTimeToSleep > 0 && iTimeToSleep < iInterval)
                  Thread.Sleep(iInterval - iTimeToSleep);
              else
                  Thread.Sleep(iInterval);
              
          }
      }
      */
      
      public void SQLQueryMonitoringWorker()
      {
          while (!bIsServiceReady || WSPDB.dtAlertRules.Rows.Count < 1)
          {
              Thread.Sleep(100);
          }

          clsAlerts.dtSQLHBAlerts = clsAlerts.dtAlerts.Clone();
          DataTable dtQueryAlerts = new DataTable();

          SQLDB_Monitor clsSQLDBChk = new SQLDB_Monitor();

          clsSQLDBChk.g_SharedData = g_SharedData;
          clsSQLDBChk.dtAlertRules = WSPDB.dtAlertRules.Copy();
          //clsSQLDBChk.dtAlerts =  clsAlerts.dtAlerts.Clone();

          int iInterval = 0;
          bool bIsFirstRun = true;
          //clsSQLDBChk.strServerName = Sys_Info.strComputerName;
          DateTime dtStart = DateTime.Now;

          clsSQLDBChk.ReadMonitoringQueriesWS();

          ///////////////////////////////////////////////////////////////////////////////////////////////////
          // FIX : 설정된 간격마다 돌도록 TimeSpan 점검. 기존에는 Interval 만큼 쉬다가 돌았음.

          TimeSpan tsElapsedTime = new TimeSpan();
          DateTime dtSavedTime = DateTime.Now;
          DateTime dtNow = new DateTime();
          int iTimeToSleep = 0;
          int iHCQueryTime = 0;
          ///////////////////////////////////////////////////////////////////////////////////////////////////

          while (!_shouldStop)
          {
              if (bIsServiceReady)
              {
                  clsSQLDBChk.g_SharedData = g_SharedData;

                  int iTimeTaken = DateTime.Now.Subtract(dtStart).Minutes;

                  //iInterval = AppConfig.Local_SQL_Settings.iQueryInterval * 1000;

                  iInterval = g_SharedData.LOCAL_SQL_SETTING.iQueryInterval * 1000;
                  if (iInterval < 1000)
                      iInterval = 2000;

                  clsSQLDBChk.strLocalConnectionString = g_SharedData.LOCAL_SQL_SETTING.strConnectionString;

                  try
                  {
                      if (iTimeTaken > 2)       //minutes
                      {
                          clsSQLDBChk.dtAlertRules = WSPDB.dtAlertRules.Copy();
                          clsSQLDBChk.ReadMonitoringQueriesWS();
                          dtStart = DateTime.Now;
                          //clsSQLHC.strReadRunningQuery = clsSQLDBChk.strReadRunningQuery;   // 실행중인 쿼리(QID=1)를 업데이트하여 SQLHC쿼리에 전달한다.
                      }

                      clsSQLDBChk.g_sharedGUID = g_GlobalGUID;                // To Sync GUID for trace & alerts                      
                      clsSQLDBChk.g_TimeIn = g_TimeIn;
                      clsSQLDBChk.g_TimeIn_UTC = g_TimeIn_UTC;

                        if (clsSQLDBChk.dtChkQuery.Rows.Count > 0)
                      {
                          dtQueryAlerts = clsSQLDBChk.RunChkQueries();
                          if (dtQueryAlerts.Rows.Count > 0)
                          {
                              clsAlerts.dtQueryMonitorAlerts.Merge(dtQueryAlerts);
                              //clsAlerts.dtSQLHBAlerts.Merge(dtQueryAlerts);
                              //foreach (DataRow dr in dtQueryAlerts.Rows)
                              //    clsAlerts.dtSQLHBAlerts.ImportRow(dr);
                          }
                          dtQueryAlerts.Clear();
                      }

                  }
                  catch (Exception ex)
                  {
                      string strMessage;
                      strMessage = "Service Point Agent failed to monitor local SQL Database. " + ex.Message;
                      WSPEvent.WriteEvent(strMessage, "W", 1132);
                  }

                  if (bIsFirstRun)
                  {
                      string strMessage;
                      strMessage = "Service Point Agent started to monitoring the local SQL Server.";
                      WSPEvent.WriteEvent(strMessage, "I", 1006);
                  }
                  bIsFirstRun = false;
              }

              tsElapsedTime = dtNow - dtSavedTime;
              iTimeToSleep = tsElapsedTime.Milliseconds;
              dtSavedTime = dtNow;

              if (iTimeToSleep > 0 && iTimeToSleep < iInterval)
                  Thread.Sleep(iInterval - iTimeToSleep);
              else
                  Thread.Sleep(iInterval);

              GC.Collect();
          }

      }
    

      public void HandlingPerfValues()
      {
          
          bool IsPerfReaderStarted = false;
          bool bSkipTurn = false;
          int iRetryCount = 0;

          while (!bIsServiceReady)
          {
              Thread.Sleep(100);
          }

          PerfReaders.g_dtSharedPValues.Rows.Clear();

          clsAlerts.dtAlertRules = WSPDB.dtAlertRules.Copy();
          //clsAlerts.dtPCID = WSPDB.dtPCID.Copy();

          clsAlerts.InitializeAlertRules();

          while (!_shouldStop)              // end thread by main service
          {
              if (_bThreadFlag && bIsServiceReady)             // observer thread enables this every 15 seconds.
              {
                  try
                  {
                      PerfReaders.g_SharedData = g_SharedData;
                      clsAlerts.g_SharedData = g_SharedData;

                      PerfReaders.dtPCID = WSPDB.dtPCID.Copy();
                      PerfReaders.dtInstanceList = WSPDB.dtInstanceList.Copy();
                      clsAlerts.dtAlertRules = WSPDB.dtAlertRules.Copy();

                  }
                  catch (Exception ex)
                  {
                      WSPEvent.WriteEvent("Service Point can't find the information to collect performance on this system. - " + ex.Message, "W", 1158);
                  }

                  try
                  {
                      PerfReaders.g_dtmNow = g_TimeIn;        // 성능값 리더쓰레드에서, 시간이 변경되면 성능 수집이 시작됨.
                      PerfReaders.g_dtTimeIn_UTC = g_TimeIn_UTC;
                      PerfReaders.g_guidPerfCollectingTurn = g_GlobalGUID;

                      if (!IsPerfReaderStarted)
                      {
                          PerfReaders.StartPerformanceReaders();
                          IsPerfReaderStarted = true;
                      }

                      while (PerfReaders.g_dtSharedPValues.Rows.Count == 0)
                      {
                          Thread.Sleep(200);

                          // To prevent unlimited loop (100 seconds)
                          iRetryCount++;
                          if (iRetryCount > 500)
                          {
                              iRetryCount = 0;
                              bSkipTurn = true;
                              break;
                          }
                      }

                      if (!bSkipTurn)
                      {
                          WSPDB.g_dtTimeIn = PerfReaders.g_dtmNow;
                          WSPDB.g_dtTimeIn_UTC = PerfReaders.g_dtTimeIn_UTC;
                          WSPDB.dtPValues.Rows.Clear();
                          WSPDB.dtPValues = PerfReaders.g_dtSharedPValues.Copy();

                          clsAlerts.g_TimeIn = WSPDB.InsertPerformanceValues();  //To Sync DateTime Values.
                          //clsAlerts.g_TimeIn = WSPDB.g_dtTimeIn;  //To Sync DateTime Values.
                          clsAlerts.g_TimeIn_UTC = WSPDB.g_dtTimeIn_UTC;  //To Sync DateTime Values.
                          //clsAlerts.g_iSQLMaxAlertLevel = clsSQLHC.g_iSQLMaxAlertLevel;
                          clsAlerts.g_iWebMaxAlertLevel = WebHC.g_iMaxWebAlertLevel;
                          clsAlerts.g_guidPerfCollectingTurn = PerfReaders.g_guidPerfCollectingTurn;
                          clsAlerts.dtPValues = PerfReaders.g_dtSharedPValues.Copy();

                          clsAlerts.CheckAlerts(clsAlerts.g_TimeIn, clsAlerts.g_TimeIn_UTC);

                          if (bFirstRunPerfLog)
                          {
                              WSPEvent.WriteEvent("Service Point Agent started to collect performance data successfully.", "I", 1001);
                              bFirstRunPerfLog = false;
                          }
                      }

                      bSkipTurn = false;
                      PerfReaders.g_dtSharedPValues.Rows.Clear();

                  }
                  catch (Exception ex)
                  {
                      WSPEvent.WriteEvent("Unexpected exception happended during checking performance analysis rules. - " + ex.Message, "W", 1145);
                  }
                  _bThreadFlag = false;
              }

              Thread.Sleep(200);     // To wait until _bThreadFlag set to TRUE by observer thread every 15 sec (by default)
              GC.Collect();
          }

      }

      public void RequestStop()
      {
          PerfReaders.StopPerformanceReaders();

          _shouldStop = true;
          _bThreadFlag = false;
          _bLoggerThreadFlag = false;
          _bHCThreadFlag = false;

      }


      private volatile bool _shouldStop;
      private volatile bool _bThreadFlag;
      private volatile bool _bLoggerThreadFlag;
      private volatile bool _bHCThreadFlag;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // End of SHST Main Functions to copy
        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    }   // end of class

}
