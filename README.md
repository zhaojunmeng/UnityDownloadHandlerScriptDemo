# 使用UnityWebRequest来实现断点续传，并且不会产生额外GC的一个Demo

关键词：Unity UnityWebRequest 断点续传 无额外GC

本Demo，基于Unity2018.4.36f1版本

## 原理

断点续传指的是，如果下载过程中断，支持下次从中断的部分继续下载未完成的部分，而不用再重新从头开始下载。

### 实现流程

* 假设我们要下载remoteFile到本地的localFile
* 首先读取localFile文件的大小：localFileSize，单位字节，localFileSize就表示之前已经下载的大小
  * 如果localFile不存在，那么localFileSize就是0
* 通过HTTP Range请求头，从未下载的部分继续下载
  * 即请求从localFileSize到文件末尾的部分来下载

### HTTP Range Request Header

```Text
Range 是一个请求首部，告知服务器返回文件的哪一部分。

从range-start请求到文件结尾的语法：
Range: <unit>=<range-start>-
参数解释：
<unit>: 范围所采用的单位，通常是字节（bytes）。
<range-start>: 一个整数，表示在特定单位下，范围的起始值。
```

Range请求头的完整参考见：
<https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Headers/Range>

### UnityWebRequest中，设置Range请求头的代码

```C#
...
var LocalFileSize = new System.IO.FileInfo(localFile).Length;
// “bytes=<range-start>-”格式的意思是，请求从range-start到文件结尾的所有bytes
// 这里就是从本地已下载文件大小，请求到文件末尾的所有bytes
unityWebRequest.SetRequestHeader("Range", "bytes=" + LocalFileSize + "-");
// 请求服务器，执行下载
unityWebRequest.SendWebRequest();
...
```

## 代码实现细节

通过搜索，网上能找到的UnityWebRequest断点续传代码模板，大概两种的实现方式：

* 不继承DownloadHandlerScript, 直接使用 UnityWebRequest.downloadHandler.data 的方式。
* 继承 DownloadHandlerScript 类的方式

### 不推荐的方式（有严重的GC问题）

不继承DownloadHandlerScript, 直接使用 UnityWebRequest.downloadHandler.data的方式是不推荐的，核心原因是下载多大的文件，就会分配多大的内存！
而Unity的Mono内存，被撑高之后是无法回落的。下载的文件过大，甚至会引起OOM(out of memory)崩溃。

代码如下

```C#
    // 前面省略细节
    var req = UnityWebRequest.Get(fileUrl);
    req.SetRequestHeader("Range", "bytes=" + fileLength + "-");
    var op = req.SendWebRequest();

    // 这个是已写入的bytes的偏移量
    var index = 0;
    while (!op.isDone)
    {
        yield return null;
        // 问题在这里，这个data和被下载文件的大小是一样的
        byte[] buff = req.downloadHandler.data;
        if (buff != null)
        {
            var length = buff.Length - index;
            // 每次根据上次写入记录的偏移量，写入新下载的data
            fs.Write(buff, index, length);
            index += length;
            fileLength += length;

            onProgress(fileLength);
        }
    }
    // 后面省略细节
```

### 推荐的方式

继承DownloadHandlerScript，并且在基类构造函数中，传递一个固定大小的Buffer作为下载的缓冲区。

```C#
public class DownloadHandlerFileRange : DownloadHandlerScript
{

    // base(new byte[1024 * 1024])就是传递下载用的固定大小的Buffer
    public DownloadHandlerFileRange(string path, UnityWebRequest request) : base(new byte[1024 * 1024])
    {
        ...
    }

    // 收到数据，写文件
    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        ...
        FileStream.Write(data, 0, dataLength);
        ...        
        return true;
    }
}
```

查看DownloadHandlerScript的构造函数文档，可以看到，上述的代码，在下载过程中，只分配了缓冲区大小的内存，下载的文件再大，也不会造成超过缓冲区大小的内存分配。这样整个下载过程中，不会产生新的GC，内存是平稳的。

```Text
public DownloadHandlerScript (byte[] preallocatedBuffer);

创建可通过重复使用预分配的缓冲区将数据传递给回调的 DownloadHandlerScript。

此构造函数会将此 DownloadHandlerScript 置于预分配模式。这会影响 DownloadHandler.ReceiveData 回调的操作。

在预分配模式下，系统将重复使用 preallocatedBuffer 字节数组以将数据传递给 DownloadHandler.ReceiveData 回调，而非每次都会分配新缓冲区。系统不会在每次使用时都将数组归零，因此必须使用 DownloadHandler.ReceiveData 的 dataLength 参数来查看哪些字节是新字节。

在这种模式下，DownloadHandlerScript 不会在下载或处理 HTTP 响应数据时分配任何内存。如果您的用例需要避免垃圾收集操作，建议您采用预分配模式。

```

参考：<https://docs.unity3d.com/cn/2019.4/ScriptReference/Networking.DownloadHandlerScript-ctor.html>

完整的代码见：<https://github.com/zhaojunmeng/UnityDownloadHandlerScriptDemo/tree/main/Assets/Scripts>

整个实现参考了：
<https://gist.github.com/TouiSoraHe/19f2afeee1334cb8e42b3c8f2c283a65>

## 注意事项

### Strip的问题

问题：Android真机上，继承自DownloadHandlerScript的子类无法收到任何回调的问题

原因：开启了Managed code stripping，且“Managed Stripping Level”在Medium及以上。导致回调代码被错误的strip掉了，回调无法触发。

解决方案：在Assets/目录中，link.xml中（如果不存在，就自行添加），添加下面的部分，不裁剪UnityWebRequestModule的相关类。

```xml
<?xml version="1.0" encoding="utf-8"?>
<linker>
 <assembly fullname="UnityEngine.UnityWebRequestModule" preserve="all" />
</linker>
```

关于Managed code stripping的相关参考：<https://docs.unity3d.com/cn/current/Manual/ManagedCodeStripping.html>

### HTTP: 416

问题：下载文件，HTTP的返回码返回了416

```Text
HTTP 416 Range Not Satisfiable 错误状态码意味着服务器无法处理所请求的数据区间。最常见的情况是所请求的数据区间不在文件范围之内，也就是说，Range 首部的值，虽然从语法上来说是没问题的，但是从语义上来说却没有意义。
```

解释：我们下载一个a.zip，有两种方式：
一种是localFile命名为a.zip，直接开始断点续传下载
另一种是命名为a.zip.tmp，开始断点续传下载，下载完成之后，重命名为a.zip。

第一种方式，有可能本地文件已经下载完成，断点续传的时候，range-start的值已经就是文件的完整大小了，这个时候，就会返回HTTP416。这个时候就要自己判断一下，文件是否已经下载完毕了。

416返回码参考：<https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/416>
