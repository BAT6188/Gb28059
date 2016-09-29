// ============================================================================
// FileName: RegistrarCore.cs
//
// Description:
// SIP Registrar that strives to be RFC3822 compliant.
//
// Author(s):
// Aaron Clauson
//
// History:
// 21 Jan 2006	Aaron Clauson	Created.
// 22 Nov 2007  Aaron Clauson   Fixed bug where binding refresh was generating a duplicate exception if the uac endpoint changed but the contact did not.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2007 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;
using SIPSorcery.Sys.XML;
using SIPSorcery.Net;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Linq;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{

    public enum SipResultEnum
    {
        Unknown = 0,
        Trying = 1,
        Forbidden = 2,
        Authenticated = 3,
        AuthenticationRequired = 4,
        Failed = 5,
        Error = 6,
        RequestWithNoUser = 7,
        RemoveAllRegistrations = 9,
        DuplicateRequest = 10,
        AuthenticatedFromCache = 11,
        RequestWithNoContact = 12,
        NonRegisterMethod = 13,
        DomainNotServiced = 14,
        IntervalTooBrief = 15,
        SwitchboardPaymentRequired = 16,
    }

    /// <summary>
    /// SIP消息处理
    /// The registrar core is the class that actually does the work of receiving registration requests and populating and
    /// maintaining the SIP registrations list.
    /// 
    /// From RFC 3261 Chapter "10.2 Constructing the REGISTER Request"
    /// - Request-URI: The Request-URI names the domain of the location service for which the registration is meant.
    /// - The To header field contains the address of record whose registration is to be created, queried, or modified.  
    ///   The To header field and the Request-URI field typically differ, as the former contains a user name. 
    /// 
    /// [ed Therefore:
    /// - The Request-URI inidcates the domain for the registration and should match the domain in the To address of record.
    /// - The To address of record contians the username of the user that is attempting to authenticate the request.]
    /// 
    /// Method of operation:
    ///  - New SIP messages received by the SIP Transport layer and queued before being sent to RegistrarCode for processing. For requests
    ///    or response that match an existing REGISTER transaction the SIP Transport layer will handle the retransmit or drop the request if
    ///    it's already being processed.
    ///  - Any non-REGISTER requests received by the RegistrarCore are responded to with not supported,
    ///  - If a persistence is being used to store registered contacts there will generally be a number of threads running for the
    ///    persistence class. Of those threads there will be one that runs calling the SIPRegistrations.IdentifyDirtyContacts. This call identifies
    ///    expired contacts and initiates the sending of any keep alive and OPTIONs requests.
    /// </summary>
    public class SipMessageCore
    {
        #region Private Fields
        private const int MAX_REGISTER_QUEUE_SIZE = 1000;
        private const int MAX_PROCESS_REGISTER_SLEEP = 10000;
        private const string REGISTRAR_THREAD_NAME_PREFIX = "sipregistrar-core";
        private const int MINIMUM_EXPIRY_SECONDS = 30;
        private int _mediaPortStart = 21000;
        private int _mediaPortEnd = 23000;

        private static ILog logger = AppState.GetLogger("sipregistrar");

        private SIPTransport m_sipTransport;
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;
        private SIPAccount _sipAccount;
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;
        private SIPAuthenticateRequestDelegate SIPRequestAuthenticator_External;

        private bool m_mangleUACContact = false;            // Whether or not to adjust contact URIs that contain private hosts to the value of the bottom via received socket.
        private bool m_strictRealmHandling = false;         // If true the registrar will only accept registration requests for domains it is configured for, otherwise any realm is accepted.
        /// <summary>
        /// 用户代理
        /// </summary>
        internal string m_userAgent;
        private SIPUserAgentConfigurationManager m_userAgentConfigs;
        private Queue<SIPNonInviteTransaction> m_registerQueue = new Queue<SIPNonInviteTransaction>();
        private AutoResetEvent m_registerARE = new AutoResetEvent(false);
        private string m_switchboarduserAgentPrefix;
        /// <summary>
        /// sip请求
        /// </summary>
        private SIPRequest m_sipRequest;
        /// <summary>
        /// sip本地终结点
        /// </summary>
        private SIPEndPoint m_localEndPoint;
        /// <summary>
        /// sip远程终结点
        /// </summary>
        private SIPEndPoint m_remoteEndPoint;
        /// <summary>
        /// 监控列表项
        /// </summary>
        public IDictionary<string, SipMonitorCore> MonitorItems { get; private set; }
        /// <summary>
        /// 停止注册请求队列处理
        /// </summary>
        public bool Stop;
        #endregion

        #region Public Event
        /// <summary>
        /// 目录推送
        /// </summary>
        public event Action<Catalog> CatalogHandler;
        /// <summary>
        /// SIP请求初始化完成
        /// </summary>
        public event Action<SIPRequest, SIPEndPoint, SIPEndPoint, SIPAccount> SipRequestInited;
        /// <summary>
        /// SIP实时视频响应
        /// </summary>
        public event Action<SIPResponse> SipInviteVideoOK;
        #endregion

        public SipMessageCore(
            SIPTransport sipTransport,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            GetCanonicalDomainDelegate getCanonicalDomain,
            bool mangleUACContact,
            bool strictRealmHandling,
            SIPUserAgentConfigurationManager userAgentConfigs,
            SIPAuthenticateRequestDelegate sipRequestAuthenticator,
            string switchboarduserAgentPrefix,
            List<string> deviceList)
        {
            m_sipTransport = sipTransport;
            GetSIPAccount_External = getSIPAccount;
            GetCanonicalDomain_External = getCanonicalDomain;
            m_mangleUACContact = mangleUACContact;
            m_strictRealmHandling = strictRealmHandling;
            m_userAgentConfigs = userAgentConfigs;
            SIPRequestAuthenticator_External = sipRequestAuthenticator;
            m_switchboarduserAgentPrefix = switchboarduserAgentPrefix;
            m_userAgent = m_userAgentConfigs.DefaultUserAgent ?? SIPConstants.SIP_USER_AGENT;
            MonitorItems = new Dictionary<string, SipMonitorCore>(500);
            foreach (string device in deviceList)
            {
                SipMonitorCore monitor = new SipMonitorCore(this, m_sipTransport, device);
                MonitorItems.Add(device, monitor);
            }
        }

        public void Start(int threadCount)
        {
            Stop = false;
            logger.Debug("SIPRegistrarCore thread started with " + threadCount + " threads.");

            for (int index = 1; index <= threadCount; index++)
            {
                string threadSuffix = index.ToString();
                ThreadPool.QueueUserWorkItem(delegate { ProcessRegisterRequest(REGISTRAR_THREAD_NAME_PREFIX + threadSuffix); });
            }
        }

        public void Shutdown()
        {
            Stop = true;
            MonitorItems.Clear();
            MonitorItems = null;
            m_registerQueue.Clear();
            m_registerQueue = null;
        }

        /// <summary>
        /// 设置媒体(rtp/rtcp)端口号
        /// </summary>
        /// <returns></returns>
        public int[] SetMediaPort()
        {
            var inUseUDPPorts = (from p in IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners() where p.Port >= _mediaPortStart select p.Port).OrderBy(x => x).ToList();

            int rtpPort = 0;
            int controlPort = 0;

            if (inUseUDPPorts.Count > 0)
            {
                // Find the first two available for the RTP socket.
                for (int index = _mediaPortStart; index <= _mediaPortEnd; index++)
                {
                    if (!inUseUDPPorts.Contains(index))
                    {
                        rtpPort = index;
                        break;
                    }
                }

                // Find the next available for the control socket.
                for (int index = rtpPort + 1; index <= _mediaPortEnd; index++)
                {
                    if (!inUseUDPPorts.Contains(index))
                    {
                        controlPort = index;
                        break;
                    }
                }
            }
            else
            {
                rtpPort = _mediaPortStart;
                controlPort = _mediaPortStart + 1;
            }

            if (_mediaPortStart >= _mediaPortEnd)
            {
                _mediaPortStart = 23000;
            }
            _mediaPortStart += 2;
            int[] mediaPort = new int[2];
            mediaPort[0] = rtpPort;
            mediaPort[1] = controlPort;
            return mediaPort;
        }

        /// <summary>
        /// sip请求消息处理
        /// </summary>
        /// <param name="localSIPEndPoint">SIP本地终结点</param>
        /// <param name="remoteEndPoint">SIP远程终结点</param>
        /// <param name="request">sip请求</param>
        public void AddRequestMessage(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest request)
        {
            try
            {
                m_localEndPoint = localSIPEndPoint;
                m_remoteEndPoint = remoteEndPoint;
                m_sipRequest = request;

                _sipAccount = GetSIPAccount_External(s => s.SIPUsername == request.Header.From.FromURI.User);

                if (SipRequestInited != null)
                {
                    SipRequestInited(m_sipRequest, m_localEndPoint, m_remoteEndPoint, _sipAccount);
                }

                if (request.Method == SIPMethodsEnum.DO)
                {
                    //if (!_registerOK)
                    //{
                    //    SIPResponse baRes = GetResponse(localSIPEndPoint, remoteEndPoint, SIPResponseStatusCodesEnum.BadRequest, "", request);
                    //    m_sipTransport.SendResponse(baRes);
                    //}
                    //else
                    //{
                    ProcessDoReqMessage(request);
                    //}
                }
                else if (request.Method == SIPMethodsEnum.NOTIFY)
                {
                    ProcessNotifyReqMessage(request);
                }
                else if (request.Method == SIPMethodsEnum.REGISTER)
                {
                    ProcessRegisterReqMessage(localSIPEndPoint, remoteEndPoint, request);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ProcessRequest (" + remoteEndPoint.ToString() + "). " + excp.Message);
            }
        }

        /// <summary>
        /// sip响应消息处理
        /// </summary>
        /// <param name="localSIPEndPoint">sip本地终结点</param>
        /// <param name="remoteEndPoint">sip远程终结点</param>
        /// <param name="response">sip响应</param>
        public void AddResponseMessage(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse response)
        {
            if (response.Status == SIPResponseStatusCodesEnum.Trying)
            {
                logger.Info("Trying 100 real video invite waitting...");
            }
            else if (response.Status == SIPResponseStatusCodesEnum.Ok)
            {

                VariableType variable = ResMessageOK(response.Body);
                switch (variable)
                {
                    case VariableType.RealMedia:    //实时视频
                        if (SipInviteVideoOK != null)
                        {
                            SipInviteVideoOK(response);
                        }
                        break;
                    case VariableType.ItemList:     //设备目录
                        break;
                    case VariableType.DeviceInfo:   //设备信息
                        break;
                }
            }
            else if (response.Status == SIPResponseStatusCodesEnum.BadRequest)
            {
                logger.Fatal("real video invite badRequest");
            }
        }

        /// <summary>
        /// sip响应消息指令
        /// </summary>
        /// <param name="response">sip响应</param>
        private VariableType ResMessageOK(string body)
        {
            DeviceItemsRes devItemRes = DeviceItemsRes.Instance.Read(body);
            DeviceRes devRes = DeviceRes.Instance.Read(body);
            RealVideoRes realRes = RealVideoRes.Instance.Read(body);

            VariableType variable = VariableType.Unknown;
            if (devItemRes != null && devItemRes.Query != null)         //设备目录查询响应
            {
                variable = devItemRes.Query.Variable;
            }
            else if (devRes != null && devRes.Query != null)        //设备信息查询响应
            {
                variable = devRes.Query.Variable;
            }
            else if (realRes != null && realRes.Variable != VariableType.Unknown)       //实时视频请求响应   
            {
                variable = realRes.Variable;
            }
            return variable;
        }

        /// <summary>
        /// 处理通知请求消息
        /// </summary>
        /// <param name="request">通知请求</param>
        private void ProcessNotifyReqMessage(SIPRequest request)
        {
            Catalog cata = Catalog.Instance.Read(request.Body);
            if (CatalogHandler != null)
            {
                CatalogHandler(cata);
            }
            SIPResponse notifyRes = GetOkResponse(request);
            Response res = new Response()
            {
                Variable = VariableType.Catalog,
                Result = 0
            };
            string xmlBody = Response.Instance.Save<Response>(res);
            notifyRes.Body = xmlBody;
            m_sipTransport.SendResponse(notifyRes);
        }

        /// <summary>
        /// 处理DO请求消息
        /// </summary>
        /// <param name="request">do请求</param>
        private void ProcessDoReqMessage(SIPRequest request)
        {
            //心跳请求
            KeepAliveReq req = KeepAliveReq.Instance.Read(request.Body);
            VariableType varibale = VariableType.Unknown;
            if (req != null)
            {
                varibale = req.NotifyMsg.Variable;
            }
            if (varibale == VariableType.KeepAlive)
            {
                DoResKeepAlive(request);
            }
        }

        /// <summary>
        /// 回复DO消息心跳响应
        /// </summary>
        /// <param name="request">心跳请求</param>
        private void DoResKeepAlive(SIPRequest request)
        {
            SIPResponse doRes = GetOkResponse(request);
            Response res = new Response()
            {
                Variable = VariableType.KeepAlive,
                Result = 0
            };
            string xmlBody = Response.Instance.Save<Response>(res);
            doRes.Body = xmlBody;
            m_sipTransport.SendResponse(doRes);
        }

        #region 注册请求
        /// <summary>
        /// 处理注册请求消息
        /// </summary>
        /// <param name="localSIPEndPoint">本地终结点</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <param name="registerRequest">注册请求</param>
        private void ProcessRegisterReqMessage(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest registerRequest)
        {
            SIPSorceryPerformanceMonitor.IncrementCounter(SIPSorceryPerformanceMonitor.REGISTRAR_REGISTRATION_REQUESTS_PER_SECOND);

            int requestedExpiry = GetRequestedExpiry(registerRequest);

            if (registerRequest.Header.To == null)
            {
                logger.Debug("Bad register request, no To header from " + remoteEndPoint + ".");
                SIPResponse badReqResponse = SIPTransport.GetResponse(registerRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing To header");
                m_sipTransport.SendResponse(badReqResponse);
            }
            else if (registerRequest.Header.To.ToURI.User.IsNullOrBlank())
            {
                logger.Debug("Bad register request, no To user from " + remoteEndPoint + ".");
                SIPResponse badReqResponse = SIPTransport.GetResponse(registerRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing username on To header");
                m_sipTransport.SendResponse(badReqResponse);
            }
            else if (registerRequest.Header.Contact == null || registerRequest.Header.Contact.Count == 0)
            {
                logger.Debug("Bad register request, no Contact header from " + remoteEndPoint + ".");
                SIPResponse badReqResponse = SIPTransport.GetResponse(registerRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing Contact header");
                m_sipTransport.SendResponse(badReqResponse);
            }
            else if (requestedExpiry > 0 && requestedExpiry < MINIMUM_EXPIRY_SECONDS)
            {
                logger.Debug("Bad register request, no expiry of " + requestedExpiry + " to small from " + remoteEndPoint + ".");
                SIPResponse tooFrequentResponse = GetErrorResponse(registerRequest, SIPResponseStatusCodesEnum.IntervalTooBrief, null);
                tooFrequentResponse.Header.MinExpires = MINIMUM_EXPIRY_SECONDS;
                m_sipTransport.SendResponse(tooFrequentResponse);
            }
            else
            {
                if (m_registerQueue.Count < MAX_REGISTER_QUEUE_SIZE)
                {
                    SIPNonInviteTransaction registrarTransaction = m_sipTransport.CreateNonInviteTransaction(registerRequest, remoteEndPoint, localSIPEndPoint, null);
                    lock (m_registerQueue)
                    {
                        m_registerQueue.Enqueue(registrarTransaction);
                    }
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingInProgress, "Register queued for " + registerRequest.Header.To.ToURI.ToString() + ".", null));
                }
                else
                {
                    logger.Error("Register queue exceeded max queue size " + MAX_REGISTER_QUEUE_SIZE + ", overloaded response sent.");
                    SIPResponse overloadedResponse = SIPTransport.GetResponse(registerRequest, SIPResponseStatusCodesEnum.TemporarilyUnavailable, "Registrar overloaded, please try again shortly");
                    m_sipTransport.SendResponse(overloadedResponse);
                }

                m_registerARE.Set();
            }
        }

        /// <summary>
        /// 处理注册请求队列线程
        /// </summary>
        /// <param name="threadName"></param>
        private void ProcessRegisterRequest(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                while (!Stop)
                {
                    if (m_registerQueue.Count > 0)
                    {
                        try
                        {
                            SIPNonInviteTransaction registrarTransaction = null;
                            lock (m_registerQueue)
                            {
                                registrarTransaction = m_registerQueue.Dequeue();
                            }

                            if (registrarTransaction != null)
                            {
                                DateTime startTime = DateTime.Now;
                                SipResultEnum result = Register(registrarTransaction);
                                TimeSpan duration = DateTime.Now.Subtract(startTime);
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegistrarTiming, "register result=" + result.ToString() + ", time=" + duration.TotalMilliseconds + "ms, user=" + registrarTransaction.TransactionRequest.Header.To.ToURI.User + ".", null));
                            }
                        }
                        catch (InvalidOperationException invalidOpExcp)
                        {
                            // This occurs when the queue is empty.
                            logger.Warn("InvalidOperationException ProcessRegisterRequest Register Job. " + invalidOpExcp.Message);
                        }
                        catch (Exception regExcp)
                        {
                            logger.Error("Exception ProcessRegisterRequest Register Job. " + regExcp.Message);
                        }
                    }
                    else
                    {
                        m_registerARE.WaitOne(MAX_PROCESS_REGISTER_SLEEP);
                    }
                }

                logger.Warn("ProcessRegisterRequest thread " + Thread.CurrentThread.Name + " stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception ProcessRegisterRequest (" + Thread.CurrentThread.Name + "). " + excp.Message);
            }
        }

        /// <summary>
        /// 处理注册消息请求
        /// </summary>
        /// <param name="registerTransaction"></param>
        /// <returns></returns>
        private SipResultEnum Register(SIPTransaction registerTransaction)
        {
            try
            {
                SIPRequest sipRequest = registerTransaction.TransactionRequest;
                SIPURI registerURI = sipRequest.URI;
                SIPToHeader toHeader = sipRequest.Header.To;
                string toUser = toHeader.ToURI.User;
                string canonicalDomain = m_strictRealmHandling ? GetCanonicalDomain_External(toHeader.ToURI.Host, true) : toHeader.ToURI.Host;
                int requestedExpiry = GetRequestedExpiry(sipRequest);

                if (canonicalDomain == null)
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Warn, "Register request for " + toHeader.ToURI.Host + " rejected as no matching domain found.", null));
                    SIPResponse noDomainResponse = GetErrorResponse(sipRequest, SIPResponseStatusCodesEnum.Forbidden, "Domain not serviced");
                    registerTransaction.SendFinalResponse(noDomainResponse);
                    return SipResultEnum.DomainNotServiced;
                }

                SIPAccount sipAccount = GetSIPAccount_External(s => s.SIPUsername == toUser && s.SIPDomain == canonicalDomain);

                SIPRequestAuthenticationResult authenticationResult = SIPRequestAuthenticator_External(registerTransaction.LocalSIPEndPoint, registerTransaction.RemoteEndPoint, sipRequest, sipAccount, FireProxyLogEvent);

                if (!authenticationResult.Authenticated)
                {
                    // 401 Response with a fresh nonce needs to be sent.
                    SIPResponse authReqdResponse = SIPTransport.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                    authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                    registerTransaction.SendFinalResponse(authReqdResponse);

                    if (authenticationResult.ErrorResponse == SIPResponseStatusCodesEnum.Forbidden)
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Warn, "Forbidden " + toUser + "@" + canonicalDomain + " does not exist, " + sipRequest.Header.ProxyReceivedFrom + ", " + sipRequest.Header.UserAgent + ".", null));
                        return SipResultEnum.Forbidden;
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Registrar, "Authentication required for " + toUser + "@" + canonicalDomain + " from " + sipRequest.Header.ProxyReceivedFrom + ".", toUser));
                        return SipResultEnum.AuthenticationRequired;
                    }
                }
                else
                {

                    if (sipRequest.Header.Contact == null || sipRequest.Header.Contact.Count == 0)
                    {
                        // No contacts header to update bindings with, return a list of the current bindings.
                        //List<SIPRegistrarBinding> bindings = m_registrarBindingsManager.GetBindings(sipAccount.Id);
                        //List<SIPContactHeader> contactsList = m_registrarBindingsManager.GetContactHeader(); // registration.GetContactHeader(true, null);
                        //if (bindings != null)
                        //{
                        //sipRequest.Header.Contact = GetContactHeader(bindings);
                        //}

                        SIPResponse okResponse = GetOkResponse(sipRequest);
                        registerTransaction.SendFinalResponse(okResponse);
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegisterSuccess, "Empty registration request successful for " + toUser + "@" + canonicalDomain + " from " + sipRequest.Header.ProxyReceivedFrom + ".", toUser));
                    }
                    else
                    {
                        SIPEndPoint uacRemoteEndPoint = SIPEndPoint.TryParse(sipRequest.Header.ProxyReceivedFrom) ?? registerTransaction.RemoteEndPoint;
                        SIPEndPoint proxySIPEndPoint = SIPEndPoint.TryParse(sipRequest.Header.ProxyReceivedOn);
                        SIPEndPoint registrarEndPoint = registerTransaction.LocalSIPEndPoint;

                        SIPResponseStatusCodesEnum updateResult = SIPResponseStatusCodesEnum.Ok;
                        //string updateMessage = null;

                        DateTime startTime = DateTime.Now;

                        //List<SIPRegistrarBinding> bindingsList = m_registrarBindingsManager.UpdateBindings(
                        //    sipAccount,
                        //    proxySIPEndPoint,
                        //    uacRemoteEndPoint,
                        //    registrarEndPoint,
                        //    //sipRequest.Header.Contact[0].ContactURI.CopyOf(),
                        //    sipRequest.Header.Contact,
                        //    sipRequest.Header.CallId,
                        //    sipRequest.Header.CSeq,
                        //    //sipRequest.Header.Contact[0].Expires,
                        //    sipRequest.Header.Expires,
                        //    sipRequest.Header.UserAgent,
                        //    out updateResult,
                        //    out updateMessage);

                        //int bindingExpiry = GetBindingExpiry(bindingsList, sipRequest.Header.Contact[0].ContactURI.ToString());
                        TimeSpan duration = DateTime.Now.Subtract(startTime);
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegistrarTiming, "Binding update time for " + toUser + "@" + canonicalDomain + " took " + duration.TotalMilliseconds + "ms.", null));

                        if (updateResult == SIPResponseStatusCodesEnum.Ok)
                        {
                            string proxySocketStr = (proxySIPEndPoint != null) ? " (proxy=" + proxySIPEndPoint.ToString() + ")" : null;

                            //int bindingCount = 1;
                            //foreach (SIPRegistrarBinding binding in bindingsList)
                            //{
                            //    string bindingIndex = (bindingsList.Count == 1) ? String.Empty : " (" + bindingCount + ")";
                            //    //FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegisterSuccess, "Registration successful for " + toUser + "@" + canonicalDomain + " from " + uacRemoteEndPoint + proxySocketStr + ", binding " + binding.ContactSIPURI.ToParameterlessString() + ";expiry=" + binding.Expiry + bindingIndex + ".", toUser));
                            //    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegisterSuccess, "Registration successful for " + toUser + "@" + canonicalDomain + " from " + uacRemoteEndPoint + ", binding " + binding.ContactSIPURI.ToParameterlessString() + ";expiry=" + binding.Expiry + bindingIndex + ".", toUser));
                            //    //FireProxyLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate, toUser, uacRemoteEndPoint, sipAccount.Id.ToString()));
                            //    bindingCount++;
                            //}

                            // The standard states that the Ok response should contain the list of current bindings but that breaks some UAs. As a 
                            // compromise the list is returned with the Contact that UAC sent as the first one in the list.
                            //bool contactListSupported = m_userAgentConfigs.GetUserAgentContactListSupport(sipRequest.Header.UserAgent);
                            //if (contactListSupported)
                            //{
                            //    sipRequest.Header.Contact = GetContactHeader(bindingsList);
                            //}
                            //else
                            //{
                            //    // Some user agents can't match the contact header if the expiry is added to it.
                            //    sipRequest.Header.Contact[0].Expires = GetBindingExpiry(bindingsList, sipRequest.Header.Contact[0].ContactURI.ToString()); ;
                            //}

                            SIPResponse okResponse = GetOkResponse(sipRequest);

                            // If a request was made for a switchboard token and a certificate is available to sign the tokens then generate it.
                            //if (sipRequest.Header.SwitchboardTokenRequest > 0 && m_switchbboardRSAProvider != null)
                            //{
                            //    SwitchboardToken token = new SwitchboardToken(sipRequest.Header.SwitchboardTokenRequest, sipAccount.Owner, uacRemoteEndPoint.Address.ToString());

                            //    lock (m_switchbboardRSAProvider)
                            //    {
                            //        token.SignedHash = Convert.ToBase64String(m_switchbboardRSAProvider.SignHash(Crypto.GetSHAHash(token.GetHashString()), null));
                            //    }

                            //    string tokenXML = token.ToXML(true);
                            //    logger.Debug("Switchboard token set for " + sipAccount.Owner + " with expiry of " + token.Expiry + "s.");
                            //    okResponse.Header.SwitchboardToken = Crypto.SymmetricEncrypt(sipAccount.SIPPassword, sipRequest.Header.AuthenticationHeader.SIPDigest.Nonce, tokenXML);
                            //}
                            //_registerOK = true;
                            registerTransaction.SendFinalResponse(okResponse);
                        }
                        else
                        {
                            // The binding update failed even though the REGISTER request was authorised. This is probably due to a 
                            // temporary problem connecting to the bindings data store. Send Ok but set the binding expiry to the minimum so
                            // that the UA will try again as soon as possible.
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Error, "Registration request successful but binding update failed for " + toUser + "@" + canonicalDomain + " from " + registerTransaction.RemoteEndPoint + ".", toUser));
                            sipRequest.Header.Contact[0].Expires = MINIMUM_EXPIRY_SECONDS;
                            SIPResponse okResponse = GetOkResponse(sipRequest);
                            registerTransaction.SendFinalResponse(okResponse);
                        }
                    }

                    return SipResultEnum.Authenticated;
                }
            }
            catch (Exception excp)
            {
                string regErrorMessage = "Exception registrarcore registering. " + excp.Message + "\r\n" + registerTransaction.TransactionRequest.ToString();
                logger.Error(regErrorMessage);
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Error, regErrorMessage, null));

                try
                {
                    SIPResponse errorResponse = GetErrorResponse(registerTransaction.TransactionRequest, SIPResponseStatusCodesEnum.InternalServerError, null);
                    registerTransaction.SendFinalResponse(errorResponse);
                }
                catch { }

                return SipResultEnum.Error;
            }
        }
        #endregion

        private int GetRequestedExpiry(SIPRequest registerRequest)
        {
            int contactHeaderExpiry = (registerRequest.Header.Contact != null && registerRequest.Header.Contact.Count > 0) ? registerRequest.Header.Contact[0].Expires : -1;
            return (contactHeaderExpiry == -1) ? registerRequest.Header.Expires : contactHeaderExpiry;
        }

        private int GetBindingExpiry(List<SIPRegistrarBinding> bindings, string bindingURI)
        {
            if (bindings == null || bindings.Count == 0)
            {
                return -1;
            }
            else
            {
                foreach (SIPRegistrarBinding binding in bindings)
                {
                    if (binding.ContactURI == bindingURI)
                    {
                        return binding.Expiry;
                    }
                }
                return -1;
            }
        }

        /// <summary>
        /// Gets a SIP contact header for this address-of-record based on the bindings list.
        /// </summary>
        /// <returns></returns>
        private List<SIPContactHeader> GetContactHeader(List<SIPRegistrarBinding> bindings)
        {
            if (bindings != null && bindings.Count > 0)
            {
                List<SIPContactHeader> contactHeaderList = new List<SIPContactHeader>();

                foreach (SIPRegistrarBinding binding in bindings)
                {
                    SIPContactHeader bindingContact = new SIPContactHeader(null, binding.ContactSIPURI);
                    bindingContact.Expires = Convert.ToInt32(binding.ExpiryTime.Subtract(DateTime.UtcNow).TotalSeconds % Int32.MaxValue);
                    contactHeaderList.Add(bindingContact);
                }

                return contactHeaderList;
            }
            else
            {
                return null;
            }
        }

        private SIPResponse GetOkResponse(SIPRequest sipRequest)
        {
            try
            {
                SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                SIPHeader requestHeader = sipRequest.Header;
                //okResponse.Header = new SIPHeader(requestHeader.Contact, requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);
                okResponse.Header = new SIPHeader(requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);
                // RFC3261 has a To Tag on the example in section "24.1 Registration".
                if (okResponse.Header.To.ToTag == null || okResponse.Header.To.ToTag.Trim().Length == 0)
                {
                    okResponse.Header.To.ToTag = CallProperties.CreateNewTag();
                }
                okResponse.Header.ContentType = "application/DDCP";
                okResponse.Header.Expires = requestHeader.Expires;
                okResponse.Header.CSeqMethod = requestHeader.CSeqMethod;
                okResponse.Header.Vias = requestHeader.Vias;
                okResponse.Header.MaxForwards = Int32.MinValue;
                okResponse.Header.SetDateHeader();
                okResponse.Header.UserAgent = m_userAgent;

                return okResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetOkResponse. " + excp.Message);
                throw excp;
            }
        }

        private SIPResponse GetErrorResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum errorResponseCode, string errorMessage)
        {
            try
            {
                SIPResponse errorResponse = SIPTransport.GetResponse(sipRequest, errorResponseCode, null);
                if (errorMessage != null)
                {
                    errorResponse.ReasonPhrase = errorMessage;
                }

                SIPHeader requestHeader = sipRequest.Header;
                SIPHeader errorHeader = new SIPHeader(requestHeader.Contact, requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);

                if (errorHeader.To.ToTag == null || errorHeader.To.ToTag.Trim().Length == 0)
                {
                    errorHeader.To.ToTag = CallProperties.CreateNewTag();
                }

                errorHeader.CSeqMethod = requestHeader.CSeqMethod;
                errorHeader.Vias = requestHeader.Vias;
                errorHeader.MaxForwards = Int32.MinValue;
                errorHeader.UserAgent = m_userAgent;

                errorResponse.Header = errorHeader;

                return errorResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetErrorResponse. " + excp.Message);
                throw excp;
            }
        }

        public SIPResponse GetResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponseStatusCodesEnum responseCode, string reasonPhrase, SIPRequest request)
        {
            try
            {
                SIPResponse response = new SIPResponse(responseCode, reasonPhrase, localSIPEndPoint);
                SIPSchemesEnum sipScheme = (localSIPEndPoint.Protocol == SIPProtocolsEnum.tls) ? SIPSchemesEnum.sips : SIPSchemesEnum.sip;
                SIPFromHeader from = request.Header.From;
                from.FromTag = request.Header.From.FromTag;
                SIPToHeader to = request.Header.To;
                response.Header = new SIPHeader(from, to, request.Header.CSeq, request.Header.CallId);
                response.Header.CSeqMethod = request.Header.CSeqMethod;
                response.Header.Vias = request.Header.Vias;
                response.Header.UserAgent = m_userAgent;
                response.Header.CSeq = request.Header.CSeq;

                if (response.Header.To.ToTag == null || request.Header.To.ToTag.Trim().Length == 0)
                {
                    response.Header.To.ToTag = CallProperties.CreateNewTag();
                }

                return response;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTransport GetResponse. " + excp.Message);
                throw;
            }
        }

        private SIPResponse GetAuthReqdResponse(SIPRequest sipRequest, string nonce, string realm)
        {
            try
            {
                SIPResponse authReqdResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Unauthorised, null);
                SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, realm, nonce);
                SIPHeader requestHeader = sipRequest.Header;
                SIPHeader unauthHeader = new SIPHeader(requestHeader.Contact, requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);

                if (unauthHeader.To.ToTag == null || unauthHeader.To.ToTag.Trim().Length == 0)
                {
                    unauthHeader.To.ToTag = CallProperties.CreateNewTag();
                }

                unauthHeader.CSeqMethod = requestHeader.CSeqMethod;
                unauthHeader.Vias = requestHeader.Vias;
                unauthHeader.AuthenticationHeader = authHeader;
                unauthHeader.MaxForwards = Int32.MinValue;
                unauthHeader.UserAgent = m_userAgent;

                authReqdResponse.Header = unauthHeader;

                return authReqdResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetAuthReqdResponse. " + excp.Message);
                throw excp;
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            //if (m_registrarLogEvent != null)
            //{
            //    try
            //    {
            //        m_registrarLogEvent(monitorEvent);
            //    }
            //    catch (Exception excp)
            //    {
            //        logger.Error("Exception FireProxyLogEvent RegistrarCore. " + excp.Message);
            //    }
            //}
            logger.Info("FireProxyLog Cancel");
        }
    }
}
