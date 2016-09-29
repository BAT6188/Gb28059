using log4net;
using SIPSorcery.Persistence;
using SIPSorcery.Servers;
using SIPSorcery.SIP.App;
using SIPSorcery.SIPRegistrar;
using SIPSorcery.Sys;
using SIPSorcery.Sys.XML;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace Gb28059_Client
{
    public partial class Form1 : Form
    {
        private delegate void SetCatalog(Catalog cata);
        private delegate void SetInitStatus(SipServiceStatus state);

        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;
        private static readonly string m_sipAccountsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_SIPACCOUNTS_FILENAME;

        private SIPRegistrarDaemon daemon;
        private static ILog logger = AppState.logger;
        private static StorageTypes m_sipRegistrarStorageType;
        private static string m_sipRegistrarStorageConnStr;

        private string _cameraId;
        private List<string> _deviceList;

        private void Initialize()
        {
            _deviceList = new List<string>();
            m_sipRegistrarStorageType = (AppState.GetConfigSetting(m_storageTypeKey) != null) ?
            StorageTypesConverter.GetStorageType(AppState.GetConfigSetting(m_storageTypeKey)) :
            StorageTypes.Unknown;
            m_sipRegistrarStorageConnStr = AppState.GetConfigSetting(m_connStrKey);

            if (m_sipRegistrarStorageType == StorageTypes.Unknown || m_sipRegistrarStorageConnStr.IsNullOrBlank())
            {
                throw new ApplicationException("The SIP Registrar cannot start with no persistence settings.");
            }

            lBoxDevice.Items.Add("34020000001320000077");
            lBoxDevice.Items.Add("34020000001320000088");
            lBoxDevice.SelectedIndex = 0;
            _deviceList = lBoxDevice.Items.Cast<string>().ToList();
        }

        private string[] ParseEndPoint(string textEndPoint)
        {
            string[] endPoint = textEndPoint.Split(':');
            return endPoint;
        }

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.Initialize();
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            SIPAssetPersistor<SIPAccount> sipAccountsPersistor = SIPAssetPersistorFactory<SIPAccount>.CreateSIPAssetPersistor(m_sipRegistrarStorageType, m_sipRegistrarStorageConnStr, m_sipAccountsXMLFilename);
            SIPDomainManager sipDomainManager = new SIPDomainManager(m_sipRegistrarStorageType, m_sipRegistrarStorageConnStr);

            daemon = new SIPRegistrarDaemon(sipDomainManager.GetDomain, sipAccountsPersistor.Get, SIPRequestAuthenticator.AuthenticateSIPRequest, _deviceList);
            lblInit.Text = "sip服务已启动，等待初始化。。。";
            lblInit.ForeColor = Color.Blue;
            Thread daemonThread = new Thread(daemon.Start);
            daemonThread.Start();
            daemon.m_registrarCore.CatalogHandler += daemon_CatalogHandler;
            foreach (KeyValuePair<string, SipMonitorCore> monitorCore in daemon.m_registrarCore.MonitorItems)
            {
                monitorCore.Value.SipStatusHandler += monitorCore_SipStatusHandler;
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            daemon.Stop();
            lblInit.Text = "sip服务已停止，请启动后再进行业务";
            lblInit.ForeColor = Color.Red;
        }

        private void monitorCore_SipStatusHandler(SipServiceStatus state)
        {
            if (lblInit.InvokeRequired)
            {
                SetInitStatus init = new SetInitStatus(SetInitText);
                this.Invoke(init, state);
            }
            else
            {
                SetInitText(state);
            }
        }

        private void SetInitText(SipServiceStatus state)
        {
            if (state == SipServiceStatus.Inited)
            {
                lblInit.Text = "sip服务初始化完成，可进行业务";
                lblInit.ForeColor = Color.LimeGreen;
            }
            else if (state == SipServiceStatus.Wait)
            {
                lblInit.Text = "sip服务未初始化完成，请稍后再试";
                lblInit.ForeColor = Color.Gold;
            }
        }

        private void daemon_CatalogHandler(Catalog cata)
        {
            if (this.lBoxDevice.InvokeRequired)
            {
                SetCatalog catalog = new SetCatalog(SetText);
                this.Invoke(catalog, cata);
            }
            else
            {
                SetText(cata);
            }
        }

        private void SetText(Catalog cata)
        {
            foreach (Catalog.Item dev in cata.SubLists.Items)
            {
                if (lBoxDevice.Items.Contains(dev.Address))
                {
                    return;
                }
                lBoxDevice.Items.Add(dev.Address);
            }
        }

        private void btnQuery_Click(object sender, EventArgs e)
        {
            //daemon.m_registrarCore.DeviceListQuery("01030000000099");
        }

        private void btnDevice_Click(object sender, EventArgs e)
        {
            //daemon.m_registrarCore.DeviceQuery(_cameraId);
        }

        /// <summary>
        /// 实时视频
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnReal_Click(object sender, EventArgs e)
        {
            daemon.m_registrarCore.MonitorItems[_cameraId].RealVideoRequest();
        }

        /// <summary>
        /// 取消实时视频
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnCancel_Click(object sender, EventArgs e)
        {
            daemon.m_registrarCore.MonitorItems[_cameraId].RealVideoBye();
        }

        /// <summary>
        /// 摄像机选择事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void lBoxDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            _cameraId = lBoxDevice.SelectedItem.ToString();
        }

        private void btnXML_Click(object sender, EventArgs e)
        {
            IPEndPoint end = new IPEndPoint(IPAddress.Parse("192.168.10.146"), 21000);

            string id = Guid.NewGuid().ToString();
            DeviceItemsRes res = new DeviceItemsRes();
            List<DeviceItemsRes.Item> items = new List<DeviceItemsRes.Item>();
            var item1 = new DeviceItemsRes.Item()
            {
                Name = "1号摄像头",
                Address = "东直门街道",
                RType = 1,
                RSubType = 1,
                Privilege = 90,
                State = 1,
                Longitude = 114.7,
                Latitude = 224.9,
                Elevation = 3000,
                Roadway = "东直门马路",
                PileNo = 1,
                AreaNo = 1,
                UpdateTime = "20160920T162627"
            };
            var item2 = new DeviceItemsRes.Item()
            {
                Name = "2号摄像头",
                Address = "东直门街道",
                RType = 2,
                RSubType = 2,
                Privilege = 90,
                State = 2,
                Longitude = 114.7,
                Latitude = 224.9,
                Elevation = 3000,
                Roadway = "东直门马路",
                PileNo = 2,
                AreaNo = 2,
                UpdateTime = "20160920T163019"
            };
            items.Add(item1);
            items.Add(item2);
            DeviceItemsRes.QueryResponse query = new DeviceItemsRes.QueryResponse()
            {
                Variable = VariableType.ItemList,
                Parent = "123123123",
                TotalSubNum = 1000,
                TotalOnlineSubNum = 990,
                SubNum = 200,
                FromIndex = 1,
                ToIndex = 200,
                SubListItem = new DeviceItemsRes.SubList()
                {
                    Items = items
                }
            };
            res.Query = query;
            string xmlBody = DeviceItemsRes.Instance.Save(res);
        }
    }
}
