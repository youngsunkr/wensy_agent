
using System;
using System.Collections.Generic;
using System.Text;
using System.Management;
using System.Diagnostics;
using System.Xml;
using System.Configuration;
using System.Data;
using System.IO;

namespace ServiceConfigurations
{
    class AppConfigs
    {
        public SharedData g_SharedData = new SharedData();
        EventRecorder WSPEvent = new EventRecorder();
          
        private string GetLocalIPAddress()
        {
            System.Net.IPHostEntry host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            string clientIP = string.Empty;
            for (int i = 0; i < host.AddressList.Length; i++)
            {
                // AddressFamily.InterNetworkV6 - IPv6
                if (host.AddressList[i].AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    clientIP = host.AddressList[i].ToString();
                }
            }

            return clientIP;
        }
        
        public bool LoadXMLConfig()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;

            string strPath = dir.ToString();
            
            if(strPath.Substring(strPath.Length - 1).Contains("\\"))
                strPath = strPath + @"ServicePoint.Settings.xml";
            else
                strPath = strPath + @"\\ServicePoint.Settings.xml";

            GetSystemInformation();

            try
            {
                XmlDocument xml = new XmlDocument();
                
                xml.Load(@strPath);

                if (xml.FirstChild.NodeType == XmlNodeType.XmlDeclaration)
                {
                    XmlDeclaration dec = (XmlDeclaration)xml.FirstChild;
                    dec.Encoding = "UTF-8";
                }
                else
                {
                    XmlDeclaration dec = xml.CreateXmlDeclaration("1.0", null, null);
                    dec.Encoding = "UTF-8";
                    xml.InsertBefore(dec, xml.DocumentElement);
                }

                XmlNodeList xnList = xml.SelectNodes("/ServicePoint/AGENT");
                foreach (XmlNode xn in xnList)
                {
                    g_SharedData.WSP_AGENT_SETTING.strServerKey = xn["SERVER_KEY"].InnerText;
                    g_SharedData.WSP_AGENT_SETTING.iBuildNumber = "2.1"; //xn["BuildNumber"].InnerText;
                    g_SharedData.WSP_AGENT_SETTING.strWS_URL = xn["WS_URL"].InnerText;

                    //g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval = Convert.ToInt32(xn["PerformanceCollectInterval"].InnerText);
                    //if (g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval < 5 || g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval > 3600)
                    //    g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval = 15;
                    g_SharedData.WSP_AGENT_SETTING.iPerfLogCollectInterval = 15;
                    g_SharedData.WSP_AGENT_SETTING.iMaxAgentMemorySizeMB = 70; // Convert.ToInt32(xn["MaxAgentMemorySizeMB"].InnerText);
                }

                xnList = xml.SelectNodes("/ServicePoint/WEB");
                foreach (XmlNode xn in xnList)
                {
                    g_SharedData.WEB_SETTING.iMaxConnectionPoolSize = Convert.ToInt32(xn["MaxPoolSize"].InnerText);
                    g_SharedData.WEB_SETTING.strLogFilesDirectory = xn["LogFileDirectory"].InnerText;
                    g_SharedData.WEB_SETTING.strLastLogFileName = xn["LastLogFile"].InnerText;

                    if (xn["IISLogAnalysis"].InnerText.ToLower() == "false")
                        g_SharedData.WEB_SETTING.bIISLogAnalysis = false;
                    else
                        g_SharedData.WEB_SETTING.bIISLogAnalysis = true;
                    
                    if (xn["HC_Enabled"].InnerText.ToLower() == "false")
                        g_SharedData.HC_SETTING.bHC_Enabled = false;
                    else
                        g_SharedData.HC_SETTING.bHC_Enabled = true;

                    g_SharedData.WEB_SETTING.strHostHeader = xn["HostHeader"].InnerText;
                    g_SharedData.HC_SETTING.iHC_Interval = Convert.ToInt32(xn["HC_Interval"].InnerText);

                    if (g_SharedData.HC_SETTING.iHC_Interval < 1 || g_SharedData.HC_SETTING.iHC_Interval > 60)
                        g_SharedData.HC_SETTING.iHC_Interval = 30;

                    g_SharedData.HC_SETTING.iHC_Timeout = Convert.ToInt32(xn["HC_TimeOut"].InnerText);
                    if (g_SharedData.HC_SETTING.iHC_Timeout < 1)
                        g_SharedData.HC_SETTING.iHC_Timeout = 1;

                    // Initialize HC List in dtHC_URL datatable
                    if (g_SharedData.HC_SETTING.dtHC_URL == null)
                    {
                        g_SharedData.HC_SETTING.dtHC_URL = new DataTable();
                        g_SharedData.HC_SETTING.dtHC_URL.Columns.Add(new DataColumn("HC_URL", typeof(string)));
                    }
                    else
                        g_SharedData.HC_SETTING.dtHC_URL.Rows.Clear();

                    XmlNodeList xnURLS = xml.SelectNodes("/ServicePoint/WEB/HC_URLS/URL");

                    foreach (XmlNode xn2 in xnURLS)
                    {
                        g_SharedData.HC_SETTING.dtHC_URL.Rows.Add(xn2.InnerText);
                    }

                }

                xnList = xml.SelectNodes("/ServicePoint/SQL");
                foreach (XmlNode xn in xnList)
                {
                    
                    g_SharedData.LOCAL_SQL_SETTING.strServerName = xn["ServerName"].InnerText;
                    g_SharedData.LOCAL_SQL_SETTING.strAuthentication = xn["Authentication"].InnerText;
                    g_SharedData.LOCAL_SQL_SETTING.strAccount = xn["UserName"].InnerText;
                    g_SharedData.LOCAL_SQL_SETTING.strPassword = xn["Password"].InnerText;
                    g_SharedData.LOCAL_SQL_SETTING.strEncrypted = xn["LocalDB_EncrtypedPassword"].InnerText;
                    g_SharedData.LOCAL_SQL_SETTING.iQueryInterval = Convert.ToInt32(xn["LocalDB_minQueryInterval"].InnerText);

                    if (xn["EnableCollectingRunningQueries"].InnerText.ToUpper() == "TRUE")
                        g_SharedData.LOCAL_SQL_SETTING.bEnableCollectingRunningQueries = true;
                    else
                        g_SharedData.LOCAL_SQL_SETTING.bEnableCollectingRunningQueries = false;
                }

                BuildLocalDBConnectionString();          
                
                return true;
            }
            catch (Exception ex)
            {
                WSPEvent.WriteEvent("Service Point Agent failed to read settings from ServicePoint.Settings.XML. Please review your cofigurations in the file. - " + ex.Message,"E", 1137);
                return false;
            }

        }

