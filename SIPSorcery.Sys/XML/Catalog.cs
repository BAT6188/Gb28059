using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace SIPSorcery.Sys.XML
{
    /// <summary>
    /// 目录报文解析
    /// </summary>
    [XmlRoot("Action")]
    public class Catalog : XmlHelper<Catalog>
    {
        private static Catalog _instance;

        /// <summary>
        /// 以单例模式访问
        /// </summary>
        public static Catalog Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new Catalog();
                return _instance;
            }
        }

        /// <summary>
        /// 指令(Catalog)
        /// </summary>
        [XmlElement("Variable")]
        public VariableType Variable { get; set; }

        /// <summary>
        /// 地址编码
        /// </summary>
        [XmlElement("Parent")]
        public long Parent { get; set; }

        /// <summary>
        /// 目录下总共有多少
        /// </summary>
        [XmlElement("TotalSubNum")]
        public int TotalSubNum { get; set; }

        /// <summary>
        /// 总共有多少在线
        /// </summary>
        [XmlElement("TotalOnlineSubNum")]
        public int TotalOnlineSubNum { get; set; }

        /// <summary>
        /// 数量
        /// </summary>
        [XmlElement("SubNum")]
        public int SubNum { get; set; }

        /// <summary>
        /// 列表
        /// </summary>
        [XmlElement("SubList")]
        public SubList SubLists { get; set; }

        public class SubList
        {
            private List<Item> _item = new List<Item>();

            /// <summary>
            /// 项目
            /// </summary>
            [XmlElement("Item")]
            public List<Item> Items
            {
                get
                {
                    return _item;
                }
            }
        }

        /// <summary>
        /// 列表项
        /// </summary>
        public class Item
        {
            /// <summary>
            /// 显示名
            /// </summary>
            [XmlElement("Name")]
            public string Name { get; set; }

            /// <summary>
            /// 地址编码
            /// </summary>
            [XmlElement("Address")]
            public string Address { get; set; }

            /// <summary>
            /// 类型
            /// </summary>
            [XmlElement("ResType")]
            public int RType { get; set; }

            /// <summary>
            /// 子类型
            /// </summary>
            [XmlElement("ResSubType")]
            public int RSubType { get; set; }

            /// <summary>
            /// 权限功能码
            /// </summary>
            [XmlElement("Privilege")]
            public int Privilege { get; set; }

            /// <summary>
            /// 活动状态
            /// </summary>
            [XmlElement("Status")]
            public int State { get; set; }

            /// <summary>
            /// 经度
            /// </summary>
            [XmlElement("Longitude")]
            public double Longitude { get; set; }

            /// <summary>
            /// 纬度
            /// </summary>
            [XmlElement("Latitude")]
            public double Latitude { get; set; }

            /// <summary>
            /// 海拔高度
            /// </summary>
            [XmlElement("Elevation")]
            public double Elevation { get; set; }

            /// <summary>
            /// 道路名称
            /// </summary>
            [XmlElement("Roadway")]
            public string Roadway { get; set; }

            /// <summary>
            /// 位置桩号
            /// </summary>
            [XmlElement("PileNo")]
            public int PileNo { get; set; }

            /// <summary>
            /// 区域编号
            /// </summary>
            [XmlElement("AreaNo")]
            public int AreaNo { get; set; }

            /// <summary>
            /// 操作类型
            /// </summary>
            [XmlElement("OperateType")]
            public Operate OperateType { get; set; }

            /// <summary>
            /// 更新时间 格式： YYYYMMDDTHHMMSSZ
            /// </summary>
            [XmlElement("UpdateTime")]
            public string UpdateTime { get; set; }
        }
    }

    /// <summary>
    /// 二级资源类型
    /// </summary>
    public enum ResSubType
    {
        /// <summary>
        /// 可控标清球机(或带云台标清枪机)
        /// </summary>
        ControllableLowCamera = 0,
        /// <summary>
        /// 不可控标清球机(或不可控标清枪机)
        /// </summary>
        UnControllableLowCamera = 1,
        /// <summary>
        /// 可控高清球机(或带云台高清枪机)
        /// </summary>
        ControllableHighCamera = 2,
        /// <summary>
        /// 不可控高清球机(或不可控高清枪机)
        /// </summary>
        UnControllableHighCamera = 3,
        /// <summary>
        /// 移动监控
        /// </summary>
        MoveMonitor = 4,
        /// <summary>
        /// 其他
        /// </summary>
        Other = 5
    }

    /// <summary>
    /// 资源类型
    /// </summary>
    public enum ResType
    {
        /// <summary>
        /// 域节点(目录或组织)
        /// </summary>
        DomainNode = 0,
        /// <summary>
        /// 摄像机
        /// </summary>
        Camera = 1,
        /// <summary>
        /// 输入开关量
        /// </summary>
        InOnOffValue = 2,
        /// <summary>
        /// 输出开关量
        /// </summary>
        OutOnOffValue = 3
    }

    /// <summary>
    /// 设备状态
    /// </summary>
    public enum Status
    {
        /// <summary>
        /// 正常
        /// </summary>
        Normal = 0,
        /// <summary>
        /// 不正常
        /// </summary>
        Unusual = 1,
        /// <summary>
        /// 报修中
        /// </summary>
        Warranty = 2,
        /// <summary>
        /// 搬迁中
        /// </summary>
        Relocation = 3,
        /// <summary>
        /// 在建
        /// </summary>
        Build = 4,
        /// <summary>
        /// 断电
        /// </summary>
        NoElectric = 5
    }

    /// <summary>
    /// 操作类型
    /// </summary>
    public enum Operate
    {
        /// <summary>
        /// 添加共享
        /// </summary>
        ADD = 0,
        /// <summary>
        /// 取消共享
        /// </summary>
        DEL = 1,
        /// <summary>
        /// 修改共享
        /// </summary>
        MOD = 2,
        /// <summary>
        /// 保留
        /// </summary>
        OTH = 3
    }
}
