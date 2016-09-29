﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GLib.Net;
using GLib.Extension;
using System.IO;

namespace SIPSorcery.Servers.Packet
{
    public enum AVCode {
        PIX_FMT_YUV420P = 0,
        PIX_FMT_RGB32 = 6,
        CODEC_ID_AAC = 86018,
        CODEC_TYPE_VIDEO = 0,
        CODEC_TYPE_AUDIO = 1,
        CODEC_ID_H264 = 28,
        CODEC_ID_H263 = 5,
        CODEC_ID_FLV1 = 22,
        CODEC_ID_XVID = 63,
        CODEC_ID_MPEG4 = 13,
    }
 
    public partial class MediaFrame : IByteObj
    {
        public byte MediaFrameVersion;//0x00小版本，0x01全版本,0xff为命令（命令的内容见nRawType对应的枚举）
        /// <summary>
        /// 扩展字段，默认为1 
        /// 当MediaFrameVersion 为0x00或0x01，则该字段：0为不可被丢弃(一般只有首帧是不可丢弃的。)，1为可被丢弃,
        /// 当MediaFrameVersion为0xff时，该字段为命令类型，命令包不可丢弃
        /// </summary>
        public byte nEx = 1;//
        public byte nIsKeyFrame;	//是否为关键帧，0为非关键帧，1为关键帧
        public long nTimetick;		//时间辍
        public byte nIsAudio;		//是否为音频,0:视频,1:音频
        public int nSize;		//数据大小,紧跟着该结构后的数据媒体数据
        public int nOffset;//偏移量

        /// <summary>
        /// 编码器CODE
        /// </summary>
        public int nEncoder;
        /// <summary>
        /// 编码器名称
        /// </summary>
        public String EncodeName { get { return MediaFrame.GetGeneralEncodecName(this.nEncoder); } }

        /// <summary>
        /// H264里面的SPS长度
        /// </summary>
        public short nSPSLen;
        /// <summary>
        /// H264里面的PPS长度
        /// </summary>
        public short nPPSLen;
        /// <summary>
        /// 视频宽
        /// </summary>
        public int nWidth;
        /// <summary>
        /// 视频高
        /// </summary>
        public int nHeight;
        /// <summary>
        /// 采样率,speex一般为8000
        /// </summary>
        public int nFrequency;
        /// <summary>
        /// 1=单通道，2=双通道,speex编码一般为1
        /// </summary>
        public int nChannel;
        /// <summary>
        /// 0=8位,1=16位,一般为2
        /// </summary>
        public short nAudioFormat;
        /// <summary>
        /// 采集大小,speex 一般为160
        /// </summary>
        public short nSamples;
        /// <summary>
        /// 媒体数据
        /// </summary>
        public byte[] Data = new byte[0];

        public MediaFrame()
        {
        }

        public MediaFrame(byte[] buf)
            : this(new System.IO.MemoryStream(buf))
        {

        }
        public MediaFrame(Stream stream)
        {
            SetBytes(stream);
        }
        public void SetBytes(byte[] buf)
        {
            SetBytes(new System.IO.MemoryStream(buf));
        }
        //充填媒体帧
        public void SetBytes(Stream stream)
        {
            var br = new System.IO.BinaryReader(stream);
            MediaFrameVersion = br.ReadByte();
            nEx = br.ReadByte();
            nIsKeyFrame = br.ReadByte();
            nTimetick = br.ReadInt64();
            nIsAudio = br.ReadByte();
            nSize = br.ReadInt32();
            nOffset = br.ReadInt32();
            if (MediaFrameVersion == 1)
            {
                nEncoder = br.ReadInt32();
                if (nIsAudio == 0)
                {
                    nSPSLen = br.ReadInt16();
                    nPPSLen = br.ReadInt16();
                    nWidth = br.ReadInt32();
                    nHeight = br.ReadInt32();
                }
                else
                {
                    nFrequency = br.ReadInt32();
                    nChannel = br.ReadInt32();
                    nAudioFormat = br.ReadInt16();
                    nSamples = br.ReadInt16();
                }
            }
            Data = br.ReadBytes(nSize + nOffset);
            
        }
        //获取媒体帧对应字节数组
        public byte[] GetBytes()
        {
            var ms = new System.IO.MemoryStream();
            var bw = new System.IO.BinaryWriter(ms);
            bw.Write(MediaFrameVersion);
            bw.Write(nEx);
            bw.Write(nIsKeyFrame);
            bw.Write(nTimetick);
            bw.Write(nIsAudio);
            bw.Write(nSize);
            int _tOffset = nOffset;// 重置偏移量
            nOffset = 0;
            bw.Write(nOffset);

            if (MediaFrameVersion == 1)
            {
                bw.Write(nEncoder);
                if (nIsAudio == 0)
                {
                    bw.Write(nSPSLen);
                    bw.Write(nPPSLen);
                    bw.Write(nWidth);
                    bw.Write(nHeight);
                }
                else
                {
                    bw.Write(nFrequency);
                    bw.Write(nChannel);
                    bw.Write(nAudioFormat);
                    bw.Write(nSamples);
                }
            }
            bw.Write(Data, _tOffset, nSize);
            nOffset = _tOffset;
            return ms.ToArray();
        }