        private void BuildLocalDBConnectionString()
        {
            string lstrConnectionString;
            string strDBName = "master";

            int InstanceIndex = 0;
            InstanceIndex = g_SharedData.LOCAL_SQL_SETTING.strServerName.IndexOf("\\") + 1;

            if (InstanceIndex > 0)
                g_SharedData.LOCAL_SQL_SETTING.strInstanceName = g_SharedData.LOCAL_SQL_SETTING.strServerName.Substring(InstanceIndex);
            else
                g_SharedData.LOCAL_SQL_SETTING.strInstanceName = "";




            if (g_SharedData.LOCAL_SQL_SETTING.strAuthentication == "Windows")
                lstrConnectionString = "Server=" + g_SharedData.LOCAL_SQL_SETTING.strServerName + ";Database=" + strDBName + ";Trusted_Connection=True";
            else
                
                lstrConnectionString = "Server=" + g_SharedData.LOCAL_SQL_SETTING.strServerName + ";Database=" + strDBName + ";User ID=" + g_SharedData.LOCAL_SQL_SETTING.strAccount + ";Password=" + g_SharedData.LOCAL_SQL_SETTING.strPassword + ";Trusted_Connection=False";

            g_SharedData.LOCAL_SQL_SETTING.strConnectionString = lstrConnectionString;


            //v.3 디비 연결 암호 값 - 암호화를 기본값으로 변경.
            //if (g_SharedData.LOCAL_SQL_SETTING.strEncrypted.ToLower() == "true")
            //    g_SharedData.LOCAL_SQL_SETTING.strPassword = EncDec.Decrypt("SHST.LocalDB.Secure.key", g_SharedData.LOCAL_SQL_SETTING.strPassword);
        }

