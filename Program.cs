using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Reflection;
using System.Configuration.Install;
using Newtonsoft.Json.Linq;
using System.Threading;


namespace FolderSync
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "/install")
            {
                InstallService();
            }
            else if (args.Length > 0 && args[0] == "/uninstall")
            {
                UninstallService();
            }
            else if (args.Length > 0 && args[0] == "/c")
            {
                //同步文件夹 - 代码备份
                try
                {
                    List<Task> tasks = new List<Task>();  // 存储所有任务
                    List<Cloner> _cloners = new List<Cloner>();

                    var FolderSyncConfig = ConfigJson.json["FolderSync"];
                    foreach (var item in FolderSyncConfig)
                    {
                        var cloner = new Cloner(item[0].ToString(), ((JArray)item[1]).ToObject<List<string>>());
                        tasks.Add(cloner.StartServices());
                        _cloners.Add(cloner);
                    }

                    var UXMLTemplatePath = ConfigJson.json["UXMLTemplatePath"]?.ToString();
                    if (!string.IsNullOrEmpty(UXMLTemplatePath))
                    {
                        var bugUxml = new BugUxml(UXMLTemplatePath);
                        tasks.Add(bugUxml.StartServices());
                    }

                    Task.WaitAll(tasks.ToArray());  // 阻塞主线程，等待所有任务完成
                }
                catch (Exception ex)
                {
                    Logger.i("异常: " + ex.Message);
                }
            }
            else
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new Service()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }


        static void InstallService()
        {
            try
            {
                // 获取当前程序集
                var assemblyPath = Assembly.GetExecutingAssembly().Location;

                // 创建安装程序
                var installer = new AssemblyInstaller(assemblyPath, null);
                installer.UseNewContext = true;

                // 安装服务
                var state = new Hashtable();
                installer.Install(state);
                installer.Commit(state);

                Console.WriteLine("Service installed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Installation failed: {ex.Message}");
            }
        }

        static void UninstallService()
        {
            try
            {
                // 获取当前程序集
                var assemblyPath = Assembly.GetExecutingAssembly().Location;

                // 创建安装程序
                var installer = new AssemblyInstaller(assemblyPath, null);
                installer.UseNewContext = true;

                // 卸载服务
                var state = new Hashtable();
                installer.Uninstall(state);

                Console.WriteLine("Service uninstalled successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Uninstallation failed: {ex.Message}");
            }
        }
    }


}
