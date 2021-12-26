using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class DownloadHandlerDemo
{
    public static IEnumerator DownloadPatchSupportResume(string fileUrl, string fileName, Action<ulong> onProgress)
    {
        // 这个变量设置为true的地方，使用者就需要自己写自己的错误处理代码了
        bool isError = false;
        var fullPath = Path.Combine(Application.persistentDataPath, fileName);

        using (UnityWebRequest request = UnityWebRequest.Get(fileUrl))
        {
            DownloadHandlerFileRange downloadHandler = new DownloadHandlerFileRange(fullPath, request);
            request.downloadHandler = downloadHandler;
            downloadHandler.DownloadedSizeUpdateEvent += onProgress;
            
            Debug.Log($"[Download] SendWebRequest start");
            
            yield return request.SendWebRequest();
            
            Debug.Log($"[Download] SendWebRequest done {request.isDone} {request.error}");
            if (request.isHttpError)
            {
                Debug.Log($"[Download] isHttpError true: {request.responseCode}");
                // 有2种方式：
                // 1. 下载的时候，文件名用.tmp这种临时的名字，下载成功之后rename，这样就不会让已经下载完毕的文件再去走下载
                // 2. 不适用临时文件名，那么在去请求Range的时候，可能就会遇到416Range非法，毕竟本地文件的Size已经等于文件总Length了
                // 这个时候就会遇到416，Range参数不对，我们认为是文件已经全部下载完成了，所以Range不对，这个时候不算错误
                if (request.responseCode != 416)
                {
                    isError = true;
                }
            }
            else if (!string.IsNullOrEmpty(request.error))
            {
                Debug.Log($"[Download] isNetworkError true: {request.error}");
                isError = true;
            }
            
            // 必须要手动清理，哪怕disposeDownloadHandlerOnDispose设置为true
            // DownloadHandlerFileRange.Dispose()是没办法被UnityWebRequest调用的
            downloadHandler.ManualDispose();
        }
    } 
}