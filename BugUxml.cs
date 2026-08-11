//同步文件夹到目标文件夹(多个)
//软件启动时对比文件夹,检查文件集合变化差异拷贝,有可能上次拷贝了部分之类的情况,这种情况下不能出错.
//保持两变文件夹内容一模一样的,文件以及文件夹的数量,以及每一个文件的数据内容以及其大小.
//运行期间,实时监控源文件夹的文件变化(windows系统)进行实现
//使用一个函数,启动线程实现功能
//考虑全面再写代码,简洁,功能要完整无暇.

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public class BugUxml
{
    private string sourceFolder;
    private FileSystemWatcher watcher;
    private CancellationTokenSource cancellationTokenSource;
    private bool isProcessing = false;
    private Dictionary<string, string> replacements = new Dictionary<string, string>();

    public BugUxml(string sourceFolder_)
    {
        sourceFolder = sourceFolder_.Replace("\\", "/").TrimEnd('/');
    }

    public async Task StartServices()
    {
        cancellationTokenSource = new CancellationTokenSource();

        // 先启动监控
        SetupFileSystemWatcher();

        await ProcessFile(cancellationTokenSource.Token);
    }

    public void StopServices()
    {
        cancellationTokenSource?.Cancel();
        watcher?.Dispose();
        isProcessing = false;
    }

    private void SetupFileSystemWatcher()
    {
        watcher = new FileSystemWatcher(sourceFolder)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                          NotifyFilters.LastWrite | NotifyFilters.Size |
                          NotifyFilters.CreationTime | NotifyFilters.Attributes
        };

        watcher.Created += OnFileSystemCreated;
        watcher.Deleted += OnFileSystemDeleted;
        watcher.Renamed += OnFileSystemRenamed;
        watcher.Changed += OnFileSystemChanged;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (!e.FullPath.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase)) return;
        Logger2.i($"BugUxml - 检测到文件变化: {e.ChangeType} - {e.FullPath}");

        for (int i = 0; i < 100; i++)
        {
            try
            {
                //读取文件内容
                string fileContent = File.ReadAllText(e.FullPath);
                string fileContent_Old = fileContent;

                //替换字符串
                foreach (var item in replacements)
                {
                    fileContent = fileContent.Replace(item.Key, item.Value.ToString());
                }

                //如果文件内容发生变化，才写回文件
                if (fileContent != fileContent_Old)
                {
                    Logger2.i($"BugUxml - 开始处理写入文件: {e.ChangeType} - {e.FullPath}");
                    File.WriteAllText(e.FullPath, fileContent);
                    Logger2.i($"BugUxml - 处理写入文件完成: {e.ChangeType} - {e.FullPath}");
                }

                break;
            }
            catch (Exception ex)
            {
                Logger2.i($"BugUxml - 处理文件变化异常.Exception: {ex.Message}, 第 {i} 次");
                Task.Delay(10).Wait();
            }
        }
    }

    private async void OnFileSystemCreated(object sender, FileSystemEventArgs e)
    {
        if (!e.FullPath.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase)) return;
        Logger2.i($"BugUxml - 检测到文件创建: {e.ChangeType} - {e.FullPath}");

        await Task.Delay(3000);
        AddUxmlFile(e.FullPath);
    }
    private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
    {
    }
    private async void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (!e.FullPath.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase)) return;
        Logger2.i($"BugUxml - 检测到文件重命名: {e.ChangeType} - {e.FullPath}");

        await Task.Delay(3000);
        AddUxmlFile(e.FullPath);
    }

    private async Task ProcessFile(CancellationToken token)
    {
        isProcessing = true;

        string[] uxmlFiles = Directory.GetFiles(sourceFolder, "*.uxml", SearchOption.AllDirectories);
        foreach (string file in uxmlFiles)
        {
            bool result = AddUxmlFile(file);
        }

        while (!token.IsCancellationRequested && isProcessing)
        {
            await Task.Delay(50, token); // 减少延迟时间，提高响应速度
        }
    }

    private bool AddUxmlFile(string fileName)
    {
        try
        {
            //读取文件内容
            fileName = fileName.Replace("\\", "/");
            string fileName_Meta = fileName + ".meta";
            string fileContent_Meta = File.ReadAllText(fileName_Meta);

            Match match = Regex.Match(fileContent_Meta, @"guid:\s*([a-fA-F0-9]+)");
            if (!match.Success) return false;
            string guid = match.Groups[1].Value;

            string fileNameOnly = Path.GetFileNameWithoutExtension(fileName);

            string key = $"<ui:Template name=\"{fileNameOnly}\" />";
            string value = $"<ui:Template name=\"{fileNameOnly}\" src=\"{fileName.Replace(sourceFolder, "project://database/Assets/Resources")}?fileID=9197481963319205126&amp;guid={guid}&amp;type=3#{fileNameOnly}\" />";

            replacements.Add(key, value);
            Logger2.i($"BugUxml - AddUxmlFile 添加成功, {key} {value}");

            try
            {
                StringBuilder templateTXT = new StringBuilder();
                foreach (var item in replacements)
                {
                    //if (item.Value.IndexOf("Node") < 0 && item.Value.IndexOf("Item") < 0) continue;
                    templateTXT.AppendLine("");
                    templateTXT.AppendLine(item.Value);
                }

                string outFileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "template.txt");
                File.WriteAllText(outFileName, templateTXT.ToString(), Encoding.UTF8);
            }
            catch (Exception) { }

            return true;
        }
        catch (Exception ex)
        {
            Logger2.i($"BugUxml - AddUxmlFile.Exception: {ex.Message}, {fileName}");
            return false;
        }
    }
}