        public override string ToString()
        {
            return string.Format("nIsAudio:{0}  nIsKeyFrame:{1}  nTick:{2}  nSize:{3}   ", nIsAudio, nIsKeyFrame, nTimetick, nSize);
        }

        public bool IsAllowDiscard()
        {

            return !IsCommandMediaFrame() && !((MediaFrameVersion == 0 || MediaFrameVersion == 1) && nEx == 0);
        }
    }
    public partial class MediaFrame
    {

        public byte[] GetSPS() {
            if (this.nIsAudio == 0 && this.nIsKeyFrame == 1) {
                byte[] sps = new byte[nSPSLen];
                Array.Copy(Data, 4, sps, 0, nSPSLen);
                return sps;
            } else
                throw new Exception();
        }
        public byte[] GetPPS() {
            if (this.nIsAudio == 0 && this.nIsKeyFrame == 1) {
                byte[] pps = new byte[nPPSLen];
                Array.Copy(Data, nSPSLen + 8, pps, 0, nPPSLen);
                return pps;
            } else
                throw new Exception();
        }
        public bool IsCommandMediaFrame()
        {
            return  MediaFrameVersion ==0xff;
        }
        public MediaFrameCommandType GetCommandType() {
            if (!IsCommandMediaFrame())
                throw new Exception("");
            return (MediaFrameCommandType)nEx;
        }
        public MediaFrame CreateCommandMediaFrame(MediaFrameCommandType cmd)
        {
            return new MediaFrame()
            {
                MediaFrameVersion = 0xff,
                nEx = (byte)cmd,
                nIsKeyFrame = 0,
                nIsAudio = this.nIsAudio,
                nTimetick = 0,
                nSize = 0,
                Data = new byte[0],
            };
        }
        public static MediaFrame CreateCommandMediaFrame(bool isAudio, MediaFrameCommandType cmd)
        {
            return new MediaFrame()
            {
                MediaFrameVersion = 0xff,
                nEx = (byte)cmd,
                nIsKeyFrame = 0,
                nIsAudio = (byte)(isAudio ? 1 : 0),
                nTimetick = 0,
                nSize = 0,
                Data = new byte[0],
            };
        }
 
        public static int H264Encoder = GetGeneralEncoder("H264");
        public static int H263Encoder = GetGeneralEncoder("H263");
        public static int SPEXEncoder = GetGeneralEncoder("SPEX");
        public static int FLV1Encoder = GetGeneralEncoder("FLV1");
        public static int AAC_Encoder = GetGeneralEncoder("AAC_");
        public static int PCM_Encoder = GetGeneralEncoder("PCM_");

        public static int GetGeneralEncoder(String name)
        {
            name = name.ToUpper();
            byte[] buf = Encoding.UTF8.GetBytes(name);
            return BitConverter.ToInt32(buf, 0);
        }
        public static String GetGeneralEncodecName(int generalEncoder)
        {
            byte[] buf = BitConverter.GetBytes(generalEncoder);
            return Encoding.UTF8.GetString(buf).ToUpper();
        }
        public static int GetEncoderByAVCoderID(AVCode code_id)
        {
            switch (code_id)
            {
                case AVCode.CODEC_ID_H264:
                    return H264Encoder;
                case AVCode.CODEC_ID_H263:
                    return H263Encoder;
                case AVCode.CODEC_ID_FLV1:
                    return FLV1Encoder;
                case AVCode.CODEC_ID_XVID:
                    return GetGeneralEncoder("XVID");
                case AVCode.CODEC_ID_MPEG4:
                    return GetGeneralEncoder("MP4V");
            }
            return 0;
        }
        public static int ConverterToFFMPEGCoderID(int id)
        {
            var name = MediaFrame.GetGeneralEncodecName(id);
            if (name.EqIgnoreCase("H264"))
                return (int)AVCode.CODEC_ID_H264;
            if (name.EqIgnoreCase("H263"))
                return (int)AVCode.CODEC_ID_H263;
            if (name.EqIgnoreCase("FLV1"))
                return (int)AVCode.CODEC_ID_FLV1;
            if (name.EqIgnoreCase("XVID"))
                return (int)AVCode.CODEC_ID_XVID;
            if (name.EqIgnoreCase("MP4V"))
                return (int)AVCode.CODEC_ID_MPEG4;
            return -1;
        }
    }
    /// <summary>
    /// 媒体帧命令类型
    /// </summary>
    public enum MediaFrameCommandType : byte
    {
        /// <summary>
        /// 开始
        /// </summary>
        Start = 0x00,
        /// <summary>
        /// 停止 
        /// </summary>
        Stop = 0x01,
        /// <summary>
        /// 暂停
        /// </summary>
        Pause = 0x02,
        /// <summary>
        /// 继续
        /// </summary>
        Continue = 0x03,
        /// <summary>
        /// 重置播放位置
        /// </summary>
        ResetPos = 0x05,

        /// <summary>
        /// 音视频索引
        /// </summary>
        Index=0x10,
        /// <summary>
        /// 缩略图
        /// </summary>
        Thumbnail = 0x24,
    }
}
