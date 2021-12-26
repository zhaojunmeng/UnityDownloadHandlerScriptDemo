using UnityEngine.Networking;
using System.IO;
using System;
using UnityEngine;

/// <summary>
/// 使用方式:
/// UnityWebRequest unityWebRequest = new UnityWebRequest("url");
/// unityWebRequest.downloadHandler = new DownloadHandlerFileRange("文件保存的路径", unityWebRequest);
/// unityWebRequest.SendWebRequest();
/// 
/// 参考自： https://gist.github.com/TouiSoraHe/19f2afeee1334cb8e42b3c8f2c283a65
/// 更完整的使用方式见：DownloadPatchSupportResume()方法
/// </summary>
public class DownloadHandlerFileRange : DownloadHandlerScript
{
    /// <summary>
    /// 文件正式开始下载事件,此事件触发以后即可获取到文件的总大小
    /// </summary>
    public event System.Action StartDownloadEvent;

    /// <summary>
    /// ReceiveData()之后调用的回调，参数是本地文件已经下载的总大小
    /// </summary>
    public event System.Action<ulong> DownloadedSizeUpdateEvent;

    #region 属性
    /// <summary>
    /// 下载速度,单位:KB/S 保留两位小数
    /// </summary>
    public float Speed
    {
        get
        {
            return ((int)(DownloadSpeed / 1024 * 100)) / 100.0f;
        }
    }

    /// <summary>
    /// 文件的总大小
    /// </summary>
    public long FileSize
    {
        get
        {
            return TotalFileSize;
        }
    }

    /// <summary>
    /// 下载进度[0,1]
    /// </summary>
    public float DownloadProgress
    {
        get
        {
            return GetProgress();
        }
    }
    #endregion

    #region 公共方法
    /// <summary>
    /// 使用1MB的缓存,在补丁2017.2.1p1中对DownloadHandlerScript的优化中,目前最大传入数据量也仅仅是1024*1024,再多也没用
    /// </summary>
    /// <param name="path">文件保存的路径</param>
    /// <param name="request">UnityWebRequest对象,用来获文件大小,设置断点续传的请求头信息</param>
    public DownloadHandlerFileRange(string path, UnityWebRequest request) : base(new byte[1024 * 1024])
    {
        Debug.Log($"[Download] DownloadHandlerFileRange start");
        Path = path;
        FileStream = new FileStream(Path, FileMode.Append, FileAccess.Write);
        unityWebRequest = request;
        if (File.Exists(path))
        {
            LocalFileSize = new System.IO.FileInfo(path).Length;
        }
        CurFileSize = LocalFileSize;
        unityWebRequest.SetRequestHeader("Range", "bytes=" + LocalFileSize + "-");
        Debug.Log($"[Download] DownloadHandlerFileRange end LocalFileSize {LocalFileSize}");
    }

    /// <summary>
    /// 清理资源,该方法没办法重写,只能隐藏,如果想要强制中止下载,并清理资源(UnityWebRequest.Dispose()),该方法并不会被调用,这让人很苦恼
    /// </summary>
    public new void Dispose()
    {
        Debug.Log("[Download] Dispose()");
        Clean();
    }
    
    
    public void ManualDispose()
    {
        Debug.Log("[Download] ManualDispose()");
        Clean();
    }
    #endregion

    #region 私有方法
    /// <summary>
    /// 关闭文件流
    /// </summary>
    private void Clean()
    {
        Debug.Log("[Download] Clean()");
        DownloadSpeed = 0.0f;
        if (FileStream != null)
        {
            FileStream.Flush();
            FileStream.Dispose();
            FileStream = null;
        }
    }
    #endregion

    #region 私有继承的方法
    /// <summary>
    /// 下载完成后清理资源
    /// </summary>
    protected override void CompleteContent()
    {
        Debug.Log("[Download] CompleteContent()");
        base.CompleteContent();
        Clean();
    }

    /// <summary>
    /// 调用UnityWebRequest.downloadHandler.data属性时,将会调用该方法,用于以byte[]的方式返回下载的数据,目前总是返回null
    /// </summary>
    /// <returns></returns>
    protected override byte[] GetData()
    {
        Debug.Log("[Download] GetData()");
        return null;
    }

