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
using System.Threading;
using System.Threading.Tasks;

public class Cloner
{
    private string sourceFolder;
    private List<string> targetFolders = null;
    private FileSystemWatcher watcher;
    private CancellationTokenSource cancellationTokenSource;
    private readonly object syncLock = new object();
    private ConcurrentQueue<string> changedFilesQueue = new ConcurrentQueue<string>();
    private HashSet<string> processingFiles = new HashSet<string>(); // 跟踪正在处理的文件
    private bool isProcessing = false;
    private int activeCopyTasks = 0;
    private const int MAX_PARALLEL_COPIES = 16; // 增加并行复制任务数量
    private const int LARGE_FILE_THRESHOLD = 32 * 1024 * 1024; // 增加大文件阈值到32MB
    private string dateSuffix;

    public Cloner(string sourceFolder_, List<string> targetFolders_)
    {
        sourceFolder = sourceFolder_;
        targetFolders = targetFolders_;
        dateSuffix = DateTime.Now.ToString("yyyy-MM-dd");
    }

    public async Task StartServices()
    {
        cancellationTokenSource = new CancellationTokenSource();

        Logger.i($"Cloner - 开始同步文件夹: {sourceFolder} 到 {string.Join(", ", targetFolders)}");

        // 先启动监控
        SetupFileSystemWatcher();
        Logger.i($"Cloner - 已启动文件系统监控: {sourceFolder}");

        // 然后开始初始同步
        _ = Task.Run(() =>
        {
            try
            {
                Logger.i("开始初始同步...");
                // 并行初始同步（不等待）
                foreach (var targetFolder in targetFolders)
                {
                    Logger.i($"Cloner - 开始同步到目标文件夹: {targetFolder}");
                    _ = SyncSingleTargetFolder(targetFolder, cancellationTokenSource.Token);
                }
            }
            catch (Exception ex)
            {
                Logger.i($"Cloner - 初始同步失败: {ex}");
            }
        }, cancellationTokenSource.Token);

        // 同步完成，开始处理文件变化
        Logger.i("开始处理文件变化...");
        await ProcessFile(cancellationTokenSource.Token);
    }

    public void StopServices()
    {
        cancellationTokenSource?.Cancel();
        watcher?.Dispose();
        isProcessing = false;
        Logger.i($"Cloner - 已停止同步: {sourceFolder}");
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

        watcher.Created += OnFileSystemChanged;
        watcher.Changed += OnFileSystemChanged;
        watcher.Deleted += OnFileSystemChanged;
        watcher.Renamed += OnFileSystemRenamed;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        Logger.i($"Cloner - 检测到文件变化: {e.ChangeType} - {e.FullPath}");
        changedFilesQueue.Enqueue(e.FullPath);
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        Logger.i($"Cloner - 检测到文件重命名: {e.OldFullPath} -> {e.FullPath}");
        changedFilesQueue.Enqueue(e.OldFullPath); // 处理旧路径删除
        changedFilesQueue.Enqueue(e.FullPath);    // 处理新路径创建
    }

    private async Task ProcessFile(CancellationToken token)
    {
        isProcessing = true;

        while (!token.IsCancellationRequested && isProcessing)
        {
            if (changedFilesQueue.TryPeek(out var filePath))
            {
                // 检查文件是否正在被处理
                if (processingFiles.Contains(filePath))
                {
                    // 如果文件正在被处理，等待一段时间后再尝试
                    await Task.Delay(500, token);
                    continue;
                }

                lock (syncLock)
                {
                    // 标记文件为正在处理
                    processingFiles.Add(filePath);
                }
                
                // 检查文件是否被其他程序占用
                if (IsFileLocked(filePath))
                {
                    Logger.i($"Cloner - 文件被锁定，稍后重试: {filePath}");
                    await Task.Delay(500, token);
                    continue;
                }

                // 从队列中移除文件
                changedFilesQueue.TryDequeue(out _);

                try
                {
                    Logger.i($"Cloner - 开始处理文件变化: {filePath}");
                    await SyncFileToAllTargets(filePath, token);
                    Logger.i($"Cloner - 成功处理文件变化: {filePath}");
                    // 处理完成后，从正在处理的文件集合中移除
                    lock (syncLock)
                    {
                        processingFiles.Remove(filePath);
                    }
                }
                catch (Exception ex)
                {
                    Logger.i($"Cloner - 处理文件变化时出错: {filePath} - {ex.Message}");
                    lock (syncLock)
                    {
                        processingFiles.Remove(filePath);
                    }
                    await Task.Delay(500, token);
                }
            }
            else
            {
                await Task.Delay(50, token); // 减少延迟时间，提高响应速度
            }
        }
    }

