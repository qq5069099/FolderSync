using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FolderSync
{
    public partial class Service : ServiceBase
    {
        private List<Cloner> _cloners = new List<Cloner>();
        private BugUxml _bugUxml = null;

        public Service()
        {
            InitializeComponent();
            ServiceName = "FolderSync";
        }

        protected override void OnStart(string[] args)
        {
            //同步文件夹 - 代码备份
            try
            {
                var FolderSyncConfig = ConfigJson.json["FolderSync"];
                foreach (var item in FolderSyncConfig)
                {
                    var cloner = new Cloner(item[0].ToString(), ((JArray)item[1]).ToObject<List<string>>());
                    _ = cloner.StartServices();
                    _cloners.Add(cloner);
                }

                var UXMLTemplatePath = ConfigJson.json["UXMLTemplatePath"]?.ToString();
                if (!string.IsNullOrEmpty(UXMLTemplatePath))
                {
                    _bugUxml = new BugUxml(UXMLTemplatePath);
                    _ = _bugUxml.StartServices();
                }
            }
            catch (Exception ex)
            {
                Logger.i("异常: " + ex.Message);
            }
        }

        protected override void OnStop()
        {
            foreach (var cloner in _cloners)
            {
                cloner.StopServices();
            }

            _bugUxml.StopServices();
        }
    }
}