        public void GetSystemInformation()
        {
            string strWinVer = Environment.OSVersion.ToString();
            
            // Refer 'ver' command in Windows - https://msdn.microsoft.com/en-us/library/windows/desktop/ms724832(v=vs.85).aspx
            if (strWinVer.Contains("10.0"))
            {
                g_SharedData.SYSINFO.iWinVer = 2016;
                g_SharedData.SYSINFO.strWinVer = "Windows Server 2016";
                //Windows Server 2016
                //Windows 10
            }
            else if (strWinVer.Contains("6.3"))
            {
                g_SharedData.SYSINFO.iWinVer = 2012;
                g_SharedData.SYSINFO.strWinVer = "Windows Server 2012 R2";
                //Windows Server 2012 R2
                //Windows 8.1
            }
            else if (strWinVer.Contains("6.2"))
            {
                g_SharedData.SYSINFO.iWinVer = 2012;
                g_SharedData.SYSINFO.strWinVer = "Windows Server 2012";
                //Windows Server 2012
                //Windows 8
            }
            else if (strWinVer.Contains("6.1") )
            {
                g_SharedData.SYSINFO.iWinVer = 2008;
                g_SharedData.SYSINFO.strWinVer = "Windows 2008 R2";
                //Windows 7
                //Windows Server 2008 R2
            }
            else if (strWinVer.Contains("6.0"))
            {
                g_SharedData.SYSINFO.iWinVer = 2008;
                g_SharedData.SYSINFO.strWinVer = "Windows 2008";
                //Windows Vista
                //Windows Server 2008

            }
            else if (strWinVer.Contains("5.2"))
            {
                g_SharedData.SYSINFO.iWinVer = 2003;
                g_SharedData.SYSINFO.strWinVer = "Windows 2003";
                //Windows XP 64-Bit Edition 
                //Windows Server 2003
                //Windows Server 2003 R2
            }
            else if (strWinVer.Contains("5.1"))
            {
                g_SharedData.SYSINFO.iWinVer = 0000;
                g_SharedData.SYSINFO.strWinVer = "Windows XP";
            }
            else if (strWinVer.Contains("5.0"))
            {
                g_SharedData.SYSINFO.iWinVer = 2000;
                g_SharedData.SYSINFO.strWinVer = "Windows 2000";
            }
            else
            {
                g_SharedData.SYSINFO.iWinVer = 0000;
                g_SharedData.SYSINFO.strWinVer = "Unknown OS";
            }

            g_SharedData.SYSINFO.iNumberOfProcessors = Environment.ProcessorCount;
            if (g_SharedData.SYSINFO.iNumberOfProcessors < 1)
                g_SharedData.SYSINFO.iNumberOfProcessors = 1;

            g_SharedData.SYSINFO.strComputerName = Environment.MachineName;

            //Available Returns - x64, AMD64, x86, i64...
            if (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE").Contains("64"))
                g_SharedData.SYSINFO.bIs64bit = true;
            else
                g_SharedData.SYSINFO.bIs64bit = false;

            ManagementObjectSearcher Search = new ManagementObjectSearcher("Select TotalPhysicalMemory From Win32_ComputerSystem");

            g_SharedData.SYSINFO.flRAMSize = 4000000000;
            foreach (ManagementObject Mobject in Search.Get())
            {
                float Ram_Bytes = (Convert.ToSingle(Mobject["TotalPhysicalMemory"]));
                g_SharedData.SYSINFO.flRAMSize = Ram_Bytes;
            }

            g_SharedData.SYSINFO.strIPAddress = GetLocalIPAddress();

            string result = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
            foreach (ManagementObject os in searcher.Get())
            {
                g_SharedData.SYSINFO.strWinVer = os["Caption"].ToString().Substring(10);
                break;
            }
            


            //g_SharedData.WSP_AGENT_SETTING.strMachineID = Security.FingerPrint.Value();

        }

    }
}
