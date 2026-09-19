using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentTaskScheduler.Models;
using Microsoft.Win32.TaskScheduler;
using System.Collections.ObjectModel;
using AppTaskState = FluentTaskScheduler.Models.Enums.TaskState;
using AppTriggerType = FluentTaskScheduler.Models.Enums.TriggerType;

namespace FluentTaskScheduler.Services
{
    public class TaskXmlImportExportService : ITaskXmlImportExportService
    {
        private readonly TaskSchedulerConnection _connection;
        public TaskXmlImportExportService(TaskSchedulerConnection connection) { _connection = connection; }

        public void ExportTask(string taskPath, string outputPath)
        {
            using (var ts = _connection.Open())
            {
                var task = ts.GetTask(taskPath);
                if (task != null)
                {
                    task.Definition.XmlText = task.Definition.XmlText; // Ensure XML is generated
                    System.IO.File.WriteAllText(outputPath, task.Definition.XmlText, new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true));
                }
                else
                {
                    throw new Exception($"Task '{taskPath}' not found.");
                }
            }
        }

        public void RegisterTaskFromXml(string folderPath, string name, string xml)
        {
            using (var ts = _connection.Open())
            {
                var td = ts.NewTask();
                td.XmlText = xml;
                var folder = TaskSchedulerConnection.GetOrCreateFolder(ts, folderPath);
                folder.RegisterTaskDefinition(name, td, TaskCreation.CreateOrUpdate, null, null, TaskLogonType.InteractiveToken);
            }
        }
    }
}
