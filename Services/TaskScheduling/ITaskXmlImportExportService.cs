using System;
using System.Collections.Generic;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{
    /// <summary>Task XML export and import.</summary>
    public interface ITaskXmlImportExportService
    {
        void ExportTask(string taskPath, string outputPath);
        void RegisterTaskFromXml(string folderPath, string name, string xml);
    }
}
