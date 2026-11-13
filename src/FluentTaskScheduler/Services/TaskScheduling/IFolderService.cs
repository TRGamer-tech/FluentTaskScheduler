using System;
using System.Collections.Generic;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Services
{
    /// <summary>Task Scheduler folder tree and moving tasks/folders around it.</summary>
    public interface IFolderService
    {
        TaskFolderModel GetFolderStructure();
        void MoveTask(string sourcePath, string targetFolderPath);
        void MoveFolder(string sourceFolderPath, string targetParentFolderPath);
        void CreateFolder(string path);
        void DeleteFolder(string path);
        void RenameFolder(string oldPath, string newName);
    }
}
