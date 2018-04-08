using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Data;
using System.IO;


class EventRecorder
{
    public EventRecorder()
    {
        //if (!EventLog.SourceExists("Service Point Agent"))
        //{
        //    EventLog.CreateEventSource("Service Point Agent", "Service Point");
        //}
    }

    public void WriteEvent(string strMessage, string strEventType, int iEvtID)
    {
        //EventLogEntryType evtType = new EventLogEntryType();

        //strEventType = strEventType.ToUpper();
        //evtType = EventLogEntryType.Information;

        //if (strEventType == "I")
        //    evtType = EventLogEntryType.Information;

        //if (strEventType == "W")
        //    evtType = EventLogEntryType.Warning;

        //if (strEventType == "E")
        //    evtType = EventLogEntryType.Error;

        //EventLog myLog = new EventLog();
        //myLog.Source = "Service Point Agent";
        //myLog.WriteEntry(strMessage, evtType, iEvtID);
                
        var dir = AppDomain.CurrentDomain.BaseDirectory;

        string strPath = dir.ToString();

        if (strPath.Substring(strPath.Length - 1).Contains("\\"))
            strPath = strPath + @"Agent_LOG.TXT";

        //string strPath = @"D:\\WSP\\HostUdate_LOG.TXT";

        try
        {
            using (StreamWriter sw = new StreamWriter(strPath, true))
            {
                //콘솔에서 로그 바로 볼수 있도록 추가
                Console.WriteLine(DateTime.Now.ToString() + ", " + strMessage);

                //sw.WriteLine(DateTime.Now.ToString() + ", " + strMessage);
                //sw.Close();
            }
        }
        catch (Exception ex)
        {
            return;
        }

    }
}
