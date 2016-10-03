﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Hucksters.Gripari.Input;
using Google.Protobuf;
using System.Net;
using System.IO;
using System.Security.Cryptography;

namespace Hucksters.Gripari.Output
{
    abstract class BaseOutput
    {
        static readonly int entitiesLength = 128;
        public EventLogs eventLogs = new EventLogs();
        int entitiesIndex = 0;
        DateTime lastFlushTime = DateTime.Now;

        public void OnEventLog(object sender, EventLogEventArgs ev)
        {
            var severity = Severity.Info;
            if (ev.Severity != "INFO")
                severity = Severity.Error;

            EventLog eventLog = new EventLog
            {
                Severity = severity,
                Timestamp = ev.Timestamp,
                Msg = ev.Msg,
                Source = sender.GetType().Name
            };
            Push(eventLog);
        }

        protected void Push(EventLog ev)
        {
            if (entitiesIndex++ == entitiesLength || ((DateTime.Now - lastFlushTime).Seconds > 60))
            {
                Flush();

                eventLogs = new EventLogs();
                entitiesIndex = 0;
                lastFlushTime = DateTime.Now;
            }
            eventLogs.Entities.Add(ev);
        }

        public abstract void Flush();
    }

    class FileOut : BaseOutput
    {
        const string filename = "gripari_{0}.dat";

        public override void Flush()
        {
            using (var writer = File.Create(String.Format(filename, Utils.DateTimeToUnixTImestamp(DateTime.Now))))
            {
                eventLogs.WriteTo(writer);
            }
        }

    }

    class WebOut : BaseOutput
    {
        WebRequest request = WebRequest.Create("https://negotians.hucksters.com/");

        public override void Flush()
        {
            var flushMsg = eventLogs.ToByteArray();
        }

        public void Send(byte[] data)
        {

            request.Method = "PUT";
            request.ContentType = "application/x-protobuf";
            request.ContentLength = data.Length;
            request.Headers.Add("Content-MD5", getMd5Hash(data));

            Stream dataStream = request.GetRequestStream();
            dataStream.Write(data, 0, data.Length);
            dataStream.Close();

            WebResponse response = request.GetResponse();

        }

        static string getMd5Hash(byte[] input)
        {
            MD5CryptoServiceProvider md5Hasher = new MD5CryptoServiceProvider();
            byte[] data = md5Hasher.ComputeHash(input);

            StringBuilder sBuilder = new StringBuilder();

            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }

            return sBuilder.ToString();
        }
    }
}
