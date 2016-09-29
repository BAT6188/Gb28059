using log4net;
using SIPSorcery.Net;
using SIPSorcery.Servers.Packet;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Sys.XML;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SIPSorcery.Servers
{
    /// <summary>
    /// SIP服务状态
    /// </summary>
    public enum SipServiceStatus
    {
        /// <summary>
        /// 等待
        /// </summary>
        Wait = 0,
        /// <summary>
        /// 初始化完成
        /// </summary>
        Inited = 1
    }

    /// <summary>
    /// sip监控核心处理
    /// </summary>
    public class SipMonitorCore
    {
        #region 私有字段
        private static ILog logger = AppState.logger;
        /// <summary>
        /// sip请求
        /// </summary>
        private SIPRequest _sipRequest;
        /// <summary>
        /// 本地sip终结点
        /// </summary>
        private SIPEndPoint _localEndPoint;
        /// <summary>
        /// 远程sip终结点
        /// </summary>
        private SIPEndPoint _remoteEndPoint;
        /// <summary>
        /// sip消息核心处理
        /// </summary>
        private SipMessageCore _messageCore;
        /// <summary>
        /// sip实时视频请求
        /// </summary>
        private SIPRequest _realReqSession;
        /// <summary>
        /// sip传输请求
        /// </summary>
        private SIPTransport _m_sipTransport;
        /// <summary>
        /// sip账户
        /// </summary>
        private SIPAccount _sipAccount;
        /// <summary>
        /// 用户代理
        /// </summary>
        private string _userAgent;
        /// <summary>
        /// 摄像机编码
        /// </summary>
        private string _cameraId;
        /// <summary>
        /// sip初始化状态
        /// </summary>
        private bool _sipInited;
        /// <summary>
        /// rtp数据通道
        /// </summary>
        private RTPChannel _rtpChannel;
        /// <summary>
        /// 远程RTP终结点
        /// </summary>
        private IPEndPoint _rtpRemoteEndPoint;
        /// <summary>
        /// 远程RTCP终结点
        /// </summary>
        private IPEndPoint _rtcpRemoteEndPoint;
        /// <summary>
        /// 媒体端口(0rtp port,1 rtcp port)
        /// </summary>
        private int[] _mediaPort;
        private byte[] _publicByte = new byte[0];
        private FileStream _m_fs;
        /// <summary>
        /// rtcp套接字连接
        /// </summary>
        private Socket _rtcpSocket;
        /// <summary>
        /// rtcp时间戳
        /// </summary>
        private uint _rtcpTimestamp = 0;
        /// <summary>
        /// rtcp同步源
        /// </summary>
        private uint _rtcpSyncSource = 0;
        private uint _senderPacketCount = 0;
        private uint _senderOctetCount = 0;
        private DateTime _senderLastSentAt = DateTime.MinValue;
        /// <summary>
        /// sip状态
        /// </summary>
        public event Action<SipServiceStatus> SipStatusHandler; 
        #endregion

        #region 构造函数
        /// <summary>
        /// sip监控初始化
        /// </summary>
        /// <param name="messageCore">sip消息</param>
        /// <param name="sipTransport">sip传输</param>
        /// <param name="cameraId">摄像机编码</param>
        public SipMonitorCore(SipMessageCore messageCore, SIPTransport sipTransport, string cameraId)
        {
            _messageCore = messageCore;
            _m_sipTransport = sipTransport;
            _cameraId = cameraId;
            _userAgent = messageCore.m_userAgent;
            _rtcpSyncSource = Convert.ToUInt32(Crypto.GetRandomInt(0, 9999999));

            _messageCore.SipRequestInited += messageCore_SipRequestInited;
            _messageCore.SipInviteVideoOK += messageCore_SipInviteVideoOK;
        } 
        #endregion

        #region 确认视频请求
        /// <summary>
        /// 实时视频请求成功事件处理
        /// </summary>
        /// <param name="res"></param>
        private void messageCore_SipInviteVideoOK(SIPResponse res)
        {
            if (_realReqSession == null)
            {
                return;
            }
            //同一会话消息
            if (_realReqSession.Header.CallId == res.Header.CallId)
            {
                RealVideoRes realRes = RealVideoRes.Instance.Read(res.Body);
                GetRemoteRtcp(realRes.Socket);

                SIPRequest ackReq = AckRequest(res);
                _m_sipTransport.SendRequest(_remoteEndPoint, ackReq);
            }
        } 
        #endregion

        #region rtp/rtcp事件处理
        /// <summary>
        /// sip初始化完成事件
        /// </summary>
        /// <param name="sipRequest">sip请求</param>
        /// <param name="localEndPoint">本地终结点</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <param name="sipAccount">sip账户</param>
        private void messageCore_SipRequestInited(SIPRequest sipRequest, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, SIPAccount sipAccount)
        {
            _sipInited = true;
            _sipRequest = sipRequest;
            _localEndPoint = localEndPoint;
            _remoteEndPoint = remoteEndPoint;
            _sipAccount = sipAccount;

            _rtpRemoteEndPoint = new IPEndPoint(remoteEndPoint.Address, remoteEndPoint.Port);
            _rtpChannel = new RTPChannel(_rtpRemoteEndPoint);
            _rtpChannel.OnFrameReady += _rtpChannel_OnFrameReady;
            _rtpChannel.OnControlDataReceived += _rtpChannel_OnControlDataReceived;

            if (SipStatusHandler != null)
            {
                SipStatusHandler(SipServiceStatus.Inited);
            }
            _messageCore.SipRequestInited -= messageCore_SipRequestInited;
        }

        /// <summary>
        /// rtp包回调事件处理
        /// </summary>
        /// <param name="frame"></param>
        private void _rtpChannel_OnFrameReady(RTPFrame frame)
        {
            byte[] buffer = frame.GetFramePayload();
            Write(buffer);
        }

        /// <summary>
        /// rtcp包回调事件处理
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="rtcpSocket"></param>
        private void _rtpChannel_OnControlDataReceived(byte[] buffer, Socket rtcpSocket)
        {
            _rtcpSocket = rtcpSocket;
            DateTime packetTimestamp = DateTime.Now;
            _rtcpTimestamp = RTPChannel.DateTimeToNptTimestamp90K(DateTime.Now);
            if (_rtcpRemoteEndPoint != null)
            {
                SendRtcpSenderReport(RTPChannel.DateTimeToNptTimestamp(packetTimestamp), _rtcpTimestamp);
            }
        }

        /// <summary>
        /// 发送rtcp包
        /// </summary>
        /// <param name="ntpTimestamp"></param>
        /// <param name="rtpTimestamp"></param>
        private void SendRtcpSenderReport(ulong ntpTimestamp, uint rtpTimestamp)
        {
            try
            {
                RTCPPacket senderReport = new RTCPPacket(_rtcpSyncSource, ntpTimestamp, rtpTimestamp, _senderPacketCount, _senderOctetCount);
                var bytes = senderReport.GetBytes();
                _rtcpSocket.BeginSendTo(bytes, 0, bytes.Length, SocketFlags.None, _rtcpRemoteEndPoint, SendRtcpCallback, _rtcpSocket);
                _senderLastSentAt = DateTime.Now;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendRtcpSenderReport. " + excp);
            }
        }

        /// <summary>
        /// 发送rtcp回调
        /// </summary>
        /// <param name="ar"></param>
        private void SendRtcpCallback(IAsyncResult ar)
        {
            try
            {
                _rtcpSocket.EndSend(ar);
            }
            catch (Exception ex)
            {
                logger.Warn("Exception Rtcp", ex);
            }
        } 
        #endregion

        #region sip视频请求
        /// <summary>
        /// 实时视频请求
        /// </summary>
        public void RealVideoRequest()
        {
            SipInitialize();
            _mediaPort = _messageCore.SetMediaPort();

            SIPRequest request = InviteRequest();
            RealVideo real = new RealVideo()
            {
                Address = _cameraId,
                Variable = VariableType.RealMedia,
                Privilege = 90,
                Format = "4CIF CIF QCIF 720p 1080p",
                Video = "H.264",
                Audio = "G.711",
                MaxBitrate = 800,
                Socket = this.ToString()
            };

            string xmlBody = RealVideo.Instance.Save<RealVideo>(real);
            request.Body = xmlBody;
            _m_sipTransport.SendRequest(_remoteEndPoint, request);

            //启动RTP通道
            _rtpChannel.IsClosed = false;
            _rtpChannel.ReservePorts(_mediaPort[0], _mediaPort[1]);
            _rtpChannel.Start();
        }

        /// <summary>
        /// 实时视频取消
        /// </summary>
        public void RealVideoBye()
        {
            SipInitialize();
            _rtpChannel.Close();
            if (_realReqSession == null)
            {
                return;
            }
            SIPRequest req = ByeRequest();
            _m_sipTransport.SendRequest(_remoteEndPoint, req);
        }

        /// <summary>
        /// 查询前端设备信息
        /// </summary>
        /// <param name="cameraId"></param>
        public void DeviceQuery(string cameraId)
        {
            SipInitialize();
            Device dev = new Device()
            {
                Privilege = 90,
                Variable = VariableType.DeviceInfo
            };
            SIPRequest req = DeviceReq(cameraId);
            string xmlBody = Device.Instance.Save<Device>(dev);
            req.Body = xmlBody;
            _m_sipTransport.SendRequest(_remoteEndPoint, req);
        }

        /// <summary>
        /// 设备目录查询
        /// </summary>
        /// <param name="cameraId">摄像机地址编码</param>
        public void DeviceListQuery(string cameraId)
        {
            SipInitialize();
            SIPRequest req = QueryItems();
            DeviceItems item = new DeviceItems()
            {
                ItemList = new DeviceItems.Query()
                {
                    Address = cameraId,
                    Privilege = 90,
                    Variable = VariableType.ItemList,
                    FromIndex = 1,
                    ToIndex = 200
                }
            };
            string xmlBody = DeviceItems.Instance.Save<DeviceItems>(item);
            req.Body = xmlBody;
            _m_sipTransport.SendRequest(_remoteEndPoint, req);
        }

        private SIPRequest ByeRequest()
        {
            SIPURI uri = new SIPURI(_cameraId, _remoteEndPoint.ToHost(), "");
            SIPRequest byeRequest = _m_sipTransport.GetRequest(SIPMethodsEnum.BYE, uri);
            SIPFromHeader from = new SIPFromHeader(null, _sipRequest.URI, _realReqSession.Header.From.FromTag);
            SIPHeader header = new SIPHeader(from, byeRequest.Header.To, _realReqSession.Header.CSeq, _realReqSession.Header.CallId);
            header.ContentType = "application/DDCP";
            header.Expires = byeRequest.Header.Expires;
            header.CSeqMethod = byeRequest.Header.CSeqMethod;
            header.Vias = byeRequest.Header.Vias;
            header.MaxForwards = byeRequest.Header.MaxForwards;
            header.UserAgent = _userAgent;
            byeRequest.Header.From = from;
            byeRequest.Header = header;
            return byeRequest;
        }

        /// <summary>
        /// 前端设备信息请求
        /// </summary>
        /// <param name="cameraId"></param>
        /// <returns></returns>
        private SIPRequest DeviceReq(string cameraId)
        {
            SIPURI remoteUri = new SIPURI(cameraId, _remoteEndPoint.ToHost(), "");
            SIPURI localUri = new SIPURI(_sipAccount.LocalSipId, _localEndPoint.ToHost(), "");
            SIPFromHeader from = new SIPFromHeader(null, localUri, CallProperties.CreateNewTag());
            SIPToHeader to = new SIPToHeader(null, remoteUri, null);
            SIPRequest queryReq = _m_sipTransport.GetRequest(SIPMethodsEnum.DO, remoteUri);
            queryReq.Header.Contact = null;
            queryReq.Header.From = from;
            queryReq.Header.Allow = null;
            queryReq.Header.To = to;
            queryReq.Header.CSeq = CallProperties.CreateNewCSeq();
            queryReq.Header.CallId = CallProperties.CreateNewCallId();
            queryReq.Header.ContentType = "Application/DDCP";
            return queryReq;
        }

        /// <summary>
        /// 查询设备目录请求
        /// </summary>
        /// <returns></returns>
        private SIPRequest QueryItems()
        {
            SIPURI remoteUri = new SIPURI(_sipAccount.RemoteSipId, _remoteEndPoint.ToHost(), "");
            SIPURI localUri = new SIPURI(_sipAccount.LocalSipId, _localEndPoint.ToHost(), "");
            SIPFromHeader from = new SIPFromHeader(null, localUri, CallProperties.CreateNewTag());
            SIPToHeader to = new SIPToHeader(null, remoteUri, null);
            SIPRequest queryReq = _m_sipTransport.GetRequest(SIPMethodsEnum.DO, remoteUri);
            queryReq.Header.From = from;
            queryReq.Header.Contact = null;
            queryReq.Header.Allow = null;
            queryReq.Header.To = to;
            queryReq.Header.CSeq = CallProperties.CreateNewCSeq();
            queryReq.Header.CallId = CallProperties.CreateNewCallId();
            queryReq.Header.ContentType = "Application/DDCP";
            return queryReq;
        }

        /// <summary>
        /// 监控视频请求
        /// </summary>
        /// <returns></returns>
        private SIPRequest InviteRequest()
        {
            SIPURI uri = new SIPURI(_cameraId, _remoteEndPoint.ToHost(), "");
            SIPRequest inviteRequest = _m_sipTransport.GetRequest(SIPMethodsEnum.INVITE, uri);
            SIPFromHeader from = new SIPFromHeader(null, _sipRequest.URI, CallProperties.CreateNewTag());
            SIPHeader header = new SIPHeader(from, inviteRequest.Header.To, CallProperties.CreateNewCSeq(), CallProperties.CreateNewCallId());
            header.ContentType = "application/DDCP";
            header.Expires = inviteRequest.Header.Expires;
            header.CSeqMethod = inviteRequest.Header.CSeqMethod;
            header.Vias = inviteRequest.Header.Vias;
            header.MaxForwards = inviteRequest.Header.MaxForwards;
            header.UserAgent = _userAgent;
            inviteRequest.Header.From = from;
            inviteRequest.Header = header;
            _realReqSession = inviteRequest;
            return inviteRequest;
        }

        /// <summary>
        /// 确认接收视频请求
        /// </summary>
        /// <param name="response">响应消息</param>
        /// <returns></returns>
        private SIPRequest AckRequest(SIPResponse response)
        {
            SIPURI uri = new SIPURI(response.Header.To.ToURI.User, _remoteEndPoint.ToHost(), "");
            SIPRequest ackRequest = _m_sipTransport.GetRequest(SIPMethodsEnum.ACK, uri);
            SIPFromHeader from = new SIPFromHeader(null, _sipRequest.URI, response.Header.CallId);
            from.FromTag = response.Header.From.FromTag;
            SIPHeader header = new SIPHeader(from, response.Header.To, response.Header.CSeq, response.Header.CallId);
            header.To.ToTag = null;
            header.CSeqMethod = SIPMethodsEnum.ACK;
            header.Vias = response.Header.Vias;
            header.MaxForwards = response.Header.MaxForwards;
            header.ContentLength = response.Header.ContentLength;
            header.UserAgent = _userAgent;
            header.Allow = null;
            ackRequest.Header = header;
            return ackRequest;
        } 
        #endregion

        #region 私有方法
        /// <summary>
        /// sip初始化状态跟踪
        /// </summary>
        private void SipInitialize()
        {
            if (!_sipInited)
            {
                if (SipStatusHandler != null)
                {
                    SipStatusHandler(SipServiceStatus.Wait);
                }
                return;
            }
        }

        /// <summary>
        /// 获取远程rtcp终结点(192.168.10.250 UDP 5000)
        /// </summary>
        /// <param name="socket"></param>
        private void GetRemoteRtcp(string socket)
        {
            string[] split = socket.Split(' ');
            if (split.Length < 3)
            {
                return;
            }

            try
            {
                IPAddress remoteIP = _remoteEndPoint.Address;
                IPAddress.TryParse(split[0], out remoteIP);
                int rtcpPort = _mediaPort[1];
                int.TryParse(split[2], out rtcpPort);
                _rtcpRemoteEndPoint = new IPEndPoint(remoteIP, rtcpPort + 1);
            }
            catch (Exception ex)
            {
                logger.Warn("remote rtp ip/port error", ex);
            }
        }

        public override string ToString()
        {
            return _localEndPoint.Address.ToString() + " UDP " + _mediaPort[0];
        } 
        #endregion

        #region 处理PS数据
        public void Write(byte[] buffer)
        {
            try
            {
                _publicByte = copybyte(_publicByte, buffer);
                int i = 0;
                int BANum = 0;
                if (buffer == null || buffer.Length < 5)
                {
                    return;
                }
                while (i < _publicByte.Length)
                {
                    if (_publicByte[i] == 0x00 && _publicByte[i + 1] == 0x00 && _publicByte[i + 2] == 0x01 && _publicByte[i + 3] == 0xBA)
                    {
                        BANum++;
                        if (BANum == 2)
                        {
                            break;
                        }
                    }
                    i++;
                }

                if (BANum == 2)
                {
                    byte[] psByte = new byte[i];
                    Array.Copy(_publicByte, 0, psByte, 0, i);

                    //处理psByte
                    doPsByte(psByte);

                    byte[] overByte = new byte[_publicByte.Length - i];
                    Array.Copy(_publicByte, i, overByte, 0, overByte.Length);
                    _publicByte = overByte;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public byte[] copybyte(byte[] a, byte[] b)
        {
            byte[] c = new byte[a.Length + b.Length];
            a.CopyTo(c, 0);
            b.CopyTo(c, a.Length);
            return c;
        }

        private void doPsByte(byte[] psDate)
        {
            Stream msStream = new System.IO.MemoryStream(psDate);
            List<PESPacket> videoPESList = new List<PESPacket>();

            while (msStream.Length - msStream.Position > 4)
            {
                bool findStartCode = msStream.ReadByte() == 0x00 && msStream.ReadByte() == 0x00 && msStream.ReadByte() == 0x01 && msStream.ReadByte() == 0xE0;
                if (findStartCode)
                {
                    msStream.Seek(-4, SeekOrigin.Current);
                    var pesVideo = new PESPacket();
                    pesVideo.SetBytes(msStream);
                    var esdata = pesVideo.PES_Packet_Data;
                    videoPESList.Add(pesVideo);
                }
            }
            msStream.Close();
            HandlES(videoPESList);
        }

        private void HandlES(List<PESPacket> videoPESList)
        {
            var stream = new MemoryStream();
            foreach (var item in videoPESList)
            {
                stream.Write(item.PES_Packet_Data, 0, item.PES_Packet_Data.Length);
            }
            if (videoPESList.Count == 0)
            {
                stream.Close();
                return;
            }
            long tick = videoPESList.FirstOrDefault().GetVideoTimetick();
            var esdata = stream.ToArray();
            stream.Close();
            if (this._m_fs == null)
            {
                this._m_fs = new FileStream("D:\\" + _cameraId + ".h264", FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, 50 * 1024);
            }
            _m_fs.Write(esdata, 0, esdata.Length);
            videoPESList.Clear();
        }
        #endregion
    }
}
