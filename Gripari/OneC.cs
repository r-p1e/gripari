using System;
using System.IO;
using System.Collections.Generic;

using IniParser;
using IniParser.Model;
using System.Reflection;
using Newtonsoft.Json;
using System.Threading;

namespace Hucksters.Forvaret.Input
{
    
    public class OneC
    {
        public event EventHandler<EventLogEventArgs> GotEventLog;
        DateTime _lastLogEventDT = DateTime.Now.AddYears(-1);

        public void run()
        {
            EventHandler<EventLogEventArgs> handler = GotEventLog;

            if (handler == null)
                // TODO: Exception which tell that there is no subscriber for what i will publish something!
                throw new Exception();

            DateTime currentStartDT = _lastLogEventDT;
            DateTime currentEndDT = DateTime.Now;
            _lastLogEventDT = currentEndDT;

            if (currentStartDT.AddMinutes(15) < currentEndDT)
            {
                foreach (var cfg in findConfigs())
                {
                    foreach (var dbase in connectStringPerSectionFromConfig(cfg))
                    {
                        ExportOneC exportOneC = new ExportOneC(dbase.Value);
                        foreach (var eventLog in exportOneC.Export(currentStartDT, currentEndDT))
                        {
                            EventLogEventArgs elea = new EventLogEventArgs();
                            elea.Severity = eventLog.level;
                            elea.Timestamp = Utils.DateTimeToUnixTImestamp(eventLog.date);
                            elea.Msg = JsonConvert.SerializeObject(eventLog);
                            handler?.Invoke(this, elea);
                            Thread.Sleep(1000);
                        }
                    }
                }
            }
        }
        
        public List<string> findConfigs()
        {
            Console.WriteLine("Looking for 1C config file...");

            string pathToOneC = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1C");
            string pathToOneCEStart = Path.Combine(pathToOneC, "1CEStart");
            List<string> configs = new List<string>();

            if (Directory.Exists(pathToOneCEStart))
            {
                DirectoryInfo di = new DirectoryInfo(pathToOneCEStart);

                foreach (var fi in di.EnumerateFiles("*.v8i"))
                {
                    configs.Add(fi.FullName);
                }
            } else
            {
                // TODO: create make sense exception
                throw new Exception();
            }

            return configs;
        }

        public Dictionary<string, string> connectStringPerSectionFromConfig(string pathToConfigFile)
        {
            var parser = new FileIniDataParser();
            IniData configs = parser.ReadFile(pathToConfigFile);
            Dictionary<string, string> connectStringPerSection = new Dictionary<string, string>();
            
            foreach (var config in configs.Sections)
            {
                connectStringPerSection.Add(config.SectionName, configs[config.SectionName]["Connect"]);
            }

            return connectStringPerSection;
        }

    }

    public class ExportOneC
    {
        dynamic Connection;
        Dictionary<string, string[]> oneCDataMetaStructure = new Dictionary<string, string[]>();

        public ExportOneC(string connectString)
        {
            dynamic connector = Activator.CreateInstance(Type.GetTypeFromProgID("V83.COMConnector"));

            Connection = connector.Connect(connectString);
        }

        public string[] OneCDocumentItemNames(string metadataname)
        {
            if (!oneCDataMetaStructure.ContainsKey(metadataname))
            {
                loadOneCDataMetaStructureByMetaDataName(metadataname);
            }
            return oneCDataMetaStructure[metadataname];
        }

        public void loadOneCDataMetaStructureByMetaDataName(string metadataname)
        {
            string[] metadatavalues = metadataname.Split('.');
            List<string> metaStructureItemNames = new List<string>();

            if (!(metadatavalues == null || metadatavalues.Length == 0))
            {
                dynamic metaStruture = ReflectionExt.GetAttr(Connection.Metadata, metadatavalues[0]);

                for (int i = 1; i < metadatavalues.Length; i++)
                {
                    metaStruture = ReflectionExt.GetAttr(metaStruture, metadatavalues[i]);
                }

                foreach (var item in metaStruture.Attributes)
                {
                    metaStructureItemNames.Add(item.Name);
                }
            }
            oneCDataMetaStructure[metadataname] = metaStructureItemNames.ToArray();
         }

        public EventLog OneCEventLogToEventLogStruct(dynamic eventLog)
        {
            EventLog eventlog;
            
            eventlog.level = Connection.String(eventLog.Level);
            eventlog.user = Connection.String(eventLog.User);
            eventlog.date = eventLog.Date;
            eventlog.what = eventLog.Event;

            Dictionary<string, object> data = new Dictionary<string, object>();

            if (eventLog.Event.Contains("Data"))
            {
                string[] metadatavalues = eventLog.Metadata.Split('.');

                if (!(metadatavalues == null || metadatavalues.Length == 0))
                {

                    metadatavalues[0] += 's';
                    string[] metaStructureItemNames = OneCDocumentItemNames(String.Join(".", metadatavalues));

                    foreach (var item in metaStructureItemNames)
                    {
                        dynamic value = ReflectionExt.GetAttr(eventLog.Data, item);

                        if (value.GetType().ToString() == "System.__ComObject")
                        {
                            data.Add(item, Connection.String(value));
                        }
                        else
                        {
                            data.Add(item, value);
                        }
                    }
                }
            }

            eventlog.data = data;
            return eventlog;
        }

        public IEnumerable<EventLog> Export(DateTime startDate, DateTime endDate)
        {

            dynamic filterEvents = Connection.NewObject("Structure");
            filterEvents.Insert("StartDate", startDate);
            filterEvents.Insert("EndDate", endDate);

            dynamic eventLogsValueTable = Connection.NewObject("ValueTable");
            Connection.UnloadEventLog(eventLogsValueTable, filterEvents);

            Console.WriteLine(eventLogsValueTable.Count());
            foreach (var eventLog in eventLogsValueTable)
            {
                yield return OneCEventLogToEventLogStruct(eventLog);
            }
        }

        private static string[] AllSupportedSources()
        {
            return new []{ "Constant", "Enum", "Document", "Catalog" };
        }

        private IEnumerable<string> AllSupportedSourcesFullNames()
        {
            foreach (var source in AllSupportedSources())
            {
                dynamic sourceMetadata = ReflectionExt.GetAttr(Connection.Metadata, (source + "s"));

                foreach (var sourceInfo in sourceMetadata)
                {
                    yield return (source + "." + sourceInfo.Name);
                }
            }
        }
    }

    public struct EventLog
    {
        public string level, user, what;
        public DateTime date;
        public Dictionary<string, object> data;
    }

    public class EventLogEventArgs : EventArgs
    {
        public string Severity;
        public double Timestamp;
        public string Msg;
    }

    /*
     Class for emulate python's __getattr__ behaviour
     took from here: https://stackoverflow.com/questions/138045/is-there-something-like-pythons-getattr-in-c
     */
     public static class ReflectionExt
    {
        public static dynamic GetAttr(this object obj, string name)
        {
            Type type = obj.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty;
            return type.InvokeMember(name, flags, Type.DefaultBinder, obj, null);
        }
    }

    /*
     Class utils. Here we collect some usefull methods
     */
     public static class Utils
    {
        public static double DateTimeToUnixTImestamp (DateTime dt)
        {
            return (dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}