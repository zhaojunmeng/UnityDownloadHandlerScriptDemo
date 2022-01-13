# UnityDownloadHandlerScriptDemo

一个使用UnityWebRequest来实现断点续传功能的Demo

Unity中，使用UnityWebRequest进行断点续传并且不会额外GC的方式

本Demo，基于Unity2018.4.36f1版本

原理

断点续传原理比较简单，首先检测本地已经下载了多少，直接从未下载的部分继续下载

需要使用HTTP的Range请求头

```C#
LocalFileSize = new System.IO.FileInfo(path).Length;
// 请求服务器，下载本地未下载的部分
// 格式的意思是，下载 LocalFileSize到文件末尾的所有字节
unityWebRequest.SetRequestHeader("Range", "bytes=" + LocalFileSize + "-");
```

请求头Range的格式参考：
https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Headers/Range 

代码实现方式：
通过搜索，网上能找到的UnityWebRequest断点续传代码模板，大概两种的实现方式：
一是继承DownloadHandlerScript类的方式，另一种是不继承直接使用UnityWebRequest的方式。

不推荐的方式：
不继承的方式是不推荐的，代码如下
这里的问题主要是GC，真机实测，下载过程中，内存会持续增长，文件越大内存增长越大。
下载的文件过大，甚至会引起OOM(out of memory)崩溃。

```C#
    var req = UnityWebRequest.Get(fileUrl);
    req.SetRequestHeader("Range", "bytes=" + fileLength + "-" + totalLength);
    var op = req.SendWebRequest();

    var index = 0;

        while (!op.isDone)
    {
        yield return null;
        byte[] buff = req.downloadHandler.data;
        if (buff != null)
        {
            var length = buff.Length - index;
            fs.Write(buff, index, length);
            index += length;
            fileLength += length;

            onProgress((ulong)fileLength);
        }
    }
```

推荐的方式：

继承DownloadHandlerScript，并且在基类构造函数中，传递一个固定大小的Buffer作为下载的缓冲区。

```C#
// base(new byte[1024 * 1024])就是传递下载用的固定大小的Buffer
public DownloadHandlerFileRange(string path, UnityWebRequest request) : base(new byte[1024 * 1024])
{
   ...
}
```

这样整个下载过程中，不会产生新的GC，内存是平稳的。

完整的代码见：https://gist.github.com/TouiSoraHe/19f2afeee1334cb8e42b3c8f2c283a65 


原因是：
https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Networking.DownloadHandler.ReceiveData.html 

https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Networking.DownloadHandlerScript-ctor.html

注意事项

Strip的问题

Android真机上，自定义的DownloadHandler无法收到任何回调的问题

Managed Stripping Level: Medium

```xml
<?xml version="1.0" encoding="utf-8"?>
<linker>
	<assembly fullname="UnityEngine.UnityWebRequestModule" preserve="all" />
</linker>
```

HTTP: 416