using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace FolderSync
{
    [RunInstaller(true)] // 必须标记为 true
    public class ProjectInstaller : Installer
    {
        private ServiceProcessInstaller processInstaller;
        private ServiceInstaller serviceInstaller;

        public ProjectInstaller()
        {
            // 初始化安装程序
            processInstaller = new ServiceProcessInstaller();
            serviceInstaller = new ServiceInstaller();

            // 设置运行账户（LocalSystem、LocalService、NetworkService 或自定义账户）
            processInstaller.Account = ServiceAccount.LocalSystem;

            // 设置服务名称和显示名称
            serviceInstaller.ServiceName = "FolderSync";
            serviceInstaller.DisplayName = "FolderSync张工同步工具";
            serviceInstaller.Description = "文件夹自动同步工具.";
            serviceInstaller.StartType = ServiceStartMode.Automatic; // 自动启动

            // 添加安装程序
            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
        }
    }
}