    /// <summary>
    /// 调用UnityWebRequest.downloadProgress属性时,将会调用该方法,用于返回下载进度
    /// </summary>
    /// <returns></returns>
    protected override float GetProgress()
    {
        return TotalFileSize == 0 ? 0 : ((float)CurFileSize) / TotalFileSize;
    }

    /// <summary>
    /// 调用UnityWebRequest.downloadHandler.text属性时,将会调用该方法,用于以string的方式返回下载的数据,目前总是返回null
    /// </summary>
    /// <returns></returns>
    protected override string GetText()
    {
        return null;
    }

    //Note:当下载的文件数据大于2G时,该int类型的参数将会数据溢出,所以先自己通过响应头来获取长度,获取不到再使用参数的方式
    protected override void ReceiveContentLength(int contentLength)
    {
        Debug.Log($"[Download] ReceiveContentLength {contentLength}");
        string contentLengthStr = unityWebRequest.GetResponseHeader("Content-Length");
        
        if (!string.IsNullOrEmpty(contentLengthStr))
        {
            try
            {
                TotalFileSize = long.Parse(contentLengthStr);
            }
            catch (System.FormatException e)
            {
                UnityEngine.Debug.Log("获取文件长度失败,contentLengthStr:" + contentLengthStr + "," + e.Message);
                TotalFileSize = contentLength;
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.Log("获取文件长度失败,contentLengthStr:" + contentLengthStr + "," + e.Message);
                TotalFileSize = contentLength;
            }
        }
        else
        {
            TotalFileSize = contentLength;
        }
        //这里拿到的下载大小是待下载的文件大小,需要加上本地已下载文件的大小才等于总大小
        TotalFileSize += LocalFileSize;
        LastTime = UnityEngine.Time.time;
        LastDataSize = CurFileSize;
        if (StartDownloadEvent != null)
        {
            StartDownloadEvent();
        }
    }

    //在2017.3.0(包括该版本)以下的正式版本中存在一个性能上的问题
    //该回调方法有性能上的问题,每次传入的数据量最大不会超过65536(2^16)个字节,不论缓存区有多大
    //在下载速度中的体现,大约相当于每秒下载速度不会超过3.8MB/S
    //这个问题在 "补丁2017.2.1p1" 版本中被优化(2017.12.21发布)(https://unity3d.com/cn/unity/qa/patch-releases/2017.2.1p1)
    //(965165) - Web: UnityWebRequest: improve performance for DownloadHandlerScript.
    //优化后,每次传入数据量最大不会超过1048576(2^20)个字节(1MB),基本满足下载使用
    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength == 0 || unityWebRequest.responseCode > 400)
        {
            Debug.Log($"[Download] ReceiveData return false: data: {data} length: {dataLength} responseCode: {unityWebRequest.responseCode}");
            return false;
        }
        FileStream.Write(data, 0, dataLength);
        CurFileSize += dataLength;
        //统计下载速度
        if (UnityEngine.Time.time - LastTime >= 1.0f)
        {
            DownloadSpeed = (CurFileSize - LastDataSize) / (UnityEngine.Time.time - LastTime);
            LastTime = UnityEngine.Time.time;
            LastDataSize = CurFileSize;
        }
        
        DownloadedSizeUpdateEvent?.Invoke((ulong)CurFileSize);
        
        return true;
    }

    ~DownloadHandlerFileRange()
    {
        Debug.Log("[Download] ~DownloadHandlerFileRange()");
        Clean();
    }
    #endregion

    #region 私有字段
    private string Path;//文件保存的路径
    private FileStream FileStream;
    private UnityWebRequest unityWebRequest;
    private long LocalFileSize = 0;//本地已经下载的文件的大小
    private long TotalFileSize = 0;//文件的总大小
    private long CurFileSize = 0;//当前的文件大小
    private float LastTime = 0;//用作下载速度的时间统计
    private float LastDataSize = 0;//用来作为下载速度的大小统计
    private float DownloadSpeed = 0;//下载速度,单位:Byte/S
    #endregion
}