    private bool IsFileLocked(string filePath)
    {
        // 如果是目录或文件不存在，不检查锁定
        if (Directory.Exists(filePath) || !File.Exists(filePath)) return false;
            
        try
        {
            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return false; // 文件可以打开，没有被锁定
            }
        }
        catch
        {
            return true; // 任何异常都视为文件被锁定
        }
    }

    private async Task SyncSingleTargetFolder(string targetFolder, CancellationToken token)
    {
        string targetBasePath = Path.Combine(targetFolder, dateSuffix + "-" + Path.GetFileName(sourceFolder));
        string deleteFolderPath = targetBasePath;

        Logger.i($"Cloner - 开始同步文件夹: {sourceFolder} -> {targetBasePath}");
        
        // 确保目标文件夹存在
        if (!Directory.Exists(targetBasePath))
        {
            Directory.CreateDirectory(targetBasePath);
            Logger.i($"Cloner - 创建目标文件夹: {targetBasePath}");
        }

        // 同步文件和文件夹
        await SyncFolders(sourceFolder, targetBasePath, deleteFolderPath, token);
        
        Logger.i($"Cloner - 完成同步文件夹: {sourceFolder} -> {targetBasePath}");
    }

    private async Task SyncFolders(string source, string target, string deleteFolderPath, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        // 获取源文件夹中的所有文件和子文件夹
        var sourceFiles = Directory.GetFiles(source).Select(f => new FileInfo(f)).ToList();
        var sourceDirs = Directory.GetDirectories(source).Where(d => !Path.GetFileName(d).Equals("__delete__", StringComparison.OrdinalIgnoreCase)).Select(d => new DirectoryInfo(d)).ToList();

        // 获取目标文件夹中的所有文件和子文件夹
        Directory.CreateDirectory(target); // 确保目标文件夹存在

        var targetFiles = Directory.GetFiles(target).Select(f => new FileInfo(f)).ToList();
        var targetDirs = Directory.GetDirectories(target).Where(d => !Path.GetFileName(d).Equals("__delete__", StringComparison.OrdinalIgnoreCase)).Select(d => new DirectoryInfo(d)).ToList();

        // 并行同步文件
        foreach (var sourceFile in sourceFiles)
        {
            if (token.IsCancellationRequested) return;

            string targetFilePath = Path.Combine(target, sourceFile.Name);
            var targetFile = targetFiles.FirstOrDefault(f => f.Name == sourceFile.Name);

            if (targetFile == null || !FilesAreEqual(sourceFile.FullName, targetFile.FullName))
            {
                // 检查源文件是否被锁定
                if (IsFileLocked(sourceFile.FullName))
                {
                    Logger.i($"Cloner - 文件被锁定，跳过: {sourceFile.FullName}");
                    continue; // 跳过被锁定的文件
                }

                // 限制并行复制任务数量
                while (activeCopyTasks >= MAX_PARALLEL_COPIES && !token.IsCancellationRequested)
                {
                    await Task.Delay(10, token);
                }

                if (token.IsCancellationRequested) return;

                Logger.i($"Cloner - 开始复制文件: {sourceFile.FullName} -> {targetFilePath}");
                Interlocked.Increment(ref activeCopyTasks);
                // 直接执行复制操作，不等待任务完成
                _ = Task.Run(() =>
                {
                    try
                    {
                        File.Copy(sourceFile.FullName, targetFilePath, true);
                        Logger.i($"Cloner - 成功复制文件: {sourceFile.FullName} -> {targetFilePath}");
                        Interlocked.Decrement(ref activeCopyTasks);
                    }
                    catch (Exception ex)
                    {
                        Logger.i($"Cloner - 复制文件失败: {sourceFile.FullName} -> {targetFilePath} - {ex.Message}");
                        lock (syncLock)
                        {
                            processingFiles.Remove(sourceFile.FullName);
                        }
                    }
                }, token);
            }
        }

        // 移动目标文件夹中多余的文件到__delete__子文件夹中
        foreach (var targetFile in targetFiles)
        {
            if (token.IsCancellationRequested) return;

            if (!sourceFiles.Any(f => f.Name == targetFile.Name))
            {
                try
                {
                    string deleteFilePath = MoveToDeleteFolder(targetFile.FullName, false, deleteFolderPath);
                    Logger.i($"Cloner - 移动多余文件: {targetFile.FullName} -> {deleteFilePath}");
                }
                catch (Exception ex)
                {
                    Logger.i($"Cloner - 移动文件异常: {targetFile.FullName} - {ex.Message}");
                }
            }
        }

        // 并行递归同步子文件夹
        foreach (var sourceDir in sourceDirs)
        {
            if (token.IsCancellationRequested) return;

            string targetDirPath = Path.Combine(target, sourceDir.Name);
            //Logger.i($"Cloner - 开始同步子文件夹: {sourceDir.FullName} -> {targetDirPath}");
            // 直接启动任务，不等待完成
            _ = Task.Run(() => SyncFolders(sourceDir.FullName, targetDirPath, deleteFolderPath, token), token);
        }

        // 移动目标文件夹中多余的子文件夹到__delete__子文件夹中
        foreach (var targetDir in targetDirs)
        {
            if (token.IsCancellationRequested) return;

            if (!sourceDirs.Any(d => d.Name == targetDir.Name))
            {
                try
                {
                    string deleteDirPath = MoveToDeleteFolder(targetDir.FullName, true, deleteFolderPath);
                    Logger.i($"Cloner - 移动多余文件夹: {targetDir.FullName} -> {deleteDirPath}");
                }
                catch (Exception ex)
                {
                    Logger.i($"Cloner - 移动文件夹异常: {targetDir.FullName} - {ex.Message}");
                }
            }
        }
    }

    private string MoveToDeleteFolder(string path, bool isFolder, string deleteFolderPath)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss_fff") + "_" + new Random().Next().ToString();
        string relativePath = path.Replace("\\", "/").Substring(deleteFolderPath.Length + 1).TrimStart(Path.DirectorySeparatorChar).Replace("\\", "/");
        string targetDeletePath = Path.Combine(deleteFolderPath + $"/__delete__/").Replace("\\", "/");
        targetDeletePath = (targetDeletePath + relativePath).Replace("\\", "/");

        var deletePath = Path.GetDirectoryName(targetDeletePath);
        if (!Directory.Exists(deletePath))
        {
            Directory.CreateDirectory(deletePath);
            Logger.i($"Cloner - 创建删除文件夹: {deletePath}");
        }

        // 确保删除
        if (isFolder)
        {
            if (Directory.Exists(targetDeletePath)) targetDeletePath += timestamp;
            Directory.Move(path, targetDeletePath);
        }
        else
        {
            if (File.Exists(targetDeletePath)) targetDeletePath += timestamp;
            File.Move(path, targetDeletePath);
        }

        return targetDeletePath;
    }

    private async Task SyncFileToAllTargets(string sourcePath, CancellationToken token)
    {
        try
        {
            // 如果文件被锁定，等待一段时间后再尝试
            if (IsFileLocked(sourcePath))
            {
                Logger.i($"Cloner - 文件被锁定，稍后重试: {sourcePath}");
                await Task.Delay(500, token);
                return;
            }

            // 获取相对路径
            string relativePath = sourcePath.Substring(sourceFolder.Length);

            foreach (var targetFolder in targetFolders)
            {
                string targetBasePath = Path.Combine(targetFolder, dateSuffix + "-" + Path.GetFileName(sourceFolder));
                string deleteFolderPath = targetBasePath;
                string targetPath = targetBasePath + relativePath;

                Logger.i($"Cloner - 开始同步: {sourcePath} -> {targetPath}");
                // 直接启动任务，不等待完成
                _ = Task.Run(() =>
                {
                    if (Directory.Exists(sourcePath))
                    {
                        // 如果是新建文件夹，确保目标文件夹存在
                        Directory.CreateDirectory(targetPath);
                        Logger.i($"Cloner - 创建目标文件夹: {targetPath}");
                    }
                    else if (File.Exists(sourcePath))
                    {
                        // 确保目标文件夹存在
                        string targetDir = Path.GetDirectoryName(targetPath);
                        Directory.CreateDirectory(targetDir);

                        // 复制文件
                        try
                        {
                            File.Copy(sourcePath, targetPath, true);
                            Logger.i($"Cloner - 成功复制文件: {sourcePath} -> {targetPath}");
                        }
                        catch (Exception ex)
                        {
                            Logger.i($"Cloner - 复制文件失败: {sourcePath} -> {targetPath} - {ex.Message}");
                            lock (syncLock)
                            {
                                processingFiles.Remove(sourcePath);
                            }
                        }
                    }
                    else
                    {
                        // 源文件或文件夹已被删除，删除目标位置的对应项
                        if (File.Exists(targetPath))
                        {
                            try
                            {
                                string deleteFilePath = MoveToDeleteFolder(targetPath, false, deleteFolderPath);
                                Logger.i($"Cloner - 移动目标文件: {targetPath} -> {deleteFilePath}");
                            }
                            catch (Exception ex)
                            {
                                Logger.i($"Cloner - 移动目标文件失败: {targetPath} - {ex.Message}");
                                lock (syncLock)
                                {
                                    processingFiles.Remove(sourcePath);
                                }
                            }
                        }
                        else if (Directory.Exists(targetPath))
                        {
                            try
                            {
                                string deleteDirPath = MoveToDeleteFolder(targetPath, true, deleteFolderPath);
                                Logger.i($"Cloner - 移动目标文件夹: {targetPath} -> {deleteDirPath}");
                            }
                            catch (Exception ex)
                            {
                                Logger.i($"Cloner - 移动目标文件夹失败: {targetPath} - {ex.Message}");
                                lock (syncLock)
                                {
                                    processingFiles.Remove(sourcePath);
                                }
                            }
                        }
                    }
                }, token);
            }
            
            // 验证同步结果
            await VerifySyncResults(sourcePath, token);
        }
        catch (Exception ex)
        {
            Logger.i($"Cloner - 同步文件时出错: {sourcePath} - {ex.Message}");
        }
    }
    
    private async Task VerifySyncResults(string sourcePath, CancellationToken token)
    {
        try
        {
            // 如果是文件夹或文件不存在，不需要验证
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                return;
                
            string relativePath = sourcePath.Substring(sourceFolder.Length);
            
            foreach (var targetFolder in targetFolders)
            {
                string targetBasePath = Path.Combine(targetFolder, dateSuffix + "-" + Path.GetFileName(sourceFolder));
                string deleteFolderPath = targetBasePath;
                string targetPath = targetBasePath + relativePath;
                
                // 验证文件
                if (File.Exists(sourcePath) && File.Exists(targetPath))
                {
                    if (!FilesAreEqual(sourcePath, targetPath))
                    {
                        Logger.i($"Cloner - 验证失败，文件内容不一致: {sourcePath} 与 {targetPath}");
                        changedFilesQueue.Enqueue(sourcePath);
                        break;
                    }
                    else
                    {
                        Logger.i($"Cloner - 验证成功: {sourcePath} 与 {targetPath}");
                    }
                }
                // 验证文件夹存在性
                else if (!(Directory.Exists(sourcePath) && Directory.Exists(targetPath)))
                {
                    Logger.i($"Cloner - 验证失败，文件夹存在性不一致: {sourcePath} 与 {targetPath}");
                    changedFilesQueue.Enqueue(sourcePath);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.i($"Cloner - 验证同步结果时出错: {sourcePath} - {ex.Message}");
            changedFilesQueue.Enqueue(sourcePath);
        }
    }
   
    private bool FilesAreEqual(string file1, string file2)
    {
        try
        {
            // 快速检查 - 大小和时间戳
            var fi1 = new FileInfo(file1);
            var fi2 = new FileInfo(file2);

            if (!fi1.Exists || !fi2.Exists) return false;
            if (fi1.Length != fi2.Length) return false;
            if (fi1.LastWriteTime != fi2.LastWriteTime) return false;

            // 对于大文件，使用哈希比较，对于小文件使用内容比较
            return fi1.Length < LARGE_FILE_THRESHOLD ? CompareFileContents(file1, file2) : CompareFileHash(file1, file2);
        }
        catch
        {
            return false;
        }
    }

    private bool CompareFileContents(string file1, string file2)
    {
        const int bufferSize = 131072; // 增加到128KB缓冲区
        byte[] buffer1 = new byte[bufferSize];
        byte[] buffer2 = new byte[bufferSize];

        using (var fs1 = new FileStream(file1, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
        using (var fs2 = new FileStream(file2, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
        {
            while (true)
            {
                int count1 = fs1.Read(buffer1, 0, bufferSize);
                int count2 = fs2.Read(buffer2, 0, bufferSize);

                if (count1 != count2) return false;
                if (count1 == 0) return true;

                if (!buffer1.AsSpan(0, count1).SequenceEqual(buffer2.AsSpan(0, count2))) return false;
            }
        }
    }

    private bool CompareFileHash(string file1, string file2)
    {
        var md5 = MD5.Create();

        using (var stream1 = File.OpenRead(file1))
        using (var stream2 = File.OpenRead(file2))
        {
            byte[] hash1 = md5.ComputeHash(stream1);
            byte[] hash2 = md5.ComputeHash(stream2);

            return hash1.AsSpan().SequenceEqual(hash2);
        }
    }